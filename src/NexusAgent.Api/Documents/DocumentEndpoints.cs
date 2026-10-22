using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Features;
using NexusAgent.Api.Identity;
using NexusAgent.Application.Documents;
using NexusAgent.Domain.Common;

namespace NexusAgent.Api.Documents;

public static class DocumentEndpoints
{
    private const long MaxFileSize = 10 * 1024 * 1024;

    private static readonly HashSet<string> AllowedContentTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "application/pdf",
            "text/plain",
            "text/markdown"
        };

    public static IEndpointRouteBuilder MapDocumentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var documents = endpoints.MapGroup("/api/documents");

        documents.MapPost(
            "/",
            async (
                HttpRequest request,
                CreateDocumentHandler handler,
                DevelopmentRequestIdentity identity,
                CancellationToken cancellationToken) =>
            {
                if (!request.HasFormContentType)
                {
                    return Results.BadRequest(new
                    {
                        error = "Multipart form data is required."
                    });
                }

                IFormCollection form;

                try
                {
                    form = await request.ReadFormAsync(cancellationToken);
                }
                catch (BadHttpRequestException)
                {
                    return Results.BadRequest(new
                    {
                        error = "Invalid multipart form data."
                    });
                }
                catch (InvalidDataException)
                {
                    return Results.BadRequest(new
                    {
                        error = "Invalid multipart form data."
                    });
                }

                var files = form.Files.GetFiles("file");

                if (files.Count != 1 || form.Files.Count != 1)
                {
                    return Results.BadRequest(new
                    {
                        error = "Exactly one file is required."
                    });
                }

                var file = files[0];

                if (file.Length == 0)
                {
                    return Results.BadRequest(new
                    {
                        error = "File cannot be empty."
                    });
                }

                if (file.Length > MaxFileSize)
                {
                    return Results.StatusCode(
                        StatusCodes.Status413PayloadTooLarge);
                }

                if (!AllowedContentTypes.Contains(file.ContentType))
                {
                    return Results.BadRequest(new
                    {
                        error = "Unsupported content type."
                    });
                }

                var title = form["title"].ToString();
                var classification = form["classification"].ToString();

                if (string.IsNullOrWhiteSpace(title) ||
                    string.IsNullOrWhiteSpace(classification))
                {
                    return Results.BadRequest(new
                    {
                        error = "Title and classification are required."
                    });
                }

                if (title.Length > 256 || classification.Length > 64)
                {
                    return Results.BadRequest(new
                    {
                        error = "Document metadata exceeds allowed length."
                    });
                }

                // Development-only policy:
                // The server determines tenant and owner.
                // Clients cannot grant document access to arbitrary users.
                var ownerId = identity.RequesterId;

                await using var stream = file.OpenReadStream();

                if (!await DocumentContentValidator.IsValidAsync(
                    stream,
                    file.ContentType,
                    cancellationToken))
                {
                    return Results.BadRequest(new
                    {
                        error = "File content does not match its declared type."
                    });
                }

                try
                {
                    var command = new CreateDocumentCommand(
                        TenantId: identity.TenantId,
                        Title: title,
                        Classification: classification,
                        OwnerId: ownerId,
                        AllowedPrincipalIds: [ownerId],
                        Content: stream);

                    var result = await handler.HandleAsync(
                        command,
                        cancellationToken);

                    return Results.Created(
                        $"/api/documents/{result.DocumentId}",
                        result);
                }
                catch (DomainException exception)
                {
                    return Results.BadRequest(new
                    {
                        error = exception.Message
                    });
                }
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(11 * 1024 * 1024))
            .WithMetadata(new RequestFormLimitsAttribute
            {
                MultipartBodyLengthLimit = 10 * 1024 * 1024,
                ValueLengthLimit = 4096,
                ValueCountLimit = 8,
                MultipartHeadersLengthLimit = 4096
            })
            .WithName("CreateDocument")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<CreateDocumentResult>(
                StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status413PayloadTooLarge);

        return endpoints;
    }
}
