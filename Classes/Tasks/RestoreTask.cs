using Jellystat_API_Core.Data;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.API.Base;
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
                            APIKey = server.APIKey,
                            Name = server.Name,
                            Type = (ServerTypes)server.Type,
                            AllowRemoteAuth = server.AllowRemoteAuth,
                            TaskSettings = Deserialize<List<Models.Jellystat.TaskSettings>>(server.TaskSettings),
                        },
                        cancellationToken);

                        await loggingService.AddMessageAsync(log, $"Servers Table Restored Successfully");

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
