# syntax=docker/dockerfile:1
# Two images from one build: `--target api` (the API with the compiled Angular app in wwwroot) and `--target worker`.

FROM node:24-alpine AS web
# The lock file is written by the npm version pinned in package.json ("packageManager"); older npm reads its optional platform packages differently.
RUN npm install -g npm@11.19.1 --no-audit --no-fund
WORKDIR /web
COPY src/Comments.Web/package.json src/Comments.Web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/Comments.Web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Comments.Domain/Comments.Domain.csproj src/Comments.Domain/
COPY src/Comments.Application/Comments.Application.csproj src/Comments.Application/
COPY src/Comments.Infrastructure/Comments.Infrastructure.csproj src/Comments.Infrastructure/
COPY src/Comments.ServiceDefaults/Comments.ServiceDefaults.csproj src/Comments.ServiceDefaults/
COPY src/Comments.Api/Comments.Api.csproj src/Comments.Api/
COPY src/Comments.Worker/Comments.Worker.csproj src/Comments.Worker/
RUN dotnet restore src/Comments.Api/Comments.Api.csproj && dotnet restore src/Comments.Worker/Comments.Worker.csproj
COPY src/Comments.Domain/ src/Comments.Domain/
COPY src/Comments.Application/ src/Comments.Application/
COPY src/Comments.Infrastructure/ src/Comments.Infrastructure/
COPY src/Comments.ServiceDefaults/ src/Comments.ServiceDefaults/
COPY src/Comments.Api/ src/Comments.Api/
COPY src/Comments.Worker/ src/Comments.Worker/
RUN dotnet publish src/Comments.Api/Comments.Api.csproj -c Release -o /out/api --no-restore \
 && dotnet publish src/Comments.Worker/Comments.Worker.csproj -c Release -o /out/worker --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS worker
WORKDIR /app
COPY --from=build /out/worker ./
USER $APP_UID
ENTRYPOINT ["dotnet", "Comments.Worker.dll"]

# SkiaSharp draws the CAPTCHA with an installed font and needs fontconfig to find it.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
RUN apt-get update \
 && apt-get install -y --no-install-recommends fontconfig fonts-dejavu-core curl \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out/api ./
COPY --from=web /web/dist ./wwwroot
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=60s --retries=5 CMD curl -fs http://localhost:8080/alive || exit 1
ENTRYPOINT ["dotnet", "Comments.Api.dll"]
