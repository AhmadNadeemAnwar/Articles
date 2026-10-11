namespace Articles.Application.Auth;

/// <summary>
/// Handles nobody may register: names that impersonate the site or staff, and words that are
/// (or may become) URL paths. Compared against the already-slugified handle.
/// </summary>
public static class ReservedHandles
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "administrator", "root", "superuser", "staff", "moderator", "mod", "support", "help",
        "security", "abuse", "official", "system", "articles", "team", "editor", "editors", "press",
        "api", "www", "mail", "email", "ftp", "app", "web", "cdn", "static", "assets", "status",
        "postmaster", "webmaster", "hostmaster", "noreply", "no-reply", "info", "contact", "billing",
        "me", "you", "anonymous", "everyone", "null", "undefined", "true", "false",
        "login", "logout", "signin", "signup", "register", "auth", "account", "settings", "profile",
        "search", "tag", "tags", "topic", "topics", "write", "new", "edit", "drafts", "bookmarks",
        "p", "post", "posts", "user", "users", "feed", "explore", "about", "terms", "privacy", "legal"
    };

    public static bool IsReserved(string handle) => Reserved.Contains(handle);
}
