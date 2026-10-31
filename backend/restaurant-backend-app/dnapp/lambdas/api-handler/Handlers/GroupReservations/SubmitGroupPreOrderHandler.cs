using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using APIHandler.Models.GroupReservation;
using APIHandler.Services.GroupReservations;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace APIHandler.Handlers.GroupReservations;

/// <summary>POST /group-reservations/{groupReservationId}/submit — owner confirms the consolidated pre-order.</summary>
public class SubmitGroupPreOrderHandler : IApiRequestHandler
{
    private readonly IGroupPreOrderService _groupPreOrderService;

    public SubmitGroupPreOrderHandler(IGroupPreOrderService groupPreOrderService)
    {
        _groupPreOrderService = groupPreOrderService ?? throw new ArgumentNullException(nameof(groupPreOrderService));
    }

    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!request.TryGetPathValue("groupReservationId", out var groupReservationId))
        {
            return ResponseBuilder.CreateErrorResponse(400, "Missing 'groupReservationId' in path parameters.", context.Logger);
        }

        if (request.RequestContext?.Authorizer?.Claims == null ||
            !request.RequestContext.Authorizer.Claims.TryGetValue("sub", out var userId))
        {
            return ResponseBuilder.CreateErrorResponse(401, "Unauthorized.", context.Logger);
        }

        if (!TryReadReservationId(request.Body, out var reservationId))
        {
            return ResponseBuilder.CreateErrorResponse(400, "Request body must contain 'reservationId'.", context.Logger);
        }

        try
        {
            var dishes = await _groupPreOrderService.SubmitAsync(userId, groupReservationId, reservationId);
            return ResponseBuilder.CreateJsonResponse(200, dishes);
        }
        // NotFound exceptions derive from ValidationException, so they must be caught first.
        catch (GroupReservationNotFoundException ex)
        {
            return ResponseBuilder.CreateErrorResponse(404, ex.Message, context.Logger);
        }
        catch (ReservationNotFoundException ex)
        {
            return ResponseBuilder.CreateErrorResponse(404, ex.Message, context.Logger);
        }
        catch (ActionForbiddenException ex)
        {
            return ResponseBuilder.CreateErrorResponse(403, ex.Message, context.Logger);
        }
        catch (ValidationException ex)
        {
            return ResponseBuilder.CreateErrorCodeResponse(ex);
        }
    }

    private static bool TryReadReservationId(string? body, out string reservationId)
    {
        reservationId = string.Empty;
        if (string.IsNullOrWhiteSpace(body))
            return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<SubmitGroupPreOrderRequest>(
                body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (string.IsNullOrWhiteSpace(parsed?.ReservationId))
                return false;

            reservationId = parsed.ReservationId;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
