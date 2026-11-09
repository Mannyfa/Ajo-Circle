# AjoCircle API. Build from the repository root:
#   docker build -f deploy/api.Dockerfile -t ajocircle-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore first so the package layer is cached between code changes.
COPY AjoCircle.slnx ./
COPY src/AjoCircle.Domain/AjoCircle.Domain.csproj src/AjoCircle.Domain/
COPY src/AjoCircle.Application/AjoCircle.Application.csproj src/AjoCircle.Application/
COPY src/AjoCircle.Infrastructure/AjoCircle.Infrastructure.csproj src/AjoCircle.Infrastructure/
COPY src/AjoCircle.Api/AjoCircle.Api.csproj src/AjoCircle.Api/
RUN dotnet restore src/AjoCircle.Api/AjoCircle.Api.csproj
COPY src/ src/
RUN dotnet publish src/AjoCircle.Api/AjoCircle.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# tzdata: the app works in Lagos time (Africa/Lagos) for due dates and reminders.
RUN apt-get update && apt-get install -y --no-install-recommends tzdata curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
# Runs as the image's built-in non-root user.
USER $APP_UID
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD curl -fsS http://localhost:8080/api/v1/health/ready || exit 1
ENTRYPOINT ["dotnet", "AjoCircle.Api.dll"]
