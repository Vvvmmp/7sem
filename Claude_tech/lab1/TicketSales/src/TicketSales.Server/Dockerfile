FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/TicketSales.Shared/TicketSales.Shared.csproj src/TicketSales.Shared/
COPY src/TicketSales.Server/TicketSales.Server.csproj src/TicketSales.Server/
RUN dotnet restore src/TicketSales.Server/TicketSales.Server.csproj

COPY src/TicketSales.Shared/ src/TicketSales.Shared/
COPY src/TicketSales.Server/ src/TicketSales.Server/
RUN dotnet publish src/TicketSales.Server/TicketSales.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .

ENV APP_MODE=Docker \
    TICKETSALES_PORT=5050
EXPOSE 5050
ENTRYPOINT ["dotnet", "TicketSales.Server.dll"]
