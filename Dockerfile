FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build

ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["NotificationService/NotificationService.csproj", "NotificationService/"]
RUN dotnet restore "NotificationService/NotificationService.csproj"

COPY . .
RUN dotnet publish "NotificationService/NotificationService.csproj" \
    --configuration "$BUILD_CONFIGURATION" \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final

WORKDIR /app

# Docker Compose probes /health/ready from inside the container. Npgsql loads
# GSSAPI dynamically even when password authentication is used.
RUN apk add --no-cache curl krb5-libs

ENV ASPNETCORE_HTTP_PORTS=1005
EXPOSE 1005

COPY --from=build /app/publish .

USER $APP_UID
ENTRYPOINT ["dotnet", "NotificationService.dll"]
