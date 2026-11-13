# syntax=docker/dockerfile:1.7
# aspnet (nao runtime): o Worker puxa Infra.Async/Infra.Postgre, que tem
# FrameworkReference Microsoft.AspNetCore.App (IHttpContextAccessor etc.). Com a
# base runtime:10.0 o container crasha no launch ("No frameworks were found:
# Microsoft.AspNetCore.App 10.0.0"). Mesma base da API.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
# Mesmo uid e gid do appuser da API (Dockerfile da raiz): os dois gravam no volume
# uploads-data, que pertence a ele. Com o usuario padrao da imagem (app, 1654) o worker
# nao grava o canhoto dos avisos (#2245). scripts/docker/uid-uploads.test.sh confere.
RUN groupadd --system --gid 1001 appgroup \
 && useradd  --system --uid 1001 --gid appgroup --create-home appuser \
 && mkdir -p /app/uploaded-files && chown -R appuser:appgroup /app
USER appuser

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# .csproj primeiro (cache de restore)
COPY ["EasyStock.Worker/EasyStock.Worker.csproj",                     "EasyStock.Worker/"]
COPY ["EasyStock.Application/EasyStock.Application.csproj",           "EasyStock.Application/"]
COPY ["EasyStock.Domain/EasyStock.Domain.csproj",                     "EasyStock.Domain/"]
COPY ["EasyStock.Infra.Postgre/EasyStock.Infra.Postgre.csproj",       "EasyStock.Infra.Postgre/"]
COPY ["EasyStock.Infra.Notifications/EasyStock.Infra.Notifications.csproj", "EasyStock.Infra.Notifications/"]
COPY ["EasyStock.Infra.Async/EasyStock.Infra.Async.csproj",           "EasyStock.Infra.Async/"]
COPY ["EasyStock.Observabilidade/EasyStock.Observabilidade.csproj",   "EasyStock.Observabilidade/"]
COPY ["EasyStock.Infra.Integrations/EasyStock.Infra.Integrations.csproj", "EasyStock.Infra.Integrations/"]
COPY ["EasyStock.Contracts/EasyStock.Contracts.csproj",               "EasyStock.Contracts/"]

RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget \
    dotnet restore "EasyStock.Worker/EasyStock.Worker.csproj"

# Codigo fonte
COPY EasyStock.Worker/              EasyStock.Worker/
COPY EasyStock.Application/         EasyStock.Application/
COPY EasyStock.Domain/              EasyStock.Domain/
COPY EasyStock.Infra.Postgre/       EasyStock.Infra.Postgre/
COPY EasyStock.Infra.Notifications/ EasyStock.Infra.Notifications/
COPY EasyStock.Infra.Async/         EasyStock.Infra.Async/
COPY EasyStock.Observabilidade/     EasyStock.Observabilidade/
COPY EasyStock.Infra.Integrations/  EasyStock.Infra.Integrations/
COPY EasyStock.Contracts/           EasyStock.Contracts/

# Publish direto (publish ja faz build — etapa intermediaria removida)
WORKDIR "/src/EasyStock.Worker"
RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget \
    dotnet publish "EasyStock.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "EasyStock.Worker.dll"]
