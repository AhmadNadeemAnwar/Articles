using System.Text.Json;
using Articles.Application.Common;
using Articles.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Articles.Infrastructure.Persistence;

/// <summary>
/// Applies migrations and seeds a small, realistic corpus so the feed, tag pages and
/// search return something meaningful on a fresh clone.
/// </summary>
public static class DbInitializer
{
    private const string SeedPassword = "Password123!";

    /// <param name="seedSampleData">
    /// Demo accounts share a published password, so this must stay off for any public deployment.
    /// </param>
    public static async Task InitialiseAsync(AppDbContext db, IPasswordHasher passwordHasher, bool seedSampleData, CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        if (!seedSampleData) return;
        if (await db.Users.AnyAsync(ct)) return;

        var hash = passwordHasher.Hash(SeedPassword);

        var maya = new User("maya@example.com", "maya-okonkwo", "Maya Okonkwo", hash);
        maya.UpdateProfile("Maya Okonkwo", "Backend engineer. Writing about distributed systems and the humans who run them.", null, "https://example.com/maya");

        var devraj = new User("devraj@example.com", "devraj", "Devraj Patel", hash);
        devraj.UpdateProfile("Devraj Patel", "Long-distance runner, occasional essayist.", null, null);

        var lena = new User("lena@example.com", "lena-fischer", "Lena Fischer", hash);
        lena.UpdateProfile("Lena Fischer", "Cooking, travelling, and writing down what I learn.", null, null);

        db.Users.AddRange(maya, devraj, lena);

        var tags = new[]
        {
            new Tag("Engineering", "engineering"),
            new Tag("Distributed Systems", "distributed-systems"),
            new Tag("Productivity", "productivity"),
            new Tag("Running", "running"),
            new Tag("Food", "food"),
            new Tag("Travel", "travel"),
            new Tag("Career", "career")
        };
        db.Tags.AddRange(tags);

        var bySlug = tags.ToDictionary(t => t.Slug);

        var seeds = new (User Author, string Title, string Subtitle, string[] Paragraphs, string[] TagSlugs)[]
        {
            (maya,
                "What I learned running Postgres for three years",
                "Connection pools, vacuum, and the slow queries nobody notices until 3am.",
                [
                    "Every team I have joined has eventually hit the same wall with Postgres, and it is almost never the query planner's fault. It is the connection pool.",
                    "The default assumption is that more connections mean more throughput. In practice each connection costs memory and scheduling overhead, and past a few hundred you are paying for context switching rather than work.",
                    "The fix is unglamorous: put a pooler in front, cap the pool well below what feels comfortable, and measure. Our p99 dropped by half the week we cut the pool size by two thirds."
                ],
                ["engineering", "distributed-systems"]),

            (maya,
                "Idempotency is a product decision, not a technical one",
                "Retry semantics belong in the conversation long before they belong in the code.",
                [
                    "When a payment request times out, someone has to decide what the user sees. That decision is not the database's to make, and it is not the client's either.",
                    "Idempotency keys are the mechanism, but the interesting part is the policy: how long is a key valid, what counts as the same request, and what happens when the second attempt disagrees with the first.",
                    "Teams that treat this as plumbing end up encoding product decisions in a retry loop, where nobody can find them later."
                ],
                ["engineering", "distributed-systems"]),

            (devraj,
                "I ran every morning for a year. Here is what actually changed.",
                "Not the things I expected, and not in the order I expected them.",
                [
                    "The fitness came last. That was the surprise. For the first four months the only thing that improved was my willingness to start.",
                    "What changed first was sleep, then appetite, then mood, then, eventually and almost as an afterthought, pace.",
                    "If you are starting, measure consistency rather than distance. The distance follows on its own."
                ],
                ["running", "productivity"]),

            (devraj,
                "The case for boring career decisions",
                "Optimising for interesting has a cost that shows up years later.",
                [
                    "I have twice taken the more interesting job over the more sensible one, and both times the interesting part wore off in about seven months.",
                    "What lasted was whether I was working near people who were better than me at something I wanted to be good at.",
                    "That turns out to be a fairly boring criterion, and it has been much more predictive than any of the exciting ones."
                ],
                ["career", "productivity"]),

            (lena,
                "A week of cooking with only what was already in the cupboard",
                "Seven dinners, no shopping, and a much clearer sense of what I actually waste.",
                [
                    "The rule was simple: nothing new for seven days. Whatever was in the cupboard, the freezer, or the back of the fridge was the entire menu.",
                    "By Wednesday the easy options were gone and the cooking got genuinely more interesting. Constraint did more for my technique than any recipe had that year.",
                    "The uncomfortable part was the audit at the end. Three items had been in that cupboard long enough to predate the flat."
                ],
                ["food", "productivity"]),

            (lena,
                "Slow travel through northern Portugal",
                "Two weeks, four towns, and no itinerary past the first night.",
                [
                    "We booked the first night in Porto and nothing else. Every subsequent stop was decided the morning we left the previous one.",
                    "This is a terrible plan in August and an excellent one in October, when you can walk into almost anywhere and find a room.",
                    "The best meal of the trip came from a place with no sign, which we found because we had nowhere in particular to be."
                ],
                ["travel", "food"])
        };


        foreach (var (author, title, subtitle, paragraphs, tagSlugs) in seeds)
        {
            var contentJson = BuildDocument(paragraphs);
            var post = new Post(author.Id, title, subtitle, contentJson, ProseMirrorText.Extract(contentJson));

            post.Publish(SlugGenerator.Generate(title));

            foreach (var slug in tagSlugs)
            {
                var tag = bySlug[slug];
                post.PostTags.Add(new PostTag(post.Id, tag.Id));
                tag.IncrementPostCount();
            }

            db.Posts.Add(post);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Builds the minimal TipTap/ProseMirror document the editor expects.</summary>
    private static string BuildDocument(IEnumerable<string> paragraphs)
    {
        var content = paragraphs.Select(text => new
        {
            type = "paragraph",
            content = new[] { new { type = "text", text } }
        });

        return JsonSerializer.Serialize(new { type = "doc", content });
    }
}
