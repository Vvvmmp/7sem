using System;
using System.Linq;
using System.Threading.Tasks;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Server.Storage;
using TicketSales.Server.Storage.Postgres;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Domain.Services;

public class TicketSalesServicePostgres : ITicketSalesService
{
    private readonly IEventRepository _eventRepository;
    private readonly ISeatRepository _seatRepository;
    private readonly ITicketRepository _ticketRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly PostgresConnectionFactory _connectionFactory;
    private readonly TimeSpan _serverTimeArtificialDelay;
    private readonly object _seatLock = new();

    public TicketSalesServicePostgres(
        IEventRepository eventRepository,
        ISeatRepository seatRepository,
        ITicketRepository ticketRepository,
        ICustomerRepository customerRepository,
        IPaymentRepository paymentRepository,
        PostgresConnectionFactory connectionFactory,
        TimeSpan serverTimeArtificialDelay = default)
    {
        _eventRepository = eventRepository;
        _seatRepository = seatRepository;
        _ticketRepository = ticketRepository;
        _customerRepository = customerRepository;
        _paymentRepository = paymentRepository;
        _connectionFactory = connectionFactory;
        _serverTimeArtificialDelay = serverTimeArtificialDelay;
    }

    public Task<GetEventsResponseDto> GetEvents(GetEventsRequestDto request)
    {
        var events = _eventRepository.GetAll().AsEnumerable();

        if (!string.IsNullOrWhiteSpace(request.TitleContains))
        {
            events = events.Where(e => e.Title.Contains(request.TitleContains, StringComparison.OrdinalIgnoreCase));
        }

        var result = new GetEventsResponseDto();
        foreach (var e in events)
        {
            var freeSeats = _seatRepository.GetByEvent(e.Id).Count(s => s.Status == SeatStatus.Free);
            result.Events.Add(new EventDto
            {
                Id = e.Id,
                Title = e.Title,
                StartsAt = e.StartsAt,
                Venue = e.Venue,
                BasePrice = e.BasePrice,
                FreeSeats = freeSeats
            });
        }

        return Task.FromResult(result);
    }

    public Task<ReserveSeatResponseDto> ReserveSeat(ReserveSeatRequestDto request)
    {
        if (request.EventId <= 0 || request.SeatId <= 0)
        {
            throw new ValidationException("Не указан идентификатор события или места");
        }

        if (string.IsNullOrWhiteSpace(request.CustomerFullName))
        {
            throw new ValidationException("Не указано имя покупателя");
        }

        if (string.IsNullOrWhiteSpace(request.CustomerEmail) || !request.CustomerEmail.Contains('@'))
        {
            throw new ValidationException("Некорректный email покупателя");
        }

        var eventEntity = _eventRepository.GetById(request.EventId)
            ?? throw new NotFoundException("Событие не найдено");

        var seatExists = _seatRepository.GetById(request.SeatId)
            ?? throw new NotFoundException("Место не найдено");

        if (seatExists.EventId != eventEntity.Id)
        {
            throw new ValidationException("Указанное место не принадлежит указанному событию");
        }

        var customer = _customerRepository.FindByEmail(request.CustomerEmail);
        if (customer is null)
        {
            customer = _customerRepository.Add(new Customer
            {
                FullName = request.CustomerFullName,
                Email = request.CustomerEmail
            });
        }

        int ticketId;
        SeatStatus finalStatus;

        lock (_seatLock)
        {
            Log($"ReserveSeat: seat={request.SeatId} LOCK");

            using var connection = _connectionFactory.Open();
            using var transaction = request.UseTransaction ? connection.BeginTransaction() : null;

            try
            {
                using var checkCommand = connection.CreateCommand();
                checkCommand.Transaction = transaction;
                checkCommand.CommandText = "SELECT status FROM seats WHERE id = @id";
                checkCommand.Parameters.AddWithValue("id", request.SeatId);
                var currentStatusText = (string)checkCommand.ExecuteScalar()!;
                var currentStatus = Enum.Parse<SeatStatus>(currentStatusText);

                Log($"ReserveSeat: seat={request.SeatId} проверка статуса -> {currentStatus}");

                if (currentStatus != SeatStatus.Free)
                {
                    Log($"ReserveSeat: seat={request.SeatId} FAIL (уже занято)");
                    Log($"ReserveSeat: seat={request.SeatId} UNLOCK");
                    throw new ValidationException("Место уже занято");
                }

                using var insertTicketCommand = connection.CreateCommand();
                insertTicketCommand.Transaction = transaction;
                insertTicketCommand.CommandText = """
                    INSERT INTO tickets (event_id, seat_id, customer_id, status, created_at)
                    VALUES (@eventId, @seatId, @customerId, @status, @createdAt)
                    RETURNING id;
                    """;
                insertTicketCommand.Parameters.AddWithValue("eventId", eventEntity.Id);
                insertTicketCommand.Parameters.AddWithValue("seatId", request.SeatId);
                insertTicketCommand.Parameters.AddWithValue("customerId", customer.Id);
                insertTicketCommand.Parameters.AddWithValue("status", TicketStatus.Reserved.ToString());
                insertTicketCommand.Parameters.AddWithValue("createdAt", DateTime.UtcNow);
                ticketId = (int)insertTicketCommand.ExecuteScalar()!;

                Log($"ReserveSeat: seat={request.SeatId} создан ticketId={ticketId} (Operation 1: Create Ticket)");

                if (request.SimulateErrorAfterTicketInsert)
                {
                    Log($"ReserveSeat: seat={request.SeatId} ИСКУССТВЕННАЯ ОШИБКА между Operation 1 и Operation 2");
                    throw new ValidationException("Искусственная ошибка между операциями (демонстрация транзакции)");
                }

                using var updateSeatCommand = connection.CreateCommand();
                updateSeatCommand.Transaction = transaction;
                updateSeatCommand.CommandText = "UPDATE seats SET status = @status WHERE id = @id";
                updateSeatCommand.Parameters.AddWithValue("status", SeatStatus.Reserved.ToString());
                updateSeatCommand.Parameters.AddWithValue("id", request.SeatId);
                updateSeatCommand.ExecuteNonQuery();

                Log($"ReserveSeat: seat={request.SeatId} обновлён статус -> Reserved (Operation 2: Update Seat)");

                transaction?.Commit();
                finalStatus = SeatStatus.Reserved;

                Log($"ReserveSeat: seat={request.SeatId} SUCCESS{(transaction != null ? " COMMIT" : "")}");
                Log($"ReserveSeat: seat={request.SeatId} UNLOCK");
            }
            catch
            {
                transaction?.Rollback();
                if (transaction != null)
                {
                    Log($"ReserveSeat: seat={request.SeatId} ROLLBACK");
                }
                throw;
            }
        }

        return Task.FromResult(new ReserveSeatResponseDto
        {
            TicketId = ticketId,
            Seat = new SeatDto
            {
                Id = request.SeatId,
                Row = seatExists.Row,
                Number = seatExists.Number,
                Status = finalStatus
            }
        });
    }

