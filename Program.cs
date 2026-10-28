using Jellystat_API_Core;
using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Converters;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Helpers;
using Jellystat_API_Core.Services.API;
using Jellystat_API_Core.Services.TaskLogging;
using Jellystat_API_Core.Services.Tasks;
using Jellystat_API_Core.Services.WebSocket;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NextjsStaticHosting.AspNetCore;
using Scalar.AspNetCore;
using System.Text;
using System.Text.Json;

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
string UiURI = builder.Configuration["UI_URI"] ?? "";
bool AllowUnauthenticatedPasswordReset = builder.Configuration.GetValue<bool?>("ALLOW_UNAUTHENTICATED_PASSWORD_RESET") ?? Global.AllowUnauthenticatedPasswordReset;
// Same variable names as Jellystat v1 so existing deployments keep working
string? geoLiteAccountId = builder.Configuration["JS_GEOLITE_ACCOUNT_ID"];
string? geoLiteLicenseKey = builder.Configuration["JS_GEOLITE_LICENSE_KEY"];


var jwtKey = builder.Configuration["JWT_KEY"];

if (string.IsNullOrWhiteSpace(jwtKey))
{
    // No fixed fallback: a key published in source code would let anyone sign their own admin tokens.
    // A fresh random key is used instead, so existing sessions end (everyone signs in again) on every restart.
    jwtKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
    Console.WriteLine("WARNING: JWT_KEY is not set. Using a random signing key for this run; users will need to sign in again after every restart. Set JWT_KEY (32+ characters) to keep sessions across restarts.");
}
else if (jwtKey.Length < 32)
{
    throw new InvalidOperationException("JWT key must be at least 32 characters (256 bits) for HS256.");
}

if ((dbHost == null || dbUsername == null || dbPassword == null))
{
    throw new InvalidOperationException("Database configuration is incomplete. Please set DB_HOST, DB_USERNAME, and DB_PASSWORD environment variables.");
}

Global.CompletionThreshold = CompletionThreshold;
Global.NewWatchThresholdHours = NewWatchThresholdHours;
Global.MinimumDurationThreshold = MinimumDurationThreshold;

Global.JwtKey = jwtKey;

Global.AllowUnauthenticatedPasswordReset = AllowUnauthenticatedPasswordReset;

Global.GeoLiteAccountId = geoLiteAccountId;
Global.GeoLiteLicenseKey = geoLiteLicenseKey;


var connectionString = $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUsername};Password={dbPassword}";

// Configure Npgsql to use System.Text.Json with dynamic JSON support
var jsonOptions = new JsonSerializerOptions
{
    Converters = { new PostgresDateTimeConverter() }
};

var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.ConfigureJsonOptions(jsonOptions);
dataSourceBuilder.EnableDynamicJson();
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(dataSource, npgsqlOptions =>
    {
        npgsqlOptions.CommandTimeout(120); // Set command timeout to 120 seconds
    })); // Scoped by default - each scope gets its own instance


// Register repositories

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ITaskLogging, TaskLogging>();
builder.Services.AddSingleton<ITaskSchedulerService, TaskSchedulerService>();
builder.Services.AddHostedService<TaskSchedulerHostedService>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
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
    options.DefaultAuthenticateScheme = ApiKeyDefaults.PolicyScheme;
    options.DefaultChallengeScheme = ApiKeyDefaults.PolicyScheme;
})
// Requests carrying an x-api-token header are authenticated as API keys, everything else as JWT
.AddPolicyScheme(ApiKeyDefaults.PolicyScheme, "JWT or API key", options =>
{
    options.ForwardDefaultSelector = context =>
        context.Request.Headers.ContainsKey(ApiKeyDefaults.HeaderName)
            ? ApiKeyDefaults.Scheme
            : JwtBearerDefaults.AuthenticationScheme;
})
.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.Scheme, null)
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = Global.JwtIssuer,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };

    // Allow token from query string for WebSocket connections
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var token = context.Request.Query["token"];
            var path = context.HttpContext.Request.Path;

            // If the request is for our WebSocket endpoint and has a token in query string
            if (!string.IsNullOrEmpty(token) && path.StartsWithSegments("/WebSocket"))
            {
                context.Token = token;
            }

            return Task.CompletedTask;
        }
    };
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCors", policy =>
    {
        if (builder.Environment.IsDevelopment() || string.IsNullOrEmpty(UiURI))
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins(UiURI)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });
});


//nextjs static routing


builder.Services.Configure<NextjsStaticHostingOptions>(options =>
{
    options.RootPath = "wwwroot";
});
builder.Services.AddNextjsStaticHosting();
//

var app = builder.Build();

Global.WebUIDirectory = builder.Environment.IsDevelopment()
    ? Path.Combine(builder.Environment.ContentRootPath, "wwwroot")
    : Path.Combine(AppContext.BaseDirectory, "wwwroot");

// Initialize Global static references
using (var scope = app.Services.CreateScope())
{
    Global.WebSocketClientService = scope.ServiceProvider.GetRequiredService<IWebSocketClientService>();
    Global.APIInstanceManager = scope.ServiceProvider.GetRequiredService<IAPIInstanceManager>();
    Global.WebSocketServerService = scope.ServiceProvider.GetRequiredService<IWebSocketServerService>();

}


// Apply migrations automatically
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var apiManager = scope.ServiceProvider.GetRequiredService<IAPIInstanceManager>();
    var taskSchedulerService = scope.ServiceProvider.GetRequiredService<ITaskSchedulerService>();

    try
    {
        logger.LogInformation("Applying database migrations...");
        await context.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to apply database migrations. Please ensure the database server is accessible.");
        throw;
    }

    logger.LogInformation("Fetching Configured Servers");
    List<Jellystat_API_Core.Models.Jellystat.Server> serverConfigs = (await context.Servers.ToListAsync()).Select(s => new Jellystat_API_Core.Models.Jellystat.Server() { Id = s.Id, Name = s.Name, URL = s.URL, ExternalURL = s.ExternalURL, APIKey = s.APIKey, Type = s.Type, TaskSettings = s.TaskSettings, AllowRemoteAuth = s.AllowRemoteAuth }).ToList();
    foreach (Jellystat_API_Core.Models.Jellystat.Server config in serverConfigs)
    {
        logger.LogInformation($"Fetched: {config.Id}");
        await apiManager.RegisterInstanceAsync(config);
    }

    await taskSchedulerService.RegisterGlobalTasks();


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


    await ConfigurationHelper.CheckIsConfiguredAsync(context);
    //await UpdateHelper.GetLatestUITag();

}

app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "v1");
});

app.MapScalarApiReference();

// Enable WebSocket support
app.UseWebSockets();

app.UseHttpsRedirection();

app.UseCors("DefaultCors");

app.UseAuthentication();

app.UseAuthorization();


// Server wwwroot

//app.UseDefaultFiles();
//var provider = new FileExtensionContentTypeProvider();
//app.UseStaticFiles(new StaticFileOptions
//{
//    FileProvider = new PhysicalFileProvider(
//        Path.Combine(builder.Environment.ContentRootPath, "wwwroot")),
//    ContentTypeProvider = provider,
//    ServeUnknownFileTypes = true, // Allows files without extensions generated by Next.js
//});


app.MapNextjsStaticHtmls();
app.UseNextjsStaticHosting();
///

app.MapControllers();

//app.MapFallbackToFile("index.html");

app.Run();
