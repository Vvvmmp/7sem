using TicketSales.Shared.Dto;

namespace TicketSales.Server.Domain.Services;

public interface ITicketSalesService
{
    GetEventsResponseDto GetEvents(GetEventsRequestDto request);
    ReserveSeatResponseDto ReserveSeat(ReserveSeatRequestDto request);
    BuyTicketResponseDto BuyTicket(BuyTicketRequestDto request);
    CancelTicketResponseDto CancelTicket(CancelTicketRequestDto request);
}
