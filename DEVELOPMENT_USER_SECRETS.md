# Development User Secrets (.NET)

This project is configured with a `UserSecretsId` in `Jellystat-API-Core.csproj`, so local development can use .NET User Secrets.

## Secrets currently configured locally

Current local secret keys detected for this project:
- `JWT_KEY`
- `DB_USERNAME`
- `DB_PASSWORD`
- `DB_HOST`
- `DB_NAME`

## Required secrets for development

These keys must be set for the API to start in development:
- `JWT_KEY` (must be at least 32 characters)
- `DB_HOST`
- `DB_USERNAME`
- `DB_PASSWORD`

`DB_NAME` is recommended (defaults to `jellystat` if omitted).

## Optional development keys

You can also set these as needed:
- `DB_PORT` (default: `5432`)
- `COMPLETION_THRESHOLD_PERCENTAGE`
- `NEW_WATCH_EVENT_THRESHOLD_HOURS`
- `MINIMUM_SECONDS_TO_INCLUDE_PLAYBACK`
- `UI_URI`
- `ALLOW_UNAUTHENTICATED_PASSWORD_RESET`

## Useful commands

Run from the repository root:

- List current secrets:
  - `dotnet user-secrets list --project "Jellystat-API-Core.csproj"`

- Set the currently detected development keys:
  - `dotnet user-secrets set "JWT_KEY" "<your-32+char-jwt-secret>" --project "Jellystat-API-Core.csproj"`
  - `dotnet user-secrets set "DB_USERNAME" "<db-username>" --project "Jellystat-API-Core.csproj"`
  - `dotnet user-secrets set "DB_PASSWORD" "<db-password>" --project "Jellystat-API-Core.csproj"`
  - `dotnet user-secrets set "DB_HOST" "<db-host>" --project "Jellystat-API-Core.csproj"`
  - `dotnet user-secrets set "DB_NAME" "<db-name>" --project "Jellystat-API-Core.csproj"`

- Optional keys:
  - `dotnet user-secrets set "DB_PORT" "5432" --project "Jellystat-API-Core.csproj"`
  - `dotnet user-secrets set "COMPLETION_THRESHOLD_PERCENTAGE" "90" --project "Jellystat-API-Core.csproj"`
  - `dotnet user-secrets set "NEW_WATCH_EVENT_THRESHOLD_HOURS" "12" --project "Jellystat-API-Core.csproj"`
  - `dotnet user-secrets set "MINIMUM_SECONDS_TO_INCLUDE_PLAYBACK" "30" --project "Jellystat-API-Core.csproj"`
  - `dotnet user-secrets set "UI_URI" "http://localhost:3000" --project "Jellystat-API-Core.csproj"`
  - `dotnet user-secrets set "ALLOW_UNAUTHENTICATED_PASSWORD_RESET" "false" --project "Jellystat-API-Core.csproj"`

Do not commit secret values to source control.
