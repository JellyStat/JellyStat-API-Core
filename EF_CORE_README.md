# Entity Framework Core Setup

This project uses Entity Framework Core with PostgreSQL for data persistence.

## Prerequisites

- PostgreSQL 12 or higher installed and running
- .NET 9.0 SDK

## Database Configuration

The connection string is configured in `appsettings.json` and `appsettings.Development.json`:

```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Port=5432;Database=jellystat;Username=postgres;Password=postgres"
}
```

**Important:** Update the connection string with your actual PostgreSQL credentials before running migrations.

## EF Core Tools

The project uses EF Core tools version 9.0.0 (local tool). The tool is configured in `dotnet-tools.json`.

## Running Migrations

### Create a new migration

```bash
dotnet dotnet-ef migrations add <MigrationName>
```

### Apply migrations to the database

```bash
dotnet dotnet-ef database update
```

### Revert to a specific migration

```bash
dotnet dotnet-ef database update <MigrationName>
```

### Remove the last migration (if not applied)

```bash
dotnet dotnet-ef migrations remove
```

### List all migrations

```bash
dotnet dotnet-ef migrations list
```

## Database Context

The `ApplicationDbContext` is located in `Data/ApplicationDbContext.cs` and includes:

- **Servers** - DbSet for managing server configurations

## Code-First Approach

To add new entities:

1. Create your entity class in the appropriate `Models` folder
2. Add a DbSet property to `ApplicationDbContext`
3. Configure the entity in the `OnModelCreating` method (optional)
4. Create a migration: `dotnet dotnet-ef migrations add Add<EntityName>`
5. Update the database: `dotnet dotnet-ef database update`

## Example Usage

```csharp
public class MyController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public MyController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> GetServers()
    {
        var servers = await _context.Servers.ToListAsync();
        return Ok(servers);
    }
}
```

## Connection String Security

For production environments:

1. Use User Secrets for local development:
   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "your-connection-string"
   ```

2. Use environment variables or Azure Key Vault for production

3. Never commit sensitive credentials to source control
