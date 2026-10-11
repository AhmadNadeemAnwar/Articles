# Articles

The blog part of Ahmad Nadeem's website (his portfolio is at https://ahmadnadeem.dev). One owner writes; anyone
can read, search, react and subscribe. There are no reader accounts.

Live at https://articles.ahmadnadeem.dev, with a private admin portal at https://admin.ahmadnadeem.dev.

| Part | What it is | Where it runs |
|---|---|---|
| `src/` | ASP.NET Core 10 Web API (layered: Domain, Application, Infrastructure, Api) | Render |
| `web/` | The public site: React 19 + Vite, plus a small Cloudflare Worker (`web/worker/`) | Cloudflare |
| `admin/` | The owner's portal: React 19 + Vite, with a TipTap editor | Cloudflare |
| `tools/Articles.AdminSetup` | One-off helper to create the authenticator secret | your machine |

Everything runs on free plans. How to deploy is in [DEPLOY.md](DEPLOY.md); how to use and set up the
admin portal is in [ADMIN.md](ADMIN.md).

## Running it locally

Three processes.

**API**, at http://localhost:5231 (Swagger at `/swagger`):

```bash
dotnet run --project src/Articles.Api
```

**Public site**, at http://localhost:5173:

```bash
npm install --prefix web
```

```bash
npm run dev --prefix web
```

**Admin portal**, at http://localhost:5174:

```bash
npm install --prefix admin
```

```bash
npm run dev --prefix admin
```

Both sites proxy `/api` to the local API, so the browser stays on one origin in development.

On first run the API applies its migrations and seeds three sample writers and a few posts into
`src/Articles.Api/articles.db`. To sign in to the local admin portal, use `maya@example.com` and a code
from an authenticator app loaded with the development secret in
`src/Articles.Api/appsettings.Development.json` (`Admin:TotpSecret`). That secret and the seeded
accounts exist only in development and are never used by the deployed site.

The Worker is not part of `npm run dev`. To try it, run `npx wrangler dev` in `web/`; it serves the
built site from `web/dist` and talks to whatever `API_BASE` in `web/wrangler.jsonc` points at.

## Tests

```bash
dotnet test tests/Articles.Tests
```

```bash
npm test --prefix web
```

```bash
npm test --prefix admin
```

The API tests use a real in-memory SQLite database, run the full HTTP pipeline in Production
configuration, and check that every query also translates for Postgres. The front-end tests cover
the logic that has rules worth pinning down: how a post body is stored and rendered, link and
picture address checks, the Worker's preview, sitemap and feed output, and form validation.

## How it works

**Readers have no accounts.** Each browser makes up a random id for itself. The API stores only a
hash of it, and uses it for two things: so a reader's Clap or Insightful is a toggle rather than a
counter, and so a view is counted once per reader per post per day. Crawlers and link previews are
not counted, because a view is reported by the page's script, not by fetching the post.

**The owner signs in with an authenticator code, and nothing else.** Email-and-password sign-in is
switched off in production (`Accounts:AllowPasswordSignIn`). An admin session lasts two hours, lives
in the browser tab, and is re-checked against the admin list on every request.

**A post body is structured data, not HTML.** It is a list of sections (rich text, an uploaded
picture, a list of sources) stored as JSON. The public site walks that JSON and renders React
elements for a fixed set of node types; there is no `dangerouslySetInnerHTML` anywhere, so a crafted
body cannot inject markup. The API checks the shape again before saving, and derives the searchable
plain text itself rather than trusting the browser for it.

**A post is in one of three states:** Draft, Published, or Inactive (shown as "Not active"). Its
address is fixed the first time it is published (`/read/its-title-k3x9p`) and never changes, so
retitling or republishing does not break a link.

**Pictures live in the database.** Uploads are shrunk in the browser, type-checked by their signature
bytes on the server, and capped at 300 MB in total to stay inside the free database allowance. The
public site fetches them from `/media/<id>`, which the Worker serves from Cloudflare's cache.

**The Worker fills the gaps a single-page app leaves.** It runs only for a few addresses: it adds
each post's title, description and picture to its page so shared links show a proper card; answers
"not found" for a post that does not exist; redirects old `/p/` links; serves pictures from cache;
and builds `/sitemap.xml` and `/rss.xml`.

**Email is opt-in twice.** A reader is not subscribed until they click the link in a confirmation
email, and nothing is sent to subscribers until the owner presses *Notify subscribers* on a post.
Until a mail service key is configured the feature is off and the subscribe form is hidden.

## Layout

```
src/
  Articles.Domain              Entities, invariants, repository interfaces. No framework dependencies.
  Articles.Application         Services, DTOs, validators, post-body checks. Depends on Domain.
  Articles.Infrastructure      EF Core, repositories, SQLite migrations, tokens, mail. Depends on Domain + Application.
  Articles.Migrations.Postgres The same migrations generated for Postgres, which production uses.
  Articles.Api                 Controllers, middleware, rate limiting, DI.
tests/Articles.Tests           xUnit + FluentAssertions.
web/                          Public site, and web/worker/ for the Cloudflare Worker.
admin/                        Admin portal.
offline/                      A "temporarily offline" page for pausing a site.
```

Dependencies point inward: `Api -> Application / Infrastructure -> Domain`.

Local development uses SQLite; production uses Postgres. Timestamps are stored as UTC ticks on
SQLite, which has no native type for them and could not otherwise sort by date.

## Configuration

Nothing secret is in the repository. On the host, set:

| Setting | What it is |
|---|---|
| `Jwt__Key` | Signing key, at least 32 characters. The API refuses to start without one. |
| `ConnectionStrings__Default`, `Database__Provider=postgres` | The database. |
| `Admin__Emails__0`, `Admin__TotpSecret` | Who may use the admin portal, and their authenticator secret. |
| `Email__ApiKey`, `Email__FromAddress` | Optional. Switches on email subscriptions (Brevo). |
| `Portfolio__Repo`, `Portfolio__Token` | Optional. Switches on portfolio editing. |

## Limits worth knowing

- The API sleeps when idle on the free plan, so the first request after a quiet spell is slow.
  Pictures and link previews are cached at Cloudflare to soften this; post text is not.
- Reader counts are honest but not tamper-proof: clearing a browser's site data makes a "new" reader.
- A picture removed from a post stays in the database; there is no screen yet to delete unused ones.
- Search is a case-insensitive substring match, which is fine at this size. It lives behind one
  repository method, so moving to Postgres full-text search later changes only that method.
- Some account-era features remain in the API but are unreachable now that nobody can sign in with a
  password: comments, bookmarks, follows and the personal feed.
