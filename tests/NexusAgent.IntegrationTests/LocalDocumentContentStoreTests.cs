using System.Security.Cryptography;
using System.Text;
using NexusAgent.Infrastructure.Storage;

namespace NexusAgent.IntegrationTests;

public sealed class LocalDocumentContentStoreTests
{
    [Fact]
    public async Task PutAndRead_PreservesContentAndSha256()
    {
        var root = CreateTestDirectory();

        try
        {
            var store = new LocalDocumentContentStore(root);
            var documentId = Guid.NewGuid();
            var content = Encoding.UTF8.GetBytes("NexusAgent knowledge document");

            await using var input = new MemoryStream(content);

            var result = await store.PutAsync(
                "tenant-a", documentId, 1, input);

            await using var output = await store.OpenReadAsync(
                "tenant-a", documentId, 1);

            using var buffer = new MemoryStream();
            await output.CopyToAsync(buffer);

            Assert.Equal(content, buffer.ToArray());
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
                result.Sha256);
            Assert.Equal(content.LongLength, result.Length);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DifferentTenants_CannotReadEachOthersContent()
    {
        var root = CreateTestDirectory();

        try
        {
            var store = new LocalDocumentContentStore(root);
            var documentId = Guid.NewGuid();

            await using var input = new MemoryStream(
                Encoding.UTF8.GetBytes("tenant-a private content"));

            await store.PutAsync("tenant-a", documentId, 1, input);

            await Assert.ThrowsAsync<FileNotFoundException>(
                async () =>
                {
                    await using var stream = await store.OpenReadAsync(
                        "tenant-b", documentId, 1);
                });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DifferentVersions_PreserveIndependentContent()
    {
        var root = CreateTestDirectory();

        try
        {
            var store = new LocalDocumentContentStore(root);
            var documentId = Guid.NewGuid();

            await using var first = new MemoryStream(
                Encoding.UTF8.GetBytes("version-one"));

            await using var second = new MemoryStream(
                Encoding.UTF8.GetBytes("version-two"));

            await store.PutAsync("tenant-a", documentId, 1, first);
            await store.PutAsync("tenant-a", documentId, 2, second);

            await using var firstRead = await store.OpenReadAsync(
                "tenant-a", documentId, 1);

            await using var secondRead = await store.OpenReadAsync(
                "tenant-a", documentId, 2);

            using var firstBuffer = new MemoryStream();
            using var secondBuffer = new MemoryStream();

            await firstRead.CopyToAsync(firstBuffer);
            await secondRead.CopyToAsync(secondBuffer);

            Assert.Equal(
                "version-one",
                Encoding.UTF8.GetString(firstBuffer.ToArray()));

            Assert.Equal(
                "version-two",
                Encoding.UTF8.GetString(secondBuffer.ToArray()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DuplicateVersionWrite_DoesNotOverwriteOriginal()
    {
        var root = CreateTestDirectory();

        try
        {
            var store = new LocalDocumentContentStore(root);
            var documentId = Guid.NewGuid();

            await using var original = new MemoryStream(
                Encoding.UTF8.GetBytes("original"));

            await store.PutAsync("tenant-a", documentId, 1, original);

            await using var replacement = new MemoryStream(
                Encoding.UTF8.GetBytes("replacement"));

            var exception = await Assert.ThrowsAsync<
                NexusAgent.Application.Documents.DocumentContentVersionConflictException>(
                () => store.PutAsync(
                    "tenant-a", documentId, 1, replacement));

            Assert.IsType<IOException>(exception.InnerException);

            await using var output = await store.OpenReadAsync(
                "tenant-a", documentId, 1);

            using var buffer = new MemoryStream();
            await output.CopyToAsync(buffer);

            Assert.Equal(
                "original",
                Encoding.UTF8.GetString(buffer.ToArray()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DeleteVersion_DoesNotDeleteOtherVersions()
    {
        var root = CreateTestDirectory();

        try
        {
            var store = new LocalDocumentContentStore(root);
            var documentId = Guid.NewGuid();

            await using var first = new MemoryStream(
                Encoding.UTF8.GetBytes("version-one"));

            await using var second = new MemoryStream(
                Encoding.UTF8.GetBytes("version-two"));

            await store.PutAsync("tenant-a", documentId, 1, first);
            await store.PutAsync("tenant-a", documentId, 2, second);

            await store.DeleteAsync("tenant-a", documentId, 1);

            await Assert.ThrowsAsync<FileNotFoundException>(
                async () =>
                {
                    await using var missing = await store.OpenReadAsync(
                        "tenant-a", documentId, 1);
                });

            await using var remaining = await store.OpenReadAsync(
                "tenant-a", documentId, 2);

            using var buffer = new MemoryStream();
            await remaining.CopyToAsync(buffer);

            Assert.Equal(
                "version-two",
                Encoding.UTF8.GetString(buffer.ToArray()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LargeDocument_PreservesContentIntegrity()
    {
        var root = CreateTestDirectory();

        try
        {
            var store = new LocalDocumentContentStore(root);
            var documentId = Guid.NewGuid();

            var content = new byte[10 * 1024 * 1024];
            RandomNumberGenerator.Fill(content);

            await using var input = new MemoryStream(content);

            var stored = await store.PutAsync(
                "tenant-a", documentId, 1, input);

            Assert.Equal(content.LongLength, stored.Length);

            await using var output = await store.OpenReadAsync(
                "tenant-a", documentId, 1);

            var actualHash = Convert.ToHexString(
                await SHA256.HashDataAsync(output))
                .ToLowerInvariant();

            Assert.Equal(stored.Sha256, actualHash);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
    private static string CreateTestDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "nexusagent-storage-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);

        return path;
    }
}
