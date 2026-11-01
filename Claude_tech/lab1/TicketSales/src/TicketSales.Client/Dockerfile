FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/TicketSales.Shared/TicketSales.Shared.csproj src/TicketSales.Shared/
COPY src/TicketSales.Client/TicketSales.Client.csproj src/TicketSales.Client/
RUN dotnet restore src/TicketSales.Client/TicketSales.Client.csproj

COPY src/TicketSales.Shared/ src/TicketSales.Shared/
COPY src/TicketSales.Client/ src/TicketSales.Client/
RUN dotnet publish src/TicketSales.Client/TicketSales.Client.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV TICKETSALES_HOST=server \
    TICKETSALES_PORT=5050
ENTRYPOINT ["dotnet", "TicketSales.Client.dll"]
