using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Server.Storage;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Domain.Services;

public class TicketSalesService : ITicketSalesService
{
    private readonly IEventRepository _eventRepository;
    private readonly ISeatRepository _seatRepository;
    private readonly ITicketRepository _ticketRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly TimeSpan _serverTimeArtificialDelay;
    private readonly TimeSpan _reservationDelay;
    private readonly object _seatLock = new();

    public TicketSalesService(
        IEventRepository eventRepository,
        ISeatRepository seatRepository,
        ITicketRepository ticketRepository,
        ICustomerRepository customerRepository,
        IPaymentRepository paymentRepository,
        TimeSpan serverTimeArtificialDelay = default,
        TimeSpan reservationDelay = default)
    {
        _eventRepository = eventRepository;
        _seatRepository = seatRepository;
        _ticketRepository = ticketRepository;
        _customerRepository = customerRepository;
        _paymentRepository = paymentRepository;
        _serverTimeArtificialDelay = serverTimeArtificialDelay;
        _reservationDelay = reservationDelay;
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

        var seat = _seatRepository.GetById(request.SeatId)
            ?? throw new NotFoundException("Место не найдено");

        if (seat.EventId != eventEntity.Id)
        {
            throw new ValidationException("Указанное место не принадлежит указанному событию");
        }

        // Поиск/создание покупателя не относится к общему ресурсу (месту) и не должен
        // сериализоваться вместе с ним — выполняется до входа в критическую секцию.
        var customer = _customerRepository.FindByEmail(request.CustomerEmail);
        if (customer is null)
        {
            customer = _customerRepository.Add(new Customer
            {
                FullName = request.CustomerFullName,
                Email = request.CustomerEmail
            });
        }

        Ticket ticket;
        lock (_seatLock)
        {
            Log($"ReserveSeat: seat={seat.Id} LOCK");
            Log($"ReserveSeat: seat={seat.Id} проверка статуса -> {seat.Status}");

            if (_reservationDelay > TimeSpan.Zero)
            {
                Thread.Sleep(_reservationDelay);
            }

            if (seat.Status != SeatStatus.Free)
            {
                Log($"ReserveSeat: seat={seat.Id} FAIL (уже занято)");
                Log($"ReserveSeat: seat={seat.Id} UNLOCK");
                throw new ValidationException("Место уже занято");
            }

            seat.Status = SeatStatus.Reserved;
            _seatRepository.Update(seat);

            ticket = _ticketRepository.Add(new Ticket
            {
                EventId = eventEntity.Id,
                SeatId = seat.Id,
                CustomerId = customer.Id,
                Status = TicketStatus.Reserved,
                CreatedAt = DateTime.UtcNow
            });

            Log($"ReserveSeat: seat={seat.Id} SUCCESS (customer={customer.Email}, ticketId={ticket.Id})");
            Log($"ReserveSeat: seat={seat.Id} UNLOCK");
        }

        return Task.FromResult(new ReserveSeatResponseDto
        {
            TicketId = ticket.Id,
            Seat = new SeatDto
            {
                Id = seat.Id,
                Row = seat.Row,
                Number = seat.Number,
                Status = seat.Status
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

        var ticket = _ticketRepository.GetById(request.TicketId)
            ?? throw new NotFoundException("Билет не найден");

        if (ticket.Status != TicketStatus.Reserved)
        {
            throw new ValidationException("Билет не может быть куплен в текущем статусе");
        }

        var eventEntity = _eventRepository.GetById(ticket.EventId)
            ?? throw new NotFoundException("Событие для билета не найдено");

        var seat = _seatRepository.GetById(ticket.SeatId)
            ?? throw new NotFoundException("Место для билета не найдено");

        lock (_seatLock)
        {
            Log($"BuyTicket: seat={seat.Id} LOCK");
            seat.Status = SeatStatus.Sold;
            _seatRepository.Update(seat);

            ticket.Status = TicketStatus.Purchased;
            _ticketRepository.Update(ticket);
            Log($"BuyTicket: seat={seat.Id} SUCCESS, UNLOCK");
        }

        var payment = _paymentRepository.Add(new Payment
        {
            TicketId = ticket.Id,
            Amount = eventEntity.BasePrice,
            Status = PaymentStatus.Completed,
            CreatedAt = DateTime.UtcNow
        });

        return Task.FromResult(new BuyTicketResponseDto
        {
            TicketId = ticket.Id,
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

        var ticket = _ticketRepository.GetById(request.TicketId)
            ?? throw new NotFoundException("Билет не найден");

        if (ticket.Status == TicketStatus.Cancelled)
        {
            throw new ValidationException("Билет уже отменен");
        }

        var seat = _seatRepository.GetById(ticket.SeatId);
        lock (_seatLock)
        {
            Log($"CancelTicket: ticket={ticket.Id} LOCK");
            if (seat is not null)
            {
                seat.Status = SeatStatus.Free;
                _seatRepository.Update(seat);
            }

            ticket.Status = TicketStatus.Cancelled;
            _ticketRepository.Update(ticket);
            Log($"CancelTicket: ticket={ticket.Id} SUCCESS, UNLOCK");
        }

        return Task.FromResult(new CancelTicketResponseDto
        {
            TicketId = ticket.Id,
            Status = ticket.Status
        });
    }
}
