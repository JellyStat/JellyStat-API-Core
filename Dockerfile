# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Jellystat-API-Core.csproj", "./"]
RUN dotnet restore "./Jellystat-API-Core.csproj"
COPY . .
RUN dotnet build "./Jellystat-API-Core.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Jellystat-API-Core.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app

# --- CORRECTED PERMISSIONS FIX START ---
# 1. Switch to root BEFORE copying files
USER root

# 2. Copy the published app and immediately assign ownership to the .NET non-root user
COPY --chown=app:app --from=publish /app/publish .

# 3. Create wwwroot and recursively grant the non-root user full control over the whole /app folder
RUN mkdir -p /app/wwwroot && chown app:app /app/wwwroot

# 4. Switch back to the secure, restricted user
USER $APP_UID
# --- CORRECTED PERMISSIONS FIX END ---

ENTRYPOINT ["dotnet", "Jellystat-API-Core.dll"]