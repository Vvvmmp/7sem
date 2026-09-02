using System;
using TicketSales.Server.Storage;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Storage.InMemory;

public static class SeedData
{
    public const int FirstEventId = 1;
    public const int FirstEventFirstSeatId = 1;
    public const int SecondEventId = 2;

    public static void Populate(IEventRepository eventRepository, ISeatRepository seatRepository)
    {
        eventRepository.Add(new Event
        {
            Title = "Концерт группы Horizon",
            Venue = "Дворец спорта",
            StartsAt = DateTime.UtcNow.AddDays(14),
            BasePrice = 1500m
        });

        for (var row = 1; row <= 2; row++)
        {
            for (var number = 1; number <= 5; number++)
            {
                seatRepository.Add(new Seat
                {
                    EventId = FirstEventId,
                    Row = row.ToString(),
                    Number = number,
                    Status = SeatStatus.Free
                });
            }
        }

        eventRepository.Add(new Event
        {
            Title = "Стендап вечер",
            Venue = "Клуб Смех",
            StartsAt = DateTime.UtcNow.AddDays(7),
            BasePrice = 800m
        });

        for (var number = 1; number <= 8; number++)
        {
            seatRepository.Add(new Seat
            {
                EventId = SecondEventId,
                Row = "1",
                Number = number,
                Status = SeatStatus.Free
            });
        }
    }
}
