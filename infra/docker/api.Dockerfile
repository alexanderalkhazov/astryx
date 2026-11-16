# syntax=docker/dockerfile:1
# Build context: repository root (see infra/docker/docker-compose.yml)
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /repo
COPY global.json Directory.Build.props Directory.Packages.props Astryx.slnx ./
COPY apps/api/ apps/api/
COPY src/ src/
# FakeMarketDataProvider embeds these fixtures (src/Modules/MarketData).
COPY tests/fixtures/ tests/fixtures/
RUN dotnet restore apps/api/Astryx.Api.csproj --locked-mode
RUN dotnet publish apps/api/Astryx.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8080
COPY --from=build /out ./
USER $APP_UID
ENTRYPOINT ["dotnet", "Astryx.Api.dll"]
