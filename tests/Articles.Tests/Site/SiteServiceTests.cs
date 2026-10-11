using FluentAssertions;
using Articles.Application.Admin;
using Articles.Application.Posts;
using Articles.Application.Posts.Dtos;
using Articles.Application.Site;
using Articles.Domain.Common;
using Articles.Domain.Entities;
using Articles.Domain.Exceptions;
using Articles.Infrastructure.Persistence;
using Articles.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Articles.Tests.Site;

public class ThemesTests
{
    [Theory]
    [InlineData("blue", "blue")]
    [InlineData("SeaGreen", "seagreen")]
    [InlineData("  seagreen ", "seagreen")]
    public void A_known_theme_is_accepted_however_it_is_typed(string input, string expected) =>
        Themes.Normalise(input).Should().Be(expected);

    [Theory]
    [InlineData("purple")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("blue; background: url(https://evil.example)")]
    public void Anything_else_is_refused(string? input)
    {
        var act = () => Themes.Normalise(input);

        act.Should().Throw<DomainException>().WithMessage("*blue, seagreen*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a-theme-from-a-newer-version")]
    public void A_stored_value_this_version_does_not_know_falls_back_to_the_default(string? stored) =>
        Themes.OrDefault(stored).Should().Be(Themes.Blue);
}

public class SiteServiceTests : IDisposable
{
    private const string Owner = "owner@example.com";

    private readonly TestDatabase _fixture = new();
    private readonly SiteRepository _repository;
    private readonly ActivityLog _log;
    private readonly SiteService _site;
    private readonly PostService _posts;

    public SiteServiceTests()
    {
        _repository = new SiteRepository(_fixture.Db);
        _log = new ActivityLog(_fixture.Db, NullLogger<ActivityLog>.Instance);
        _site = new SiteService(_repository, _fixture.Db, _log);
        _posts = new PostService(_fixture.Posts, _fixture.Tags, _fixture.Engagement, _fixture.Db, null, _repository);
    }

    private Task<CategoryWithCountDto> AddAsync(string name) => _site.CreateCategoryAsync(new SaveCategoryRequest(name), Owner);

    private async Task<PostDetailDto> PostAsync(Guid authorId, string title, Guid? categoryId, bool publish = true)
    {
        var draft = await _posts.CreateDraftAsync(new CreatePostRequest(title, null, TestDatabase.Document($"Body of {title}."), null, [], categoryId), authorId);
        return publish ? await _posts.PublishAsync(draft.Id, authorId) : draft;
    }

    private async Task<IReadOnlyList<string>> HistoryAsync() => (await _log.GetPageAsync(1, 100)).Items.Select(i => $"{i.Action}: {i.Subject}").ToList();

    // ---- Theme --------------------------------------------------------------------------------

    [Fact]
    public async Task A_site_that_has_never_chosen_wears_the_default_theme()
    {
        (await _site.GetPublicAsync()).Theme.Should().Be(Themes.Blue);
        (await _site.GetSettingsAsync()).AvailableThemes.Should().Equal(Themes.Blue, Themes.SeaGreen, Themes.Custom);
    }

    // ---- Custom colours -----------------------------------------------------------------------

    [Fact]
    public async Task The_owners_own_colours_reach_every_reader_and_are_recorded()
    {
        var saved = await _site.UpdateSettingsAsync(new UpdateSiteSettingsRequest("custom", " #7A1F5C ", "#FFF8F0"), Owner);

        saved.Theme.Should().Be("custom");
        saved.Colors.Should().Be(new ThemeColorsDto("#7a1f5c", "#fff8f0"));
        var site = await _site.GetPublicAsync();
        site.Theme.Should().Be("custom");
        site.Colors.Should().Be(new ThemeColorsDto("#7a1f5c", "#fff8f0"));
        (await HistoryAsync()).Should().ContainSingle().Which.Should().Be($"{Activity.ChangedTheme}: blue to custom (#7a1f5c on #fff8f0)");
    }

