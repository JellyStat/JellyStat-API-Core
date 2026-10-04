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

# Ensure files copied from the publish phase belong to the app user
COPY --from=publish --chown=$APP_UID:$APP_UID /app/publish .

# Ensure the parent /app directory, the wwwroot directory, and all files have the expected ownership
USER root
RUN mkdir -p /app/wwwroot && chown -R $APP_UID:$APP_UID /app
RUN chmod 755 /app/wwwroot
USER $APP_UID

ENTRYPOINT ["dotnet", "Jellystat-API-Core.dll"]
