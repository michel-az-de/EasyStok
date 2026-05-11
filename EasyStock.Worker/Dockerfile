FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
WORKDIR /app
USER app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["EasyStock.Worker/EasyStock.Worker.csproj", "EasyStock.Worker/"]
COPY ["EasyStock.Application/EasyStock.Application.csproj", "EasyStock.Application/"]
COPY ["EasyStock.Domain/EasyStock.Domain.csproj", "EasyStock.Domain/"]
COPY ["EasyStock.Infra.Postgre/EasyStock.Infra.Postgre.csproj", "EasyStock.Infra.Postgre/"]
COPY ["EasyStock.Infra.Notifications/EasyStock.Infra.Notifications.csproj", "EasyStock.Infra.Notifications/"]
COPY ["EasyStock.Infra.Async/EasyStock.Infra.Async.csproj", "EasyStock.Infra.Async/"]
RUN dotnet restore "EasyStock.Worker/EasyStock.Worker.csproj"

COPY . .
WORKDIR "/src/EasyStock.Worker"
RUN dotnet build "EasyStock.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "EasyStock.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV DOTNET_ENVIRONMENT=Production \
    DOTNET_PRINT_TELEMETRY_MESSAGE=false \
    DOTNET_RUNNING_IN_CONTAINER=true
ENTRYPOINT ["dotnet", "EasyStock.Worker.dll"]
