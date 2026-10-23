using System.Security.Cryptography;
using System.Text;
using NexusAgent.Application.Documents;

namespace NexusAgent.Infrastructure.Storage;

public sealed class LocalDocumentContentStore(string rootDirectory)
    : IDocumentContentStore
{
    public async Task<StoredDocumentContent> PutAsync(
        string tenantId,
        Guid documentId,
        int version,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var path = GetPath(tenantId, documentId, version);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true))
            {
                await content.CopyToAsync(output, cancellationToken);
            }

            string hash;
            long length;

            await using (var input = new FileStream(
                temporaryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true))
            {
                length = input.Length;

                var hashBytes = await SHA256.HashDataAsync(
                    input,
                    cancellationToken);

                hash = Convert.ToHexString(hashBytes)
                    .ToLowerInvariant();
            }

            try
            {
                File.Move(temporaryPath, path, overwrite: false);
            }
            catch (IOException exception) when (File.Exists(path))
            {
                throw new DocumentContentVersionConflictException(
                    exception);
            }

            return new StoredDocumentContent(
                GetStorageKey(tenantId, documentId, version),
                hash,
                length);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
    public Task<Stream> OpenReadAsync(
        string tenantId,
        Guid documentId,
        int version,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = GetPath(tenantId, documentId, version);

        try
        {
            Stream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true);

            return Task.FromResult(stream);
        }
        catch (DirectoryNotFoundException)
        {
            throw new FileNotFoundException(
                "Document content was not found.");
        }
    }

    public Task DeleteAsync(
        string tenantId,
        Guid documentId,
        int version,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = GetPath(tenantId, documentId, version);

        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    private string GetPath(
        string tenantId,
        Guid documentId,
        int version)
    {
        return Path.Combine(
            rootDirectory,
            GetStorageKey(tenantId, documentId, version)
                .Replace('/', Path.DirectorySeparatorChar));
    }

    private static string GetStorageKey(
        string tenantId,
        Guid documentId,
        int version)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException(
                "Tenant ID is required.", nameof(tenantId));

        if (documentId == Guid.Empty)
            throw new ArgumentException(
                "Document ID is required.", nameof(documentId));

        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version));

        var tenantHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(tenantId)))
            .ToLowerInvariant();

        return $"{tenantHash}/{documentId:N}/v{version:D8}.bin";
    }
}
