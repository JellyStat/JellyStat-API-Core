using Jellystat_API_Core.Data;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Models.API.Base;
using Jellystat_API_Core.Models.Jellystat.V1;
using Jellystat_API_Core.Services.TaskLogging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Jellystat_API_Core.Classes.Tasks
{
    public class RestoreTask : BaseTask
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private const int ImportBatchSize = 500;
        private static readonly JsonSerializerOptions JsonRestoreOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip,
        };

        private static readonly Dictionary<string, string> JsonTableNameMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["jf_playback_activity"] = "activity",
            ["jf_playback_reporting_plugin_data"] = "jf_playback_reporting_plugin_data"
        };

        public RestoreTask(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }

        public async Task RunRestoreAsync(string filePath, CancellationToken cancellationToken)
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var loggingService = scope.ServiceProvider.GetRequiredService<ITaskLogging>();
                Logs log = await loggingService.CreateLogAsync(TaskTypes.Restore);

                var sourceContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await using var transaction = await sourceContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {

                    await loggingService.AddMessageAsync(log, $"Starting restore task");
                    await loggingService.AddMessageAsync(log, $"File: {Path.GetFileName(filePath)}");
                    string extension = Path.GetExtension(filePath).ToLowerInvariant();



                    if (extension.Equals(".bac"))
                    {
                        var backupOptions = new DbContextOptionsBuilder<BackupExportSqliteContext>()
                                .UseSqlite($"Data Source={filePath}")
                                .Options;
                        await using var backupContext = new BackupExportSqliteContext(backupOptions);
                        await backupContext.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=DELETE;");

                        await ImportInBatchesAsync(
                            backupContext.LocalUsers
                                .AsNoTracking()
                                .OrderBy(u => u.Id)
                                .AsAsyncEnumerable(),
                            sourceContext.LocalUsers,
                            sourceContext,
                            user => new LocalUsers
                            {
                                Id = user.Id,
                                Username = user.Username,
                                Password = user.Password,
                                ServerId = user.ServerId,
                                ImageTag = user.ImageTag,
                            },
                            cancellationToken);

                        await loggingService.AddMessageAsync(log, $"Local Users Table Restored Successfully");

                        await ImportInBatchesAsync(
                         backupContext.Permissions
                              .AsNoTracking()
                            .OrderBy(p => p.Id)
                            .AsAsyncEnumerable(),
                         sourceContext.Permissions,
                         sourceContext,
                         permission => new Permissions
                         {
                             Id = permission.Id,
                             PermissionKey = permission.PermissionKey,
                             Description = permission.Description,
                             Active = permission.Active,
                         },
                         cancellationToken);

                        await loggingService.AddMessageAsync(log, $"Permissions Table Restored Successfully");

                        await ImportInBatchesAsync(
                         backupContext.UserPermissions
                            .AsNoTracking()
                            .OrderBy(up => up.PermissionId)
                            .ThenBy(up => up.UserId)
                            .AsAsyncEnumerable(),
                         sourceContext.UserPermissions,
                         sourceContext,
                         userPermission => new UserPermissions
                         {
                             PermissionId = userPermission.PermissionId,
                             UserId = userPermission.UserId,
                         },
                         cancellationToken);

                        await loggingService.AddMessageAsync(log, $"User Permissions Table Restored Successfully");

                        // Backups no longer contain API keys. Keep the key of any server that already exists here
                        // so the upsert doesn't blank it; older backups that still include a key restore it as before.
                        Dictionary<string, string> existingApiKeys = await sourceContext.Servers
                            .AsNoTracking()
                            .ToDictionaryAsync(s => s.Id, s => s.APIKey, cancellationToken);

                        await ImportInBatchesAsync(
                        backupContext.Servers
                            .AsNoTracking()
                            .OrderBy(s => s.Id)
                            .AsAsyncEnumerable(),
                        sourceContext.Servers,
                        sourceContext,
                        server => new Server
                        {
                            Id = server.Id,
                            URL = server.URL,
                            ExternalURL = server.ExternalURL,
                            APIKey = string.IsNullOrEmpty(server.APIKey)
                                ? existingApiKeys.GetValueOrDefault(server.Id, string.Empty)
                                : server.APIKey,
                            Name = server.Name,
                            Type = (ServerTypes)server.Type,
                            AllowRemoteAuth = server.AllowRemoteAuth,
                            TaskSettings = Deserialize<List<Models.Jellystat.TaskSettings>>(server.TaskSettings),
                        },
                        cancellationToken);

                        await loggingService.AddMessageAsync(log, $"Servers Table Restored Successfully");

                        List<string> serversMissingKeys = await sourceContext.Servers
                            .Where(s => s.APIKey == "")
                            .Select(s => s.Name)
                            .ToListAsync(cancellationToken);
                        if (serversMissingKeys.Count > 0)
                        {
                            await loggingService.AddMessageAsync(log, $"Backups don't include API keys. Re-enter the API key in Settings > Servers for: {string.Join(", ", serversMissingKeys)}");
                        }

                        await ImportInBatchesAsync(
                        backupContext.Users
                            .AsNoTracking()
                            .OrderBy(u => u.Id)
                            .AsAsyncEnumerable(),
                        sourceContext.Users,
                        sourceContext,
                        user => new Users
                        {
                            Id = user.Id,
                            Username = user.Username,
                            ServerId = user.ServerId,
                            ImageTag = user.ImageTag,
                        },
                        cancellationToken);

                        await loggingService.AddMessageAsync(log, $"Users Table Restored Successfully");

                        await ImportInBatchesAsync(
                        backupContext.ExcludedUsers
                            .AsNoTracking()
                            .OrderBy(e => e.ServerId)
                            .ThenBy(e => e.UserId)
                            .AsAsyncEnumerable(),
                        sourceContext.ExcludedUsers,
                        sourceContext,
                        excludedUser => new ExcludedUsers
                        {
                            ServerId = excludedUser.ServerId,
                            UserId = excludedUser.UserId,
                        }, cancellationToken);

                        await loggingService.AddMessageAsync(log, $"Excluded Users Table Restored Successfully");

                        await ImportInBatchesAsync(
                        backupContext.Activity
                            .AsNoTracking()
                            .OrderBy(a => a.Id)
                            .AsAsyncEnumerable(),
                        sourceContext.Activity,
                        sourceContext,
                        activity => new Activity
                        {
                            Id = activity.Id,
                            ServerId = activity.ServerId,
                            Name = activity.Name,
                            SeriesName = activity.SeriesName,
                            UserId = activity.UserId,
                            UserName = activity.UserName,
                            Client = activity.Client,
                            Device = activity.Device,
                            DeviceId = activity.DeviceId,
                            ItemId = activity.ItemId,
                            SeriesId = activity.SeriesId,
                            SeasonId = activity.SeasonId,
                            Duration = activity.Duration,
                            DirectPlay = activity.DirectPlay,
                            PlayMethod = activity.PlayMethod,
                            MediaStreams = Deserialize<List<BaseMediaStream>>(activity.MediaStreams),
                            TranscodingInfo = activity.TranscodingInfo is null ? null : Deserialize<BaseTranscodingInfo>(activity.TranscodingInfo),
                            Imported = activity.Imported,
                            DateCreated = activity.DateCreated,
                            Container = activity.Container,
                            IpAddress = activity.IpAddress,
                            RuntimeTicks = activity.RuntimeTicks,
                        }, cancellationToken);

                        await loggingService.AddMessageAsync(log, $"Activity Table Restored Successfully");

                        await ImportInBatchesAsync(
                        backupContext.Libraries
                            .AsAsyncEnumerable(),
                        sourceContext.Libraries,
                        sourceContext,
                        library => new Libraries
                        {
                            Id = library.Id,
                            ServerId = library.ServerId,
                            Name = library.Name,
                            Type = (LibraryTypes)library.Type,
                            ImageTag = library.ImageTag,
                            ImageHash = library.ImageHash,
                            archived = library.archived,
                        },
                        cancellationToken);

                        await loggingService.AddMessageAsync(log, $"Libraries Table Restored Successfully");

                        await ImportInBatchesAsync(
                        backupContext.ExcludedLibraries
                            .AsNoTracking()
                            .OrderBy(e => e.ServerId)
                            .ThenBy(e => e.LibraryId)
                            .AsAsyncEnumerable(),
                        sourceContext.ExcludedLibraries,
                        sourceContext,
                        excludedLibrary => new ExcludedLibraries
                        {
                            ServerId = excludedLibrary.ServerId,
                            LibraryId = excludedLibrary.LibraryId,
                        }, cancellationToken);

                        await loggingService.AddMessageAsync(log, $"Excluded Libraries Table Restored Successfully");

                        await ImportInBatchesAsync(
                        backupContext.Logs
                         .AsNoTracking()
                         .OrderBy(e => e.ServerId)
                         .AsAsyncEnumerable(),
                        sourceContext.Logs,
                        sourceContext,
                        logEntry => new Logs
                        {
                            Id = logEntry.Id,
                            ServerId = logEntry.ServerId,
                            Task = logEntry.Task,
                            DateCreated = logEntry.DateCreated,
                            Duration = logEntry.Duration,
                            Messages = logEntry.Messages,
                            Success = logEntry.Success,
                        }, cancellationToken);

                        await loggingService.AddMessageAsync(log, $"Logs Table Restored Successfully");


                        await transaction.CommitAsync(cancellationToken);

                        await RegisterNewServers(sourceContext);

                        await loggingService.AddMessageAsync(log, $"Restore Task Complete");
                        await loggingService.SuccessAsync(log);

                        var dbConnection = backupContext.Database.GetDbConnection();

                        await backupContext.DisposeAsync();

                        if (dbConnection is SqliteConnection sqliteConnection)
                        {
                            await sqliteConnection.CloseAsync();
                            SqliteConnection.ClearPool(sqliteConnection);
                        }




                    }
                    else if (extension.Equals(".json"))
                    {
                        await RestoreJsonBackupAsync(filePath, sourceContext, loggingService, log, cancellationToken);

                        await transaction.CommitAsync(cancellationToken);

                        await RegisterNewServers(sourceContext);

                        await loggingService.AddMessageAsync(log, $"Restore Task Complete");
                        await loggingService.SuccessAsync(log);
                    }
                    else if (extension.Equals(".jsonl"))
                    {
                        await RestoreJsonLinesBackupAsync(filePath, sourceContext, loggingService, log, cancellationToken);

                        await transaction.CommitAsync(cancellationToken);

                        await RegisterNewServers(sourceContext);

                        await loggingService.AddMessageAsync(log, $"Restore Task Complete");
                        await loggingService.SuccessAsync(log);
                    }
                    else
                    {
                        await loggingService.AddMessageAsync(log, $"Unsupported file extension: {extension}");
                        await loggingService.ErrorAsync(log);
                    }


                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    await loggingService.AddMessageAsync(log, $"Restore task has been cancelled, rolling back changes");
                    await loggingService.ErrorAsync(log);
                    throw;
                }
                catch (Exception ex)
                {
                    // Handle exceptions (e.g., log them)
                    await transaction.RollbackAsync(cancellationToken);
                    Console.WriteLine($"Error during restore, rolling back changes: {ex.Message}");
                    await loggingService.AddMessageAsync(log, $"Error during restore, rolling back changes: {ex.Message}");
                    await loggingService.ErrorAsync(log);
                }
                finally
                {
                    IsRunning = false;
                }
            }

        }

        private async Task RegisterNewServers(ApplicationDbContext sourceContext)
        {
            List<Server> dbServers = await sourceContext.Servers.ToListAsync();

            List<Models.Jellystat.Server> registeredServers = Global.APIInstanceManager.GetAllInstances().Select(instance => instance.config).ToList();

            List<Server> serversToRegister = dbServers.Where(dbServer => !registeredServers.Any(registeredServer => registeredServer.Id == dbServer.Id)).ToList();

            foreach (var server in serversToRegister)
            {
                Models.Jellystat.Server serverConfig = new Models.Jellystat.Server
                {
                    Id = server.Id,
                    URL = server.URL,
                    ExternalURL = server.ExternalURL,
                    APIKey = server.APIKey,
                    Name = server.Name,
                    Type = server.Type,
                    AllowRemoteAuth = server.AllowRemoteAuth,
                    TaskSettings = server.TaskSettings
                };
                await Global.APIInstanceManager.RegisterInstanceAsync(serverConfig);
            }
        }


        private async Task RestoreJsonBackupAsync(
          string filePath,
          ApplicationDbContext sourceContext,
          ITaskLogging loggingService,
          Logs log,
          CancellationToken cancellationToken)
        {
            await loggingService.AddMessageAsync(log, "Starting JSON restore");

            await using FileStream fileStream = File.OpenRead(filePath);
            using JsonDocument document = await JsonDocument.ParseAsync(fileStream, cancellationToken: cancellationToken);
            var elements = document.RootElement.EnumerateArray().ToArray();
            var backup = JsonBackupFile.FromArray(elements);

            if (backup.jf_playback_activity.IsEmpty())
            {
                throw new JsonException("No activity data found in the backup file.");
            }

            List<string> serverIds = backup.jf_playback_activity.Where(a => a.ServerId != null).Select(a => a.ServerId!).Distinct().ToList();

            List<string> existingServerIds = await sourceContext.Servers.Select(s => s.Id).ToListAsync(cancellationToken);
            List<string> matchingServerIds = serverIds.Intersect(existingServerIds).ToList();

            if (matchingServerIds.IsEmpty())
            {
                throw new JsonException($"No Valid ServerId's have been found in the current backup.");
            }

            List<Activity> mappedActivity = backup.jf_playback_activity.Where(a => a.ServerId != null && matchingServerIds.Contains(a.ServerId)).Select(s => s.ConvertData()).ToList();

            await UpsertBatchAsync(mappedActivity, sourceContext.Activity, sourceContext, cancellationToken);

            await loggingService.AddMessageAsync(log, $"Restored {mappedActivity.Count} row(s) from jf_playback_activity");


        }

        private async Task RestoreJsonLinesBackupAsync(
            string filePath,
            ApplicationDbContext sourceContext,
            ITaskLogging loggingService,
            Logs log,
            CancellationToken cancellationToken)
        {
            await loggingService.AddMessageAsync(log, "Starting JSONL restore");

            // Get valid server IDs from database
            HashSet<string> validServerIds = new(
                await sourceContext.Servers.Select(s => s.Id).ToListAsync(cancellationToken),
                StringComparer.Ordinal);

            List<Activity> currentBatch = [];
            int restoredRows = 0;
            int skippedRows = 0;

            async Task FlushBatchAsync()
            {
                if (currentBatch.Count == 0)
                {
                    return;
                }

                await UpsertBatchAsync(currentBatch, sourceContext.Activity, sourceContext, cancellationToken);
                restoredRows += currentBatch.Count;
                currentBatch.Clear();
            }

            using StreamReader reader = new(filePath);
            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using JsonDocument lineDocument = JsonDocument.Parse(line);
                JsonElement record = lineDocument.RootElement;

                if (record.ValueKind != JsonValueKind.Object || !record.TryGetProperty("type", out JsonElement typeElement))
                {
                    //throw new JsonException("Invalid JSONL backup record");
                    continue;
                }

                string? type = typeElement.GetString();

                // Skip all types except "row"
                if (type is not "row")
                {
                    continue;
                }

                // Only process rows if they have a data property and the table is jf_playback_activity
                if (!record.TryGetProperty("table", out JsonElement rowTableElement)
                    || !string.Equals(rowTableElement.GetString(), "jf_playback_activity", StringComparison.OrdinalIgnoreCase)
                    || !record.TryGetProperty("data", out JsonElement dataElement)
                    || dataElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                // Deserialize the row
                jf_playback_activity? activityRecord = dataElement.Deserialize<jf_playback_activity>(JsonRestoreOptions);
                if (activityRecord is null)
                {
                    //throw new JsonException("Failed to deserialize activity row");
                    skippedRows++;
                    continue;
                }

                // Skip rows with invalid server IDs
                if (activityRecord.ServerId is null || !validServerIds.Contains(activityRecord.ServerId))
                {
                    skippedRows++;
                    continue;
                }

                currentBatch.Add(activityRecord.ConvertData());

                if (currentBatch.Count >= ImportBatchSize)
                {
                    await FlushBatchAsync();
                }
            }

            await FlushBatchAsync();
            await loggingService.AddMessageAsync(log, $"Restored {restoredRows} row(s) from jf_playback_activity");

            if (skippedRows > 0)
            {
                await loggingService.AddMessageAsync(log, $"{skippedRows} rows not imported as they do not match any configured servers");
            }
        }
        private static async Task ImportInBatchesAsync<TSource, TDestination>(
            IAsyncEnumerable<TSource> source,
            DbSet<TDestination> destinationSet,
            ApplicationDbContext destinationContext,
            Func<TSource, TDestination> map,
            CancellationToken cancellationToken)
            where TDestination : class
        {
            List<TDestination> batch = [];

            await foreach (var item in source.WithCancellation(cancellationToken))
            {
                batch.Add(map(item));

                if (batch.Count < ImportBatchSize)
                {
                    continue;
                }

                await UpsertBatchAsync(batch, destinationSet, destinationContext, cancellationToken);
                batch.Clear();
            }

            if (batch.Count == 0)
            {
                return;
            }

            await UpsertBatchAsync(batch, destinationSet, destinationContext, cancellationToken);
        }

        private static async Task UpsertBatchAsync<TDestination>(
            IEnumerable<TDestination> batch,
            DbSet<TDestination> destinationSet,
            ApplicationDbContext destinationContext,
            CancellationToken cancellationToken)
            where TDestination : class
        {
            foreach (var entity in batch)
            {
                TDestination? existingEntity = await FindExistingEntityAsync(destinationSet, destinationContext, entity, cancellationToken);

                if (existingEntity is null)
                {
                    destinationSet.Add(entity);
                    continue;
                }

                destinationContext.Entry(existingEntity).CurrentValues.SetValues(entity);
            }

            await destinationContext.SaveChangesAsync(cancellationToken);
            destinationContext.ChangeTracker.Clear();
        }

        private static async Task<TDestination?> FindExistingEntityAsync<TDestination>(
            DbSet<TDestination> destinationSet,
            ApplicationDbContext destinationContext,
            TDestination entity,
            CancellationToken cancellationToken)
            where TDestination : class
        {
            var entityType = destinationContext.Model.FindEntityType(typeof(TDestination))
                ?? throw new InvalidOperationException($"Unable to determine entity metadata for {typeof(TDestination).Name}.");

            var primaryKey = entityType.FindPrimaryKey()
                ?? throw new InvalidOperationException($"Entity type {typeof(TDestination).Name} does not have a primary key.");

            object?[] keyValues = primaryKey.Properties
                .Select(property => destinationContext.Entry(entity).Property(property.Name).CurrentValue)
                .ToArray();

            return await destinationSet.FindAsync(keyValues, cancellationToken);
        }

        private static T Deserialize<T>(string json)
        {
            T? value = JsonSerializer.Deserialize<T>(json);
            return value ?? throw new JsonException($"Failed to deserialize {typeof(T).Name} from backup payload.");
        }
    }
}
