using FluentAssertions;
using Articles.Application.Images;
using Articles.Domain.Common;
using Articles.Domain.Entities;
using Articles.Domain.Exceptions;
using Articles.Domain.Interfaces;
using Articles.Infrastructure.Persistence.Repositories;
using Moq;
using Xunit;

namespace Articles.Tests.Images;

public static class SampleImages
{
    /// <summary>Starts like a real file of each type; the service only inspects the signature.</summary>
    public static byte[] Png(int size = 64) => WithHeader([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], size);
    public static byte[] Jpeg(int size = 64) => WithHeader([0xFF, 0xD8, 0xFF, 0xE0], size);
    public static byte[] WebP(int size = 64) => WithHeader([.. "RIFF"u8, 0x24, 0, 0, 0, .. "WEBP"u8], size);

    private static byte[] WithHeader(byte[] header, int size)
    {
        var data = new byte[Math.Max(size, header.Length)];
        header.CopyTo(data, 0);
        return data;
    }
}

public class ImageFormatTests
{
    [Fact]
    public void Jpeg_png_and_webp_are_recognised_by_their_first_bytes()
    {
        ImageFormat.Detect(SampleImages.Jpeg()).Should().Be("image/jpeg");
        ImageFormat.Detect(SampleImages.Png()).Should().Be("image/png");
        ImageFormat.Detect(SampleImages.WebP()).Should().Be("image/webp");
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>")]
    [InlineData("<html><body>hello</body></html>")]
    [InlineData("GIF89a-an-animation")]
    [InlineData("RIFF....WAVEfmt ")]
    [InlineData("MZ-an-executable")]
    [InlineData("")]
    public void Anything_else_is_not_an_image_whatever_it_is_called(string content)
    {
        ImageFormat.Detect(System.Text.Encoding.ASCII.GetBytes(content)).Should().BeNull();
    }
}

public class ImageServiceTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly ImageService _service;

    public ImageServiceTests() => _service = new ImageService(new ImageRepository(_fixture.Db), _fixture.Db);

    [Fact]
    public async Task An_uploaded_picture_can_be_fetched_back_unchanged()
    {
        var owner = await _fixture.AddUserAsync();
        var data = SampleImages.Png(500);

        var stored = await _service.UploadAsync(owner.Id, data);
        var fetched = await _service.GetAsync(stored.Id);

        stored.ContentType.Should().Be("image/png");
        stored.Size.Should().Be(500);
        fetched.ContentType.Should().Be("image/png");
        fetched.Data.Should().Equal(data);
    }

    [Fact]
    public async Task The_address_handed_back_is_one_a_post_is_allowed_to_use()
    {
        var owner = await _fixture.AddUserAsync();

        var stored = await _service.UploadAsync(owner.Id, SampleImages.Jpeg());

        stored.Path.Should().Be($"/api/v1/images/{stored.Id}");
        UrlRules.IsStoredImagePath(stored.Path).Should().BeTrue();
        UrlRules.IsImageReference(stored.Path).Should().BeTrue();
    }

    [Fact]
    public async Task A_file_that_is_not_a_picture_is_refused_and_nothing_is_stored()
    {
        var owner = await _fixture.AddUserAsync();
        var svg = System.Text.Encoding.ASCII.GetBytes("<svg><script>alert(1)</script></svg>");

        var failure = await Record.ExceptionAsync(() => _service.UploadAsync(owner.Id, svg));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("JPEG, PNG and WebP");
        _fixture.Db.Images.Should().BeEmpty();
    }

    [Fact]
    public async Task An_empty_upload_is_refused()
    {
        var failure = await Record.ExceptionAsync(() => _service.UploadAsync(Guid.NewGuid(), []));

        failure.Should().BeOfType<DomainException>();
    }

    [Fact]
    public async Task A_picture_over_the_size_limit_is_refused()
    {
        var failure = await Record.ExceptionAsync(() => _service.UploadAsync(Guid.NewGuid(), SampleImages.Png(ImageService.MaxBytes + 1)));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("too large");
    }

    [Fact]
    public async Task A_picture_exactly_at_the_size_limit_is_accepted()
    {
        var owner = await _fixture.AddUserAsync();

        var stored = await _service.UploadAsync(owner.Id, SampleImages.Png(ImageService.MaxBytes));

        stored.Size.Should().Be(ImageService.MaxBytes);
    }

    [Fact]
    public async Task Uploads_stop_once_the_storage_allowance_would_be_exceeded()
    {
        var images = new Mock<IImageRepository>();
        images.Setup(r => r.GetTotalBytesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ImageService.MaxTotalBytes - 10);
        var service = new ImageService(images.Object, Mock.Of<IUnitOfWork>());

        var failure = await Record.ExceptionAsync(() => service.UploadAsync(Guid.NewGuid(), SampleImages.Png(64)));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("storage is full");
        images.Verify(r => r.AddAsync(It.IsAny<StoredImage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_storage_total_counts_every_picture()
    {
        var owner = await _fixture.AddUserAsync();
        await _service.UploadAsync(owner.Id, SampleImages.Png(300));
        await _service.UploadAsync(owner.Id, SampleImages.Jpeg(200));

        (await new ImageRepository(_fixture.Db).GetTotalBytesAsync()).Should().Be(500);
    }

    [Fact]
    public async Task An_unknown_picture_is_not_found()
    {
        var failure = await Record.ExceptionAsync(() => _service.GetAsync(Guid.NewGuid()));

        failure.Should().BeOfType<NotFoundException>();
    }

    public void Dispose() => _fixture.Dispose();
}
