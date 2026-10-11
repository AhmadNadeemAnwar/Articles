using System.Diagnostics;
using OtpNet;
using QRCoder;

// One-time helper for the admin portal's second sign-in factor.
//
//   dotnet run --project tools/Articles.AdminSetup -- you@example.com
//
// It makes a secret, shows it as a QR code to scan with an authenticator app (Google Authenticator,
// Authy, 1Password, Microsoft Authenticator...), checks that the app produces matching codes, and
// prints the one value to put on the host. Nothing is sent anywhere and nothing is written except a
// temporary QR image, which is deleted before this exits.

const string Issuer = "Articles Admin";

if (args.Contains("--check"))
{
    // Non-interactive self-test (no secret is printed): can this tool make a secret and verify its own codes?
    var key = KeyGeneration.GenerateRandomKey(20);
    var totp = new Totp(key);
    var ok = totp.VerifyTotp(totp.ComputeTotp(), out _, VerificationWindow.RfcSpecifiedNetworkDelay);
    Console.WriteLine(ok ? "Self-check passed." : "Self-check FAILED.");
    return ok ? 0 : 1;
}

if (args.Contains("--reset-password"))
{
    // Lost-password recovery. Articles sends no email, so the new password is hashed here and the
    // hash is put into the database by hand. The password itself never leaves this machine.
    //
    //   dotnet run --project tools/Articles.AdminSetup -- --reset-password you@example.com
    var account = args.FirstOrDefault(a => !a.StartsWith("--"))?.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(account) || !account.Contains('@'))
    {
        Console.WriteLine("Usage: dotnet run --project tools/Articles.AdminSetup -- --reset-password you@example.com");
        return 2;
    }

    Console.WriteLine();
    Console.WriteLine("Choose a new password: 10 or more characters, and not built from your email address.");
    Console.WriteLine("Nothing shows while you type.");

    string? password = null;
    for (var attempt = 1; attempt <= 5 && password is null; attempt++)
    {
        var first = ReadHidden("New password: ");
        var problem =
            first.Length < 10 ? "That is shorter than 10 characters." :
            first.Length > 128 ? "That is longer than 128 characters." :
            first.Distinct().Count() < 4 ? "That needs more variety." :
            first.Contains(account.Split('@')[0], StringComparison.OrdinalIgnoreCase) ? "Don't include your email address in it." :
            null;
        if (problem is not null) { Console.WriteLine(problem); continue; }

        if (ReadHidden("Type it again: ") != first) { Console.WriteLine("Those did not match."); continue; }
        password = first;
    }

    if (password is null)
    {
        Console.WriteLine("Stopping. Nothing has been changed.");
        return 1;
    }

    // Same algorithm and work factor as BCryptPasswordHasher in the API.
    var hash = BCrypt.Net.BCrypt.HashPassword(password, 12);

    Console.WriteLine();
    Console.WriteLine("Now put it in the database. On neon.tech open your project, then SQL Editor,");
    Console.WriteLine("paste this one line and press Run. It should report that 1 row was updated:");
    Console.WriteLine();
    Console.WriteLine($"UPDATE users SET \"PasswordHash\" = '{hash}' WHERE \"Email\" = '{account.Replace("'", "''")}';");
    Console.WriteLine();
    Console.WriteLine("If it says 0 rows, the email is not the one the account was registered with.");
    Console.WriteLine("The line above holds a hash, not your password, but still don't share it.");
    return 0;
}

var email = args.FirstOrDefault(a => !a.StartsWith("--"))?.Trim();
if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
{
    Console.WriteLine("Usage: dotnet run --project tools/Articles.AdminSetup -- you@example.com");
    Console.WriteLine("Use the email address of your Articles account.");
    return 2;
}

var secretBytes = KeyGeneration.GenerateRandomKey(20);
var secret = Base32Encoding.ToString(secretBytes);

var uri = $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(email)}" +
          $"?secret={secret}&issuer={Uri.EscapeDataString(Issuer)}&algorithm=SHA1&digits=6&period=30";

var qrPath = Path.Combine(Path.GetTempPath(), $"articles-admin-setup-{Guid.NewGuid():N}.png");

try
{
    using (var generator = new QRCodeGenerator())
    using (var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q))
    {
        File.WriteAllBytes(qrPath, new PngByteQRCode(data).GetGraphic(12));
    }

    Console.WriteLine();
    Console.WriteLine("Step 1. Scan the QR code with your authenticator app.");
    Console.WriteLine($"        It is saved at: {qrPath}");
    Console.WriteLine("        (It should open by itself. This tool deletes the file when it finishes.)");
    try { Process.Start(new ProcessStartInfo(qrPath) { UseShellExecute = true }); } catch { /* the path above is enough */ }

    Console.WriteLine();
    Console.WriteLine("        Can't scan? Add it by typing this key into the app instead (time-based, 6 digits):");
    Console.WriteLine($"        {string.Join(' ', Enumerable.Range(0, (secret.Length + 3) / 4).Select(i => secret.Substring(i * 4, Math.Min(4, secret.Length - i * 4))))}");

    var totp = new Totp(secretBytes);
    var confirmed = false;

    Console.WriteLine();
    Console.WriteLine("Step 2. Prove it works: type the 6-digit code your app shows now.");

    for (var attempt = 1; attempt <= 5 && !confirmed; attempt++)
    {
        Console.Write("        Code: ");
        var code = Console.ReadLine()?.Trim().Replace(" ", "");
        if (code is null) { Console.WriteLine("No input, stopping. Nothing has been set up."); return 1; }

        confirmed = totp.VerifyTotp(code, out _, VerificationWindow.RfcSpecifiedNetworkDelay);
        Console.WriteLine(confirmed ? "        That matches." : "        That code is not right. Wait for the next one and try again.");
    }

    if (!confirmed)
    {
        Console.WriteLine("Too many wrong codes. Start again; nothing has been set up.");
        return 1;
    }

    Console.WriteLine();
    Console.WriteLine("Step 3. Give the secret to the server. On Render, open your service, then Environment,");
    Console.WriteLine("        and add these two variables (the second line is YOUR Articles login email):");
    Console.WriteLine();
    Console.WriteLine($"          Admin__TotpSecret = {secret}");
    Console.WriteLine($"          Admin__Emails__0  = {email}");
    Console.WriteLine();
    Console.WriteLine("Keep the secret private: anyone holding it can generate your codes. Don't paste it into");
    Console.WriteLine("chat, email or a file in the repository. If you lose your phone, run this tool again and");
    Console.WriteLine("replace the value on Render; the old secret stops working at once.");
    return 0;
}
finally
{
    try { File.Delete(qrPath); } catch { /* temp file; harmless if it lingers */ }
}

static string ReadHidden(string prompt)
{
    Console.Write(prompt);
    if (Console.IsInputRedirected) return Console.ReadLine() ?? "";

    var typed = new System.Text.StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace) { if (typed.Length > 0) typed.Length--; }
        else if (!char.IsControl(key.KeyChar)) typed.Append(key.KeyChar);
    }
    Console.WriteLine();
    return typed.ToString();
}
