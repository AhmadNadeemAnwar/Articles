namespace Articles.Api.Common;

public sealed class AccountOptions
{
    public const string SectionName = "Accounts";

    /// <summary>
    /// When false, nobody can create an account through the API, and the web app hides every
    /// sign-in and sign-up prompt from visitors. Existing accounts can still sign in. This is the
    /// single switch both the API and the client follow, so the two cannot disagree.
    /// </summary>
    public bool AllowPublicSignUp { get; set; } = true;

    /// <summary>
    /// When false, the email-and-password sign-in is switched off altogether, and with it account
    /// creation. The admin portal is then the only way in, and it never uses a password. This is how
    /// the deployed site runs: nobody but its owner writes there.
    /// </summary>
    public bool AllowPasswordSignIn { get; set; } = true;
}