    [Fact]
    public async Task A_ready_made_theme_sends_no_colours_to_readers()
    {
        (await _site.GetPublicAsync()).Colors.Should().BeNull();

        await _site.UpdateSettingsAsync(new UpdateSiteSettingsRequest("custom", "#7a1f5c", "#fff8f0"), Owner);
        await _site.UpdateSettingsAsync(new UpdateSiteSettingsRequest("seagreen"), Owner);

        var site = await _site.GetPublicAsync();
        site.Theme.Should().Be("seagreen");
        site.Colors.Should().BeNull();
    }

    [Fact]
    public async Task The_pickers_start_from_a_suggestion_and_then_remember_the_last_colours_saved()
    {
        (await _site.GetSettingsAsync()).Colors.Should().Be(ThemeColors.Suggested);

        await _site.UpdateSettingsAsync(new UpdateSiteSettingsRequest("custom", "#7a1f5c", "#fff8f0"), Owner);
        await _site.UpdateSettingsAsync(new UpdateSiteSettingsRequest("blue"), Owner);

        (await _site.GetSettingsAsync()).Colors.Should().Be(new ThemeColorsDto("#7a1f5c", "#fff8f0"));
    }

    [Fact]
    public async Task Changing_only_a_colour_is_a_change_and_saving_the_same_colours_is_not()
    {
        await _site.UpdateSettingsAsync(new UpdateSiteSettingsRequest("custom", "#7a1f5c", "#fff8f0"), Owner);

        await _site.UpdateSettingsAsync(new UpdateSiteSettingsRequest("custom", "#7A1F5C", "#fff8f0"), Owner);
        (await HistoryAsync()).Should().HaveCount(1);

        await _site.UpdateSettingsAsync(new UpdateSiteSettingsRequest("custom", "#224488", "#fff8f0"), Owner);

        (await _site.GetPublicAsync()).Colors!.Main.Should().Be("#224488");
        (await HistoryAsync()).Should().Contain($"{Activity.ChangedTheme}: custom (#7a1f5c on #fff8f0) to custom (#224488 on #fff8f0)");
        _fixture.Db.SiteSettings.Should().HaveCount(2, "one setting for the theme and one for its colours, replaced each time");
    }

    [Theory]
    [InlineData(null, "#ffffff")]
    [InlineData("#17694a", null)]
    [InlineData("green", "#ffffff")]
    [InlineData("#17694", "#ffffff")]
    [InlineData("#17694a", "#fff")]
    [InlineData("#17694a", "#ffffff; background: url(https://evil.example)")]
    [InlineData("#17694a", "#gggggg")]
    public async Task A_custom_theme_needs_two_plain_colours(string? main, string? background)
    {
        await _site.UpdateSettingsAsync(new UpdateSiteSettingsRequest("seagreen"), Owner);

        var failure = await Record.ExceptionAsync(() => _site.UpdateSettingsAsync(new UpdateSiteSettingsRequest("custom", main, background), Owner));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("colour like #17694a");
        (await _site.GetPublicAsync()).Theme.Should().Be("seagreen");
    }

    [Theory]
    [InlineData("#808080")]
    [InlineData("#7a7a7a")]
    [InlineData("#c0392b")]
    public async Task A_background_no_text_could_be_read_on_is_refused(string background)
    {
        var failure = await Record.ExceptionAsync(() => _site.UpdateSettingsAsync(new UpdateSiteSettingsRequest("custom", "#17694a", background), Owner));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("hard to read");
        (await _site.GetPublicAsync()).Theme.Should().Be(Themes.Blue);
    }

