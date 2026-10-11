namespace Articles.Application.Auth;

public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    /// <summary>
    /// The passwords attackers try first. This is a cheap local screen; the breach-corpus lookup
    /// in <c>IPwnedPasswordChecker</c> covers the long tail.
    /// </summary>
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password12", "password123", "password1234", "passw0rd", "p@ssw0rd",
        "123456", "1234567", "12345678", "123456789", "1234567890", "12345678910", "0123456789",
        "qwerty", "qwerty123", "qwertyuiop", "qwerty1234", "1q2w3e4r", "1q2w3e4r5t", "zxcvbnm", "asdfghjkl",
        "iloveyou", "letmein", "welcome", "welcome1", "welcome123", "admin123", "administrator",
        "monkey", "dragon", "football", "baseball", "sunshine", "princess", "superman", "trustno1",
        "abc123", "abc12345", "abcd1234", "changeme", "internet", "whatever", "articles", "articles123"
    };

    /// <returns>A message describing the problem, or null if the password is acceptable.</returns>
    public static string? Check(string password, string email, string handle, string displayName)
    {
        if (password.Length < MinLength) return $"Password must be at least {MinLength} characters.";
        if (password.Length > MaxLength) return $"Password cannot exceed {MaxLength} characters.";
        if (Common.Contains(password)) return "That password is too common. Choose something harder to guess.";

        // A password built from the person's own identifiers is the first thing a targeted attack tries.
        var local = email.Split('@')[0];
        foreach (var personal in new[] { email, local, handle, displayName })
        {
            if (personal.Length >= 4 && password.Contains(personal, StringComparison.OrdinalIgnoreCase))
                return "Password should not contain your name, handle or email.";
        }

        if (password.Distinct().Count() < 4) return "Password needs more variety than that.";

        return null;
    }
}
