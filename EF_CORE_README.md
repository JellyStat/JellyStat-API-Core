# Entity Framework Core

The API stores its data in PostgreSQL through Entity Framework Core (Npgsql provider), code-first with migrations in `Migrations/`.

## Prerequisites

- .NET 10 SDK
- PostgreSQL, reachable from your machine
- The EF Core CLI, pinned as a local tool in `dotnet-tools.json`. Restore it once from the repository root:

  ```bash
  dotnet tool restore
  ```

> [!NOTE]
> `dotnet-tools.json` pins `dotnet-ef` 9.0.0 while the project uses EF Core 10. The commands below still work; the tool only prints a warning that it is older than the runtime.

## Database connection

There is no connection string in `appsettings.json`. `Program.cs` builds it from these configuration keys (environment variables, or User Secrets in development):

| Key | Required | Default |
|-----|----------|---------|
| `DB_HOST` | yes | |
| `DB_USERNAME` | yes | |
| `DB_PASSWORD` | yes | |
| `DB_PORT` | no | `5432` |
| `DB_NAME` | no | `jellystat` |

See [DEVELOPMENT_USER_SECRETS.md](DEVELOPMENT_USER_SECRETS.md) for setting them locally. The EF tools start the app's host to get the context, so these keys must be set for any `dotnet ef` command, even ones that don't touch the database.

## Applying migrations

The app applies pending migrations itself on startup (`context.Database.MigrateAsync()` in `Program.cs`), so a normal `dotnet run` brings the database up to date. You only need `database update` to move to a specific migration.

## Commands

There are two contexts in the project (`ApplicationDbContext` and `BackupExportSqliteContext`), so every command needs `--context ApplicationDbContext`. Run them from the repository root.

Add a migration:

```bash
dotnet dotnet-ef migrations add <MigrationName> --context ApplicationDbContext
```

Apply all migrations:

```bash
dotnet dotnet-ef database update --context ApplicationDbContext
```

Roll back to a specific migration:

```bash
dotnet dotnet-ef database update <MigrationName> --context ApplicationDbContext
```

Remove the last migration (only if it hasn't been applied):

```bash
dotnet dotnet-ef migrations remove --context ApplicationDbContext
```

List migrations:

```bash
dotnet dotnet-ef migrations list --context ApplicationDbContext
```

## Contexts

### ApplicationDbContext

`Data/ApplicationDbContext.cs` is the main context. Entity classes live in `Data/Schema/`, and it maps:

- Servers, Libraries, ExcludedLibraries, Items, ItemsStaging, ItemsPath
- Users, ExcludedUsers, LocalUsers, Permissions, UserPermissions
- Activity, Logs, GlobalTasks
- ApiKeys, Reports, ReportSchedules, Webhooks
- `ItemsWithParentData`: a keyless entity mapped to a database view (`Data/Schema/Views/`)

Every `DateTime` and `DateTime?` property is stored as UTC by a convention in `ConfigureConventions` (`Data/Converters/UtcDateTimeConverter.cs`).

### BackupExportSqliteContext

`Data/BackupExportSqliteContext.cs` describes the SQLite file written by the backup task and read by the restore task (entities in `Data/BackupsSchema/`). It has no migrations: the backup task creates the file with `EnsureCreatedAsync`. If you change a table that is backed up, update the matching `Backup*` class as well.

## Adding or changing an entity

1. Add or change the class in `Data/Schema/`.
2. For a new table, add a `DbSet` to `ApplicationDbContext`.
3. Configure keys, indexes, column types and defaults in `OnModelCreating`.
4. Add a migration: `dotnet dotnet-ef migrations add Add<EntityName> --context ApplicationDbContext`.
5. Check the generated migration, then run the app (or `database update`) to apply it.

Views and other objects EF Core can't model are created with `migrationBuilder.Sql(...)` in a migration; `20260526205827_ItemsWithParentDataView.cs` is an example. PostgreSQL won't alter or drop a column that a view uses, so a migration that does must drop the view first and recreate it afterwards.