    [Theory]
    [InlineData("#ffffff")]
    [InlineData("#fff8f0")]
    [InlineData("#101418")]
    [InlineData("#000000")]
    public async Task Light_and_dark_backgrounds_are_both_allowed(string background)
    {
        var saved = await _site.UpdateSettingsAsync(new UpdateSiteSettingsRequest("custom", "#17694a", background), Owner);

        saved.Colors.Background.Should().Be(background);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#7a1f5c")]
    [InlineData("#7a1f5c,#808080")]
    [InlineData("#7a1f5c,red")]
    [InlineData("#7a1f5c,#fff8f0,#000000")]
    public async Task A_custom_theme_whose_stored_colours_cannot_be_used_falls_back_to_the_default(string stored)
    {
        await _repository.SetSettingAsync(Themes.SettingKey, Themes.Custom);
        await _repository.SetSettingAsync(ThemeColors.SettingKey, stored);
        await _fixture.Db.SaveChangesAsync();

        var site = await _site.GetPublicAsync();

        site.Theme.Should().Be(Themes.Blue);
        site.Colors.Should().BeNull();
    }

    [Fact]
    public void Contrast_is_measured_the_standard_way()
    {
        ThemeColors.Contrast("#000000", "#ffffff").Should().BeApproximately(21, 0.01);
        ThemeColors.Contrast("#ffffff", "#ffffff").Should().BeApproximately(1, 0.001);
        ThemeColors.Contrast("#777777", "#ffffff").Should().BeApproximately(4.48, 0.01);
        ThemeColors.Contrast("#ffffff", "#777777").Should().BeApproximately(4.48, 0.01);
    }

    [Fact]
    public async Task Choosing_a_theme_changes_what_every_reader_gets_and_is_recorded()
    {
        var saved = await _site.UpdateSettingsAsync(new UpdateSiteSettingsRequest("seagreen"), Owner);

        saved.Theme.Should().Be("seagreen");
        (await _site.GetPublicAsync()).Theme.Should().Be("seagreen");
        (await HistoryAsync()).Should().ContainSingle().Which.Should().Be($"{Activity.ChangedTheme}: blue to seagreen");
    }

    [Fact]
    public async Task Choosing_the_theme_already_in_use_changes_nothing_and_records_nothing()
    {
        await _site.UpdateSettingsAsync(new UpdateSiteSettingsRequest("seagreen"), Owner);

        await _site.UpdateSettingsAsync(new UpdateSiteSettingsRequest("SeaGreen"), Owner);

        (await HistoryAsync()).Should().HaveCount(1);
        _fixture.Db.SiteSettings.Should().ContainSingle();
    }

    [Fact]
    public async Task The_theme_can_be_changed_back()
    {
        await _site.UpdateSettingsAsync(new UpdateSiteSettingsRequest("seagreen"), Owner);
        await _site.UpdateSettingsAsync(new UpdateSiteSettingsRequest("blue"), Owner);

        (await _site.GetPublicAsync()).Theme.Should().Be("blue");
        _fixture.Db.SiteSettings.Should().ContainSingle("the setting is replaced, not added to");
    }

    [Fact]
    public async Task An_unknown_theme_is_refused_and_the_current_one_stays()
    {
        await _site.UpdateSettingsAsync(new UpdateSiteSettingsRequest("seagreen"), Owner);

        var failure = await Record.ExceptionAsync(() => _site.UpdateSettingsAsync(new UpdateSiteSettingsRequest("neon"), Owner));

        failure.Should().BeOfType<DomainException>();
        (await _site.GetPublicAsync()).Theme.Should().Be("seagreen");
    }

    // ---- Categories ---------------------------------------------------------------------------

    [Fact]
    public async Task Categories_are_listed_in_the_order_they_were_added()
    {
        await AddAsync("Life and lessons");
        await AddAsync("Technology and AI");
        await AddAsync("Books");

        (await _site.GetCategoriesAsync()).Select(c => (c.Name, c.Slug)).Should().Equal(
            ("Life and lessons", "life-and-lessons"), ("Technology and AI", "technology-and-ai"), ("Books", "books"));
    }

    [Fact]
    public async Task A_category_counts_only_its_published_posts()
    {
        var author = await _fixture.AddUserAsync();
        var life = await AddAsync("Life and lessons");
        var tech = await AddAsync("Technology and AI");
        await PostAsync(author.Id, "One", life.Id);
        await PostAsync(author.Id, "Two", life.Id);
        await PostAsync(author.Id, "A draft", life.Id, publish: false);
        var hidden = await PostAsync(author.Id, "Taken down", life.Id);
        await _posts.SetStatusAsync(hidden.Id, "Inactive", author.Id);
        await PostAsync(author.Id, "No category", null);

        var counts = (await _site.GetPublicAsync()).Categories.ToDictionary(c => c.Slug, c => c.PostCount);

        counts.Should().BeEquivalentTo(new Dictionary<string, int> { ["life-and-lessons"] = 2, [tech.Slug] = 0 });
    }

    [Theory]
    [InlineData("life and lessons")]
    [InlineData("  LIFE AND LESSONS ")]
    public async Task Two_categories_cannot_share_a_name(string duplicate)
    {
        await AddAsync("Life and lessons");

        var failure = await Record.ExceptionAsync(() => AddAsync(duplicate));

        failure.Should().BeOfType<ConflictException>();
        (await _site.GetCategoriesAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Names_that_reduce_to_the_same_address_both_work()
    {
        var first = await AddAsync("AI & ML");
        var second = await AddAsync("AI ML");

        first.Slug.Should().Be("ai-ml");
        second.Slug.Should().StartWith("ai-ml-").And.NotBe(first.Slug);
    }

    [Theory]
    [InlineData("!!!")]
    [InlineData("   ")]
    public async Task A_name_with_nothing_to_make_an_address_from_is_refused(string name)
    {
        var failure = await Record.ExceptionAsync(() => AddAsync(name));

        failure.Should().BeOfType<DomainException>();
    }

    [Fact]
    public async Task There_is_a_ceiling_on_the_number_of_categories()
    {
        for (var i = 0; i < SiteService.MaxCategories; i++) await AddAsync($"Category {i}");

        var failure = await Record.ExceptionAsync(() => AddAsync("One too many"));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("at most");
    }

    [Fact]
    public async Task Renaming_changes_the_name_but_never_the_address()
    {
        var category = await AddAsync("Tech");

        var renamed = await _site.RenameCategoryAsync(category.Id, new SaveCategoryRequest("Technology and AI"), Owner);

        renamed.Name.Should().Be("Technology and AI");
        renamed.Slug.Should().Be("tech", "links to the category must keep working");
    }

    [Fact]
    public async Task A_category_can_be_renamed_to_its_own_name_with_different_capitals_but_not_to_another_categorys()
    {
        var life = await AddAsync("life and lessons");
        await AddAsync("Books");

        (await _site.RenameCategoryAsync(life.Id, new SaveCategoryRequest("Life and Lessons"), Owner)).Name.Should().Be("Life and Lessons");
        (await Record.ExceptionAsync(() => _site.RenameCategoryAsync(life.Id, new SaveCategoryRequest("books"), Owner))).Should().BeOfType<ConflictException>();
    }

    [Fact]
    public async Task Deleting_a_category_keeps_its_posts_and_leaves_them_without_one()
    {
        var author = await _fixture.AddUserAsync();
        var category = await AddAsync("Short lived");
        var post = await PostAsync(author.Id, "Survivor", category.Id);

        await _site.DeleteCategoryAsync(category.Id, Owner);

        _fixture.Db.ChangeTracker.Clear();
        var stored = await _fixture.Posts.GetByIdAsync(post.Id);
        stored.Should().NotBeNull();
        stored!.CategoryId.Should().BeNull();
        stored.Status.ToString().Should().Be("Published");
        (await _site.GetCategoriesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Renaming_or_deleting_a_category_that_is_gone_is_not_found()
    {
        (await Record.ExceptionAsync(() => _site.RenameCategoryAsync(Guid.NewGuid(), new SaveCategoryRequest("X"), Owner))).Should().BeOfType<NotFoundException>();
        (await Record.ExceptionAsync(() => _site.DeleteCategoryAsync(Guid.NewGuid(), Owner))).Should().BeOfType<NotFoundException>();
    }

    [Fact]
    public async Task Adding_renaming_and_deleting_are_recorded()
    {
        var category = await AddAsync("Tech");
        await _site.RenameCategoryAsync(category.Id, new SaveCategoryRequest("Technology"), Owner);
        await _site.DeleteCategoryAsync(category.Id, Owner);

        (await HistoryAsync()).Should().BeEquivalentTo(
            [$"{Activity.AddedCategory}: Tech", $"{Activity.RenamedCategory}: Tech to Technology", $"{Activity.DeletedCategory}: Technology"]);
    }

    // ---- A post's category --------------------------------------------------------------------

    [Fact]
    public async Task A_post_carries_its_category_and_can_be_found_by_it()
    {
        var author = await _fixture.AddUserAsync();
        var life = await AddAsync("Life and lessons");
        var tech = await AddAsync("Technology and AI");
        var post = await PostAsync(author.Id, "On patience", life.Id);
        await PostAsync(author.Id, "On compilers", tech.Id);
        await PostAsync(author.Id, "Unshelved", null);

        var found = await _posts.SearchAsync(new PostQueryParameters { Category = "Life-And-Lessons" });

        post.Category.Should().BeEquivalentTo(new CategoryDto(life.Id, "Life and lessons", "life-and-lessons"));
        found.Items.Should().ContainSingle().Which.Title.Should().Be("On patience");
        found.Items[0].Category!.Slug.Should().Be("life-and-lessons");
    }

    [Fact]
    public async Task With_no_category_asked_for_every_post_is_listed()
    {
        var author = await _fixture.AddUserAsync();
        var life = await AddAsync("Life and lessons");
        await PostAsync(author.Id, "Shelved", life.Id);
        await PostAsync(author.Id, "Unshelved", null);

        (await _posts.SearchAsync(new PostQueryParameters())).TotalCount.Should().Be(2);
        (await _posts.SearchAsync(new PostQueryParameters { Category = "no-such-category" })).TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task A_posts_category_can_be_changed_and_removed()
    {
        var author = await _fixture.AddUserAsync();
        var life = await AddAsync("Life and lessons");
        var tech = await AddAsync("Technology and AI");
        var post = await PostAsync(author.Id, "Movable", life.Id);

        UpdatePostRequest With(Guid? categoryId) => new("Movable", null, TestDatabase.Document("Body of Movable."), null, [], categoryId);

        (await _posts.UpdateAsync(post.Id, With(tech.Id), author.Id)).Category!.Name.Should().Be("Technology and AI");
        (await _posts.UpdateAsync(post.Id, With(null), author.Id)).Category.Should().BeNull();
    }

    [Fact]
    public async Task A_category_that_does_not_exist_is_refused_rather_than_silently_dropped()
    {
        var author = await _fixture.AddUserAsync();
        var life = await AddAsync("Life and lessons");
        var post = await PostAsync(author.Id, "Stays put", life.Id);

        var create = () => _posts.CreateDraftAsync(new CreatePostRequest("New", null, TestDatabase.Document("Body."), null, [], Guid.NewGuid()), author.Id);
        var update = () => _posts.UpdateAsync(post.Id, new UpdatePostRequest("Stays put", null, TestDatabase.Document("Body of Stays put."), null, [], Guid.NewGuid()), author.Id);

        await create.Should().ThrowAsync<DomainException>().WithMessage("*no longer exists*");
        await update.Should().ThrowAsync<DomainException>();
        _fixture.Db.ChangeTracker.Clear();
        (await _fixture.Posts.GetByIdAsync(post.Id))!.CategoryId.Should().Be(life.Id);
    }

    public void Dispose() => _fixture.Dispose();
}
