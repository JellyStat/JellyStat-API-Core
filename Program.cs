using Jellystat_API_Core;
using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Repositories;
using Jellystat_API_Core.Services.API;
using Jellystat_API_Core.Services.WebSocket;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Build connection string from environment variables with defaults
string? dbHost = builder.Configuration["DB_HOST"];
string dbPort = builder.Configuration["DB_PORT"] ?? "5432";
string dbName = builder.Configuration["DB_NAME"] ?? "jellystat";
string? dbUsername = builder.Configuration["DB_USERNAME"];
string? dbPassword = builder.Configuration["DB_PASSWORD"];
double CompletionThreshold = builder.Configuration.GetValue<double?>("COMPLETION_THRESHOLD_PERCENTAGE") ?? Global.CompletionThreshold;
int NewWatchThresholdHours = builder.Configuration.GetValue<int?>("NEW_WATCH_EVENT_THRESHOLD_HOURS") ?? Global.NewWatchThresholdHours;
int MinimumDurationThreshold = builder.Configuration.GetValue<int?>("MINIMUM_SECONDS_TO_INCLUDE_PLAYBACK") ?? Global.MinimumDurationThreshold;

var jwtKey = builder.Configuration["JWT_KEY"] ?? "A_super_secret_key_that_is_32_chars";
var jwtIssuer = "Jellystat";

if (string.IsNullOrEmpty(jwtKey))
{
    throw new InvalidOperationException("JWT Key is not defined");
}

if (jwtKey.Length < 32)
{
    throw new InvalidOperationException("JWT key must be at least 32 characters (256 bits) for HS256.");
}

if ((dbHost == null || dbUsername == null || dbPassword == null) && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("Database configuration is incomplete. Please set DB_HOST, DB_USERNAME, and DB_PASSWORD environment variables.");
}

Global.CompletionThreshold = CompletionThreshold;
Global.NewWatchThresholdHours = NewWatchThresholdHours;
Global.MinimumDurationThreshold = MinimumDurationThreshold;

var connectionString = $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUsername};Password={dbPassword}";

if (builder.Environment.IsDevelopment())
{
    connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? connectionString;
}

// Configure Npgsql to use System.Text.Json with dynamic JSON support
var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.EnableDynamicJson();
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(dataSource),
    ServiceLifetime.Singleton); // Make it singleton for global access

// Register repositories
builder.Services.AddSingleton<iDatabaseRepository, DatabaseRepository>();
builder.Services.AddSingleton<IAPIInstanceManager, APIInstanceManager>();
builder.Services.AddSingleton<IWebSocketClientService, WebSocketClientService>();
builder.Services.AddSingleton<IWebSocketServerService, WebSocketServerService>();

builder.Services.AddSingleton<IAuthorizationPolicyProvider, DefaultAccessTokenPolicyProvider>();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Enum.GetValues(typeof(PermissionTypes)).Cast<PermissionTypes>())
    {
        var permissionKey = permission.ToString();
        options.AddPolicy($"Permission:{permissionKey}", policy =>
            policy.Requirements.Add(new PermissionRequirement(permissionKey)));
    }
    options.AddPolicy("AccessTokenOnly", policy =>
    policy.RequireAssertion(context =>
        context.User.HasClaim(c => c.Type == "type" && c.Value == "access")));
    options.AddPolicy("RefreshTokenOnly", policy =>
    policy.RequireAssertion(context =>
        context.User.HasClaim(c => c.Type == "type" && c.Value == "refresh")));

});

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

var app = builder.Build();

// Initialize Global static references
// Note: Creates a new scope-based instance for the static reference
// This should be used carefully as it bypasses normal DI lifecycle
using (var scope = app.Services.CreateScope())
{
    Global.Database = scope.ServiceProvider.GetRequiredService<iDatabaseRepository>();
    Global.WebSocketClientService = scope.ServiceProvider.GetRequiredService<IWebSocketClientService>();
    Global.APIInstanceManager = scope.ServiceProvider.GetRequiredService<IAPIInstanceManager>();
    Global.WebSocketServerService = scope.ServiceProvider.GetRequiredService<IWebSocketServerService>();
}



// Apply migrations automatically in development (optional)
// Comment out if you prefer to run migrations manually
//if (app.Environment.IsDevelopment())
//{
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var apiManager = scope.ServiceProvider.GetRequiredService<IAPIInstanceManager>();

    try
    {
        logger.LogInformation("Applying database migrations...");
        await context.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully.");

        logger.LogInformation("Fetching Configured Servers");
        List<Jellystat_API_Core.Models.Jellystat.Server> serverConfigs = (await context.Servers.ToListAsync()).Select(s => new Jellystat_API_Core.Models.Jellystat.Server() { Id = s.Id, Name = s.Name, URL = s.URL, APIKey = s.APIKey }).ToList();
        foreach (Jellystat_API_Core.Models.Jellystat.Server config in serverConfigs)
        {
            logger.LogInformation($"Fetched: {config.Id}");
            await apiManager.RegisterInstanceAsync(config);
        }


        var enumValues = Enum.GetNames(typeof(PermissionTypes));
        var existingKeys = await context.Permissions.Select(p => p.PermissionKey).ToHashSetAsync();

        bool hasChanges = false;
        foreach (var key in enumValues)
        {
            if (!existingKeys.Contains(key))
            {
                hasChanges = true;
                context.Permissions.Add(new Jellystat_API_Core.Data.Schema.Permissions
                {
                    PermissionKey = key,
                    Description = key!.ToDisplayString(),
                    Active = true
                });
            }
        }
        if (hasChanges)
        {
            await context.SaveChangesAsync();
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while migrating the database. Make sure PostgreSQL is running.");
    }
}
//}

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
//    app.MapOpenApi();
//}

app.MapOpenApi();

app.MapGet("/", () => "Hello world!");

// Enable WebSocket support
app.UseWebSockets();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
