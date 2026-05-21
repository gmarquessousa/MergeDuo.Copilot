# syntax=docker/dockerfile:1.7
# Build context MUST be the MergeDuo.Copilot repository root.

FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
WORKDIR /src

COPY src/MergeDuo.Copilot.Api/MergeDuo.Copilot.Api.csproj       src/MergeDuo.Copilot.Api/
COPY src/MergeDuo.Copilot.Domain/MergeDuo.Copilot.Domain.csproj src/MergeDuo.Copilot.Domain/
COPY src/MergeDuo.Copilot.Infra/MergeDuo.Copilot.Infra.csproj   src/MergeDuo.Copilot.Infra/

RUN dotnet restore src/MergeDuo.Copilot.Api/MergeDuo.Copilot.Api.csproj

COPY src/ src/

RUN dotnet publish src/MergeDuo.Copilot.Api/MergeDuo.Copilot.Api.csproj \
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
