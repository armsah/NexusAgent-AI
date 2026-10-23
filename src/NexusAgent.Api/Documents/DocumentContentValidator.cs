using System.Text;

namespace NexusAgent.Api.Documents;

public static class DocumentContentValidator
{
    private static readonly byte[] PdfSignature =
        "%PDF-"u8.ToArray();

    private static readonly UTF8Encoding StrictUtf8 =
        new(false, true);

    public static async Task<bool> IsValidAsync(
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!content.CanRead || !content.CanSeek)
            return false;

        var originalPosition = content.Position;

        try
        {
            if (content.Length == 0)
                return false;

            content.Position = 0;

            if (contentType.Equals(
                    "application/pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                var header = new byte[PdfSignature.Length];

                var read = await content.ReadAsync(
                    header,
                    cancellationToken);

                return read == PdfSignature.Length &&
                       header.AsSpan().SequenceEqual(PdfSignature);
            }

            if (contentType.Equals(
                    "text/plain",
                    StringComparison.OrdinalIgnoreCase) ||
                contentType.Equals(
                    "text/markdown",
                    StringComparison.OrdinalIgnoreCase))
            {
                using var buffer = new MemoryStream();

                await content.CopyToAsync(
                    buffer,
                    cancellationToken);

                try
                {
                    StrictUtf8.GetString(buffer.ToArray());
                    return true;
                }
                catch (DecoderFallbackException)
                {
                    return false;
                }
            }

            return false;
        }
        finally
        {
            content.Position = originalPosition;
        }
    }
}
