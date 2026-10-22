using System.Text;
using NexusAgent.Api.Documents;
using Xunit;

namespace NexusAgent.UnitTests.Documents;

public sealed class DocumentContentValidatorTests
{
    [Theory]
    [InlineData("application/pdf")]
    [InlineData("APPLICATION/PDF")]
    public async Task ValidPdf_IsAccepted(string contentType)
    {
        using var stream = CreateStream("%PDF-1.7`nSample PDF");

        var result = await DocumentContentValidator.IsValidAsync(
            stream, contentType);

        Assert.True(result);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task ForgedPdf_IsRejected()
    {
        using var stream = CreateStream("Not a PDF");

        var result = await DocumentContentValidator.IsValidAsync(
            stream, "application/pdf");

        Assert.False(result);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/markdown")]
    public async Task ValidUtf8Text_IsAccepted(string contentType)
    {
        using var stream = CreateStream("Hello, world! äöü");

        var result = await DocumentContentValidator.IsValidAsync(
            stream, contentType);

        Assert.True(result);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task InvalidUtf8_IsRejected()
    {
        using var stream = new MemoryStream(
            new byte[] { 0xC3, 0x28 });

        var result = await DocumentContentValidator.IsValidAsync(
            stream, "text/plain");

        Assert.False(result);
    }

    [Fact]
    public async Task EmptyFile_IsRejected()
    {
        using var stream = new MemoryStream();

        var result = await DocumentContentValidator.IsValidAsync(
            stream, "text/plain");

        Assert.False(result);
    }

    [Fact]
    public async Task UnsupportedContentType_IsRejected()
    {
        using var stream = CreateStream("content");

        var result = await DocumentContentValidator.IsValidAsync(
            stream, "application/octet-stream");

        Assert.False(result);
    }

    [Fact]
    public async Task Validation_RestoresOriginalStreamPosition()
    {
        using var stream = CreateStream("Hello, world!");
        stream.Position = 4;

        var result = await DocumentContentValidator.IsValidAsync(
            stream, "text/plain");

        Assert.True(result);
        Assert.Equal(4, stream.Position);
    }

    private static MemoryStream CreateStream(string text)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(text));
    }
}