    public async Task<GetServerTimeResponseDto> GetServerTime()
    {
        if (_serverTimeArtificialDelay > TimeSpan.Zero)
        {
            await Task.Delay(_serverTimeArtificialDelay);
        }

        return new GetServerTimeResponseDto { ServerTimeUtc = DateTime.UtcNow };
    }

    public Task<GetSeatSnapshotResponseDto> GetSeatSnapshot(GetSeatSnapshotRequestDto request)
    {
        var seat = _seatRepository.GetById(request.SeatId)
            ?? throw new NotFoundException("Место не найдено");

        var ticketCount = _ticketRepository.GetAll().Count(t => t.SeatId == request.SeatId);

        return Task.FromResult(new GetSeatSnapshotResponseDto
        {
            Status = seat.Status,
            TicketCount = ticketCount
        });
    }

    private static void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        Console.WriteLine($"[{timestamp}] [Thread {Environment.CurrentManagedThreadId}] {message}");
    }

    public Task<BuyTicketResponseDto> BuyTicket(BuyTicketRequestDto request)
    {
        if (request.TicketId <= 0)
        {
            throw new ValidationException("Не указан идентификатор билета");
        }

        var ticketExists = _ticketRepository.GetById(request.TicketId)
            ?? throw new NotFoundException("Билет не найден");

        var eventEntity = _eventRepository.GetById(ticketExists.EventId)
            ?? throw new NotFoundException("Событие для билета не найдено");

        var seatExists = _seatRepository.GetById(ticketExists.SeatId)
            ?? throw new NotFoundException("Место для билета не найдено");

        lock (_seatLock)
        {
            Log($"BuyTicket: ticket={request.TicketId} LOCK");

            using var connection = _connectionFactory.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                using var checkCommand = connection.CreateCommand();
                checkCommand.Transaction = transaction;
                checkCommand.CommandText = "SELECT status FROM tickets WHERE id = @id";
                checkCommand.Parameters.AddWithValue("id", request.TicketId);
                var currentStatus = Enum.Parse<TicketStatus>((string)checkCommand.ExecuteScalar()!);

                Log($"BuyTicket: ticket={request.TicketId} проверка статуса -> {currentStatus}");

                if (currentStatus != TicketStatus.Reserved)
                {
                    Log($"BuyTicket: ticket={request.TicketId} FAIL (билет не может быть куплен в текущем статусе)");
                    Log($"BuyTicket: ticket={request.TicketId} UNLOCK");
                    throw new ValidationException("Билет не может быть куплен в текущем статусе");
                }

                using var updateSeatCommand = connection.CreateCommand();
                updateSeatCommand.Transaction = transaction;
                updateSeatCommand.CommandText = "UPDATE seats SET status = @status WHERE id = @id";
                updateSeatCommand.Parameters.AddWithValue("status", SeatStatus.Sold.ToString());
                updateSeatCommand.Parameters.AddWithValue("id", seatExists.Id);
                updateSeatCommand.ExecuteNonQuery();

                using var updateTicketCommand = connection.CreateCommand();
                updateTicketCommand.Transaction = transaction;
                updateTicketCommand.CommandText = "UPDATE tickets SET status = @status WHERE id = @id";
                updateTicketCommand.Parameters.AddWithValue("status", TicketStatus.Purchased.ToString());
                updateTicketCommand.Parameters.AddWithValue("id", request.TicketId);
                updateTicketCommand.ExecuteNonQuery();

                transaction.Commit();

                Log($"BuyTicket: ticket={request.TicketId} SUCCESS COMMIT");
                Log($"BuyTicket: ticket={request.TicketId} UNLOCK");
            }
            catch
            {
                transaction.Rollback();
                Log($"BuyTicket: ticket={request.TicketId} ROLLBACK");
                throw;
            }
        }

        var payment = _paymentRepository.Add(new Payment
        {
            TicketId = ticketExists.Id,
            Amount = eventEntity.BasePrice,
            Status = PaymentStatus.Completed,
            CreatedAt = DateTime.UtcNow
        });

        return Task.FromResult(new BuyTicketResponseDto
        {
            TicketId = ticketExists.Id,
            PaymentId = payment.Id,
            Amount = payment.Amount,
            Status = payment.Status
        });
    }

    public Task<CancelTicketResponseDto> CancelTicket(CancelTicketRequestDto request)
    {
        if (request.TicketId <= 0)
        {
            throw new ValidationException("Не указан идентификатор билета");
        }

        var ticketExists = _ticketRepository.GetById(request.TicketId)
            ?? throw new NotFoundException("Билет не найден");

        var seatExists = _seatRepository.GetById(ticketExists.SeatId);

        lock (_seatLock)
        {
            Log($"CancelTicket: ticket={request.TicketId} LOCK");

            using var connection = _connectionFactory.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                using var checkCommand = connection.CreateCommand();
                checkCommand.Transaction = transaction;
                checkCommand.CommandText = "SELECT status FROM tickets WHERE id = @id";
                checkCommand.Parameters.AddWithValue("id", request.TicketId);
                var currentStatus = Enum.Parse<TicketStatus>((string)checkCommand.ExecuteScalar()!);

                Log($"CancelTicket: ticket={request.TicketId} проверка статуса -> {currentStatus}");

                if (currentStatus == TicketStatus.Cancelled)
                {
                    Log($"CancelTicket: ticket={request.TicketId} FAIL (билет уже отменен)");
                    Log($"CancelTicket: ticket={request.TicketId} UNLOCK");
                    throw new ValidationException("Билет уже отменен");
                }

                if (seatExists is not null)
                {
                    using var updateSeatCommand = connection.CreateCommand();
                    updateSeatCommand.Transaction = transaction;
                    updateSeatCommand.CommandText = "UPDATE seats SET status = @status WHERE id = @id";
                    updateSeatCommand.Parameters.AddWithValue("status", SeatStatus.Free.ToString());
                    updateSeatCommand.Parameters.AddWithValue("id", seatExists.Id);
                    updateSeatCommand.ExecuteNonQuery();
                }

                using var updateTicketCommand = connection.CreateCommand();
                updateTicketCommand.Transaction = transaction;
                updateTicketCommand.CommandText = "UPDATE tickets SET status = @status WHERE id = @id";
                updateTicketCommand.Parameters.AddWithValue("status", TicketStatus.Cancelled.ToString());
                updateTicketCommand.Parameters.AddWithValue("id", request.TicketId);
                updateTicketCommand.ExecuteNonQuery();

                transaction.Commit();

                Log($"CancelTicket: ticket={request.TicketId} SUCCESS COMMIT");
                Log($"CancelTicket: ticket={request.TicketId} UNLOCK");
            }
            catch
            {
                transaction.Rollback();
                Log($"CancelTicket: ticket={request.TicketId} ROLLBACK");
                throw;
            }
        }

        return Task.FromResult(new CancelTicketResponseDto
        {
            TicketId = ticketExists.Id,
            Status = TicketStatus.Cancelled
        });
    }
}
