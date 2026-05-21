# syntax=docker/dockerfile:1.7
# Build context MUST be MergeDuo.Microservices/ because this service references MergeDuo.Aggregates.Domain.
# Example:
#   docker build -f MergeDuo.Copilot/src/MergeDuo.Copilot.Api/Dockerfile -t mergeduo-copilot:local .

FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
WORKDIR /src

COPY MergeDuo.Copilot/src/MergeDuo.Copilot.Api/MergeDuo.Copilot.Api.csproj          MergeDuo.Copilot/src/MergeDuo.Copilot.Api/
COPY MergeDuo.Copilot/src/MergeDuo.Copilot.Domain/MergeDuo.Copilot.Domain.csproj    MergeDuo.Copilot/src/MergeDuo.Copilot.Domain/
COPY MergeDuo.Copilot/src/MergeDuo.Copilot.Infra/MergeDuo.Copilot.Infra.csproj      MergeDuo.Copilot/src/MergeDuo.Copilot.Infra/
COPY MergeDuo.Aggregates/src/MergeDuo.Aggregates.Domain/MergeDuo.Aggregates.Domain.csproj MergeDuo.Aggregates/src/MergeDuo.Aggregates.Domain/

RUN dotnet restore MergeDuo.Copilot/src/MergeDuo.Copilot.Api/MergeDuo.Copilot.Api.csproj

COPY MergeDuo.Copilot/src/ MergeDuo.Copilot/src/
COPY MergeDuo.Aggregates/src/MergeDuo.Aggregates.Domain/ MergeDuo.Aggregates/src/MergeDuo.Aggregates.Domain/

RUN dotnet publish MergeDuo.Copilot/src/MergeDuo.Copilot.Api/MergeDuo.Copilot.Api.csproj \
        -c Release \
        -o /app/publish \
        --no-restore \
        /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-jammy AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

COPY --from=build /app/publish ./

USER $APP_UID

ENTRYPOINT ["dotnet", "MergeDuo.Copilot.Api.dll"]
