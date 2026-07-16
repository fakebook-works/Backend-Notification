using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NotificationService.Domain;
using NotificationService.Services;

namespace NotificationService.Api;

public static class InternalNotificationEndpoints
{
    public static IEndpointRouteBuilder MapInternalNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/internal/notifications", CreateNotificationAsync)
            .WithName("CreateInternalNotification")
            .WithOpenApi();

        return endpoints;
    }

    private static async Task<Results<Created<NotificationResponse>, Ok<NotificationResponse>, Conflict<ProblemDetails>, ValidationProblem>> CreateNotificationAsync(
        CreateNotificationRequest request,
        HttpRequest httpRequest,
        INotificationWriter notificationWriter,
        CancellationToken cancellationToken)
    {
        var idempotencyValues = httpRequest.Headers["Idempotency-Key"];
        var idempotencyKey = idempotencyValues.Count == 1 ? idempotencyValues[0] : null;
        var errors = Validate(request, idempotencyKey);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        NotificationCreateResult result;
        try
        {
            result = await notificationWriter.CreateAsync(
                new CreateNotificationCommand(request.CreatorId, request.ReceiverId, request.ActionType, request.ObjectId),
                idempotencyKey!,
                cancellationToken);
        }
        catch (IdempotencyConflictException)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Idempotency key conflict",
                Detail = "The Idempotency-Key was already used with a different notification payload.",
                Status = StatusCodes.Status409Conflict
            });
        }
        var response = NotificationResponse.From(result.Notification);

        return result.WasCreated
            ? TypedResults.Created($"/internal/notifications/{response.Id}", response)
            : TypedResults.Ok(response);
    }

    private static Dictionary<string, string[]> Validate(CreateNotificationRequest request, string? idempotencyKey)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.CreatorId <= 0)
        {
            errors[nameof(request.CreatorId)] = ["creatorId must be a positive integer."];
        }

        if (request.ReceiverId <= 0)
        {
            errors[nameof(request.ReceiverId)] = ["receiverId must be a positive integer."];
        }

        if (request.ObjectId <= 0)
        {
            errors[nameof(request.ObjectId)] = ["objectId must be a positive integer."];
        }

        if (!Enum.IsDefined(request.ActionType))
        {
            errors[nameof(request.ActionType)] = ["actionType must be a supported NotificationActionType value from 0 through 9."];
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            errors["Idempotency-Key"] = ["The Idempotency-Key header is required."];
        }
        else if (idempotencyKey.Length > 128)
        {
            errors["Idempotency-Key"] = ["The Idempotency-Key header must be at most 128 characters."];
        }

        return errors;
    }
}

public sealed record CreateNotificationRequest(
    long CreatorId,
    long ReceiverId,
    NotificationActionType ActionType,
    long ObjectId);

public sealed record NotificationResponse(
    long Id,
    long CreatorId,
    long ReceiverId,
    NotificationActionType ActionType,
    long ObjectId,
    DateTimeOffset CreatedAt,
    bool IsRead)
{
    public static NotificationResponse From(Notification notification) => new(
        notification.Id,
        notification.CreatorId,
        notification.ReceiverId,
        notification.ActionType,
        notification.ObjectId,
        notification.CreatedAt,
        notification.IsRead);
}
