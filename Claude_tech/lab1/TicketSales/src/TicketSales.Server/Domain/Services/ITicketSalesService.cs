using System.Threading.Tasks;
using TicketSales.Shared.Dto;

namespace TicketSales.Server.Domain.Services;

public interface ITicketSalesService
{
    Task<GetEventsResponseDto> GetEvents(GetEventsRequestDto request);
    Task<ReserveSeatResponseDto> ReserveSeat(ReserveSeatRequestDto request);
    Task<BuyTicketResponseDto> BuyTicket(BuyTicketRequestDto request);
    Task<CancelTicketResponseDto> CancelTicket(CancelTicketRequestDto request);
}
