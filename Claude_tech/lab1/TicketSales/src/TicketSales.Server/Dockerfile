# Сборка из корня решения: docker build -f src/TicketSales.Server/Dockerfile -t ticketsales-server .
# Этап 1: сборка и публикация на .NET SDK.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Сначала только .csproj: слой restore кешируется, пока не меняются зависимости.
COPY src/TicketSales.Shared/TicketSales.Shared.csproj src/TicketSales.Shared/
COPY src/TicketSales.Server/TicketSales.Server.csproj src/TicketSales.Server/
RUN dotnet restore src/TicketSales.Server/TicketSales.Server.csproj

COPY src/TicketSales.Shared/ src/TicketSales.Shared/
COPY src/TicketSales.Server/ src/TicketSales.Server/
RUN dotnet publish src/TicketSales.Server/TicketSales.Server.csproj -c Release -o /app --no-restore

# Этап 2: запуск на облегчённом .NET Runtime (без SDK и исходников).
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
# libgssapi-krb5-2 нужна Npgsql (иначе в журнале предупреждение "Cannot load library libgssapi_krb5.so.2").
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .

ENV APP_MODE=Docker \
    TICKETSALES_PORT=5050
EXPOSE 5050
ENTRYPOINT ["dotnet", "TicketSales.Server.dll"]
