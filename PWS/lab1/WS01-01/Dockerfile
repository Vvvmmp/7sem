FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY WS01-01.csproj .
RUN RID=linux-$([ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64) && dotnet restore -r $RID
COPY . .
RUN RID=linux-$([ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64) && \
    dotnet publish WS01-01.csproj -c Release -r $RID --self-contained true --no-restore \
      -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o /app

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
WORKDIR /app
COPY --from=build /app/WS01-01 /app/WS01-01
COPY --from=build /app/appsettings*.json /app/
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["/app/WS01-01"]
