# Development User Secrets (.NET)

This project has a `UserSecretsId` in `Jellystat-API-Core.csproj`, so local development can keep its configuration in .NET User Secrets instead of environment variables. User Secrets are only loaded when `ASPNETCORE_ENVIRONMENT` is `Development`, which the `http` and `https` launch profiles and the EF Core tools set.

The full list of settings, with descriptions, is under [Environment Variables](https://github.com/JellyStat/Jellystat#environment-variables) in the main repo.

## Required

The API won't start without these:

- `DB_HOST`
- `DB_USERNAME`
- `DB_PASSWORD`

## Recommended

- `DB_NAME` (default: `jellystat`)
- `JWT_KEY`: at least 32 characters. If it isn't set, the API generates a random key on each start, so you have to sign in again after every restart. A key shorter than 32 characters stops the API from starting.

## Optional

- `DB_PORT` (default: `5432`)
- `COMPLETION_THRESHOLD_PERCENTAGE`
- `NEW_WATCH_EVENT_THRESHOLD_HOURS`
- `MINIMUM_SECONDS_TO_INCLUDE_PLAYBACK`
- `REPORTS_MAX_ROWS`: row cap for report queries (default: `10000`)
- `UI_URI`: allowed CORS origin. Ignored in development, where any origin is allowed.
- `ALLOW_UNAUTHENTICATED_PASSWORD_RESET`: recovery mode for a locked-out admin. Leave it off.
- `JS_GEOLITE_ACCOUNT_ID`, `JS_GEOLITE_LICENSE_KEY`: MaxMind GeoLite credentials for IP geolocation

## Commands

Run from the repository root.

List the current secrets:

```bash
dotnet user-secrets list --project "Jellystat-API-Core.csproj"
```

Set the database and JWT keys:

```bash
dotnet user-secrets set "DB_HOST" "localhost" --project "Jellystat-API-Core.csproj"
dotnet user-secrets set "DB_USERNAME" "<db-username>" --project "Jellystat-API-Core.csproj"
dotnet user-secrets set "DB_PASSWORD" "<db-password>" --project "Jellystat-API-Core.csproj"
dotnet user-secrets set "DB_NAME" "jellystat" --project "Jellystat-API-Core.csproj"
dotnet user-secrets set "JWT_KEY" "<your-32+char-jwt-secret>" --project "Jellystat-API-Core.csproj"
```

Optional keys use the same pattern, for example:

```bash
dotnet user-secrets set "COMPLETION_THRESHOLD_PERCENTAGE" "90" --project "Jellystat-API-Core.csproj"
```

Remove a key:

```bash
dotnet user-secrets remove "UI_URI" --project "Jellystat-API-Core.csproj"
```

Don't commit secret values to source control.
