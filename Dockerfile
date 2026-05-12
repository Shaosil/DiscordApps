# Modified from example at https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/docker/building-net-docker-images?view=aspnetcore-10.0

# https://hub.docker.com/_/microsoft-dotnet
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy project files and restore as distinct layers
COPY ShaosilBot.Web/*.csproj ./ShaosilBot.Web/
COPY ShaosilBot.Core/*.csproj ./ShaosilBot.Core/
COPY ServerManager.Core/*.csproj ./ServerManager.Core/
RUN dotnet restore ShaosilBot.Web/ShaosilBot.Web.csproj

# Copy rest of source code and publish app
COPY ShaosilBot.Web/. ./ShaosilBot.Web/
COPY ShaosilBot.Core/. ./ShaosilBot.Core/
COPY ServerManager.Core/. ./ServerManager.Core/
WORKDIR /source/ShaosilBot.Web
ARG BUILD_CONFIG=Release
RUN dotnet publish -c $BUILD_CONFIG -o /app --no-restore

# Final stage/image, including chromium dependencies
FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends \
    libglib2.0-0 \
    libnspr4 \
    libnss3 \
    libatk1.0-0 \
    libatk-bridge2.0-0 \
    libcups2 \
    libxkbcommon0 \
    libxcomposite1 \
    libxdamage1 \
    libxrandr2 \
    libgbm1 \
    libpango-1.0-0 \
    libasound2t64 \
    libxfixes3 \
    libcairo2
RUN rm -rf /var/lib/apt/lists/*
EXPOSE 8080
WORKDIR /app
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "ShaosilBot.Web.dll"]