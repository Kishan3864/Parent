# Build context is backend/ (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first with only the project files so the layer is reused while sources change.
COPY Directory.Build.props ./
COPY src/ParentalTrack.Domain/ParentalTrack.Domain.csproj src/ParentalTrack.Domain/
COPY src/ParentalTrack.Infrastructure/ParentalTrack.Infrastructure.csproj src/ParentalTrack.Infrastructure/
COPY src/ParentalTrack.Api/ParentalTrack.Api.csproj src/ParentalTrack.Api/
RUN dotnet restore src/ParentalTrack.Api/ParentalTrack.Api.csproj

COPY src/ src/
RUN dotnet publish src/ParentalTrack.Api/ParentalTrack.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

COPY --from=build /app/publish ./

# APP_UID is defined by the base image (non-root, uid 1654).
USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "ParentalTrack.Api.dll"]
