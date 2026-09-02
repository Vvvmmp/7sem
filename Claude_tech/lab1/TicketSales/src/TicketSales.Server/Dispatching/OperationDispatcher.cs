using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Shared.Protocol;

namespace TicketSales.Server.Dispatching;

public class OperationDispatcher
{
    private readonly IReadOnlyDictionary<string, IOperationHandler> _handlers;

    public OperationDispatcher(IEnumerable<IOperationHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.OperationName, StringComparer.OrdinalIgnoreCase);
    }

    public Response Dispatch(Request request)
    {
        if (string.IsNullOrWhiteSpace(request.Operation) || !_handlers.TryGetValue(request.Operation, out var handler))
        {
            return Response.Fail(request.RequestId, ErrorCodes.UnknownOperation, $"Неизвестная операция: '{request.Operation}'");
        }

        try
        {
            var result = handler.Handle(request.Payload);
            return Response.Ok(request.RequestId, result);
        }
        catch (DomainException ex)
        {
            return Response.Fail(request.RequestId, ex.Code, ex.Message);
        }
        catch (JsonException)
        {
            return Response.Fail(request.RequestId, ErrorCodes.ValidationError, "Некорректные входные данные операции");
        }
        catch (Exception)
        {
            return Response.Fail(request.RequestId, ErrorCodes.InternalError, "Внутренняя ошибка сервера");
        }
    }
}
