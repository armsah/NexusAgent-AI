using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        documents.MapDelete(
            "/{documentId:guid}",
            async (
                Guid documentId,
                [FromBody] DeleteDocumentRequest request,
                DeleteDocumentHandler handler,
                DevelopmentRequestIdentity identity,
                CancellationToken cancellationToken) =>
            {
                if (request is null || request.ExpectedVersion < 1)
                {
                    return Results.BadRequest(new
                    {
                        error = "A valid expectedVersion is required."
                    });
                }

                try
                {
                    var result = await handler.HandleAsync(
                        new DeleteDocumentCommand(
                            TenantId: identity.TenantId,
                            RequesterId: identity.RequesterId,
                            DocumentId: documentId,
                            ExpectedVersion: request.ExpectedVersion),
                        cancellationToken);

                    return result is null
                        ? Results.NotFound()
                        : Results.Ok(result);
                }
                catch (DbUpdateConcurrencyException)
                {
                    return Results.Conflict(new
                    {
                        error = "Document version conflict."
                    });
                }
                catch (DomainException exception)
                    when (exception.Message == "Document version conflict.")
                {
                    return Results.Conflict(new
                    {
                        error = "Document version conflict."
                    });
                }
            })
            .WithName("DeleteDocument")
            .Produces<DeleteDocumentResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        documents.MapPut(
            "/{documentId:guid}",
            async (
                Guid documentId,
                UpdateDocumentRequest request,
                UpdateDocumentHandler handler,
                DevelopmentRequestIdentity identity,
                CancellationToken cancellationToken) =>
            {
                if (request is null ||
                    request.ExpectedVersion < 1 ||
                    string.IsNullOrWhiteSpace(request.Title) ||
                    string.IsNullOrWhiteSpace(request.Classification) ||
                    request.Title.Length > 256 ||
                    request.Classification.Length > 64)
                {
                    return Results.BadRequest(new
                    {
                        error = "Valid expectedVersion, title, and classification are required."
                    });
                }

                try
                {
                    var result = await handler.HandleAsync(
                        new UpdateDocumentCommand(
                            TenantId: identity.TenantId,
                            RequesterId: identity.RequesterId,
                            DocumentId: documentId,
                            ExpectedVersion: request.ExpectedVersion,
                            Title: request.Title,
                            Classification: request.Classification,
                            AllowedPrincipalIds: [identity.RequesterId]),
                        cancellationToken);

                    return result is null
                        ? Results.NotFound()
                        : Results.Ok(result);
                }
                catch (DbUpdateConcurrencyException)
                {
                    return Results.Conflict(new
                    {
                        error = "Document version conflict."
                    });
                }
                catch (DomainException exception)
                    when (exception.Message == "Document version conflict.")
                {
                    return Results.Conflict(new
                    {
                        error = "Document version conflict."
                    });
                }
                catch (DomainException exception)
                {
                    return Results.BadRequest(new
                    {
                        error = exception.Message
                    });
                }
            })
            .WithName("UpdateDocument")
            .Produces<UpdateDocumentResult>(
                StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        documents.MapPut(
            "/{documentId:guid}/content",
            async (
                Guid documentId,
                HttpRequest request,
                UpdateDocumentContentHandler handler,
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

                var expectedVersionValues = form["expectedVersion"];

                if (expectedVersionValues.Count != 1 ||
                    !int.TryParse(
                        expectedVersionValues[0],
                        out var expectedVersion) ||
                    expectedVersion < 1)
                {
                    return Results.BadRequest(new
                    {
                        error = "A valid expectedVersion is required."
                    });
                }

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
                    var result = await handler.HandleAsync(
                        new UpdateDocumentContentCommand(
                            TenantId: identity.TenantId,
                            RequesterId: identity.RequesterId,
                            DocumentId: documentId,
                            ExpectedVersion: expectedVersion,
                            Content: stream),
                        cancellationToken);

                    return result is null
                        ? Results.NotFound()
                        : Results.Ok(result);
                }
                catch (DocumentContentVersionConflictException)
                {
                    return Results.Conflict(new
                    {
                        error = "Document content version conflict."
                    });
                }
                catch (DbUpdateConcurrencyException)
                {
                    return Results.Conflict(new
                    {
                        error = "Document version conflict."
                    });
                }
                catch (DomainException exception)
                    when (exception.Message == "Document version conflict.")
                {
                    return Results.Conflict(new
                    {
                        error = "Document version conflict."
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
            .WithName("UpdateDocumentContent")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<UpdateDocumentContentResult>(
                StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status413PayloadTooLarge);
        documents.MapGet(
            "/{documentId:guid}",
            async (
                Guid documentId,
                GetDocumentHandler handler,
                DevelopmentRequestIdentity identity,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(
                    new GetDocumentQuery(
                        identity.TenantId,
                        identity.RequesterId,
                        documentId),
                    cancellationToken);

                return result is null
                    ? Results.NotFound()
                    : Results.Ok(result);
            })
            .WithName("GetDocument")
            .Produces<GetDocumentResult>(
                StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
        documents.MapGet(
            "/{documentId:guid}/content",
            async (
                Guid documentId,
                GetDocumentContentHandler handler,
                DevelopmentRequestIdentity identity,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(
                    new GetDocumentContentQuery(
                        identity.TenantId,
                        identity.RequesterId,
                        documentId),
                    cancellationToken);

                if (result is null)
                {
                    return Results.NotFound();
                }

                return Results.File(
                    result.Content,
                    contentType: "application/octet-stream",
                    fileDownloadName: $"{documentId:N}-v{result.Version}.bin");
            })
            .WithName("GetDocumentContent")
            .Produces(
                StatusCodes.Status200OK,
                contentType: "application/octet-stream")
            .Produces(StatusCodes.Status404NotFound);
        return endpoints;
    }
}





public sealed record UpdateDocumentRequest(
    int ExpectedVersion,
    string Title,
    string Classification);

public sealed record DeleteDocumentRequest(int ExpectedVersion);
