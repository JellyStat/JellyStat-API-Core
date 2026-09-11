using Jellystat_API_Core.Data;
using Jellystat_API_Core.Data.BackupsSchema;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Services.TaskLogging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text.Json;

namespace Jellystat_API_Core.Classes.Tasks
{
    public class BackupTask : BaseTask
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private const int ExportBatchSize = 500;

        public BackupTask(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }

        public async Task RunBackupAsync(CancellationToken cancellationToken)
        {

            if (IsRunning)
            {
                Debug.WriteLine($"Backup is already running, skipping.");
                return;
            }
            IsRunning = true;
            string? backupFilePath = null;

            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var loggingService = scope.ServiceProvider.GetRequiredService<ITaskLogging>();
                Logs log = await loggingService.CreateLogAsync(TaskTypes.Backup);

                try
                {

                    var sourceContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    await loggingService.AddMessageAsync(log, $"Starting backup task");


                    Directory.CreateDirectory(Global.BackupDirectory);

                    backupFilePath = Path.Combine(
                        Global.BackupDirectory,
                        $"jellystat-backup-{DateTime.UtcNow:yyyyMMddHHmmssfff}.bac");

                    await loggingService.AddMessageAsync(log, $"Creating Backup: {backupFilePath}");

                    var backupOptions = new DbContextOptionsBuilder<BackupExportSqliteContext>()
                        .UseSqlite($"Data Source={backupFilePath}")
                        .Options;

                    await using var backupContext = new BackupExportSqliteContext(backupOptions);
                    await backupContext.Database.EnsureCreatedAsync(cancellationToken);
                    await backupContext.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=DELETE;");

                    await ExportInBatchesAsync(
                        sourceContext.Activity
                            .AsNoTracking()
                            .OrderBy(a => a.Id)
                            .AsAsyncEnumerable(),
                        backupContext.Activity,
                        backupContext,
                        activity => new BackupActivity
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
                            MediaStreams = Serialize(activity.MediaStreams),
                            TranscodingInfo = activity.TranscodingInfo is null ? null : Serialize(activity.TranscodingInfo),
                            Imported = activity.Imported,
                            DateCreated = activity.DateCreated,
                            Container = activity.Container,
                            IpAddress = activity.IpAddress,
                            RuntimeTicks = activity.RuntimeTicks,
                        },
                        cancellationToken);

                    await loggingService.AddMessageAsync(log, $"Activity Table Exported Successfully");

                    await ExportInBatchesAsync(
                        sourceContext.Users
                            .AsNoTracking()
                            .OrderBy(u => u.Id)
                            .AsAsyncEnumerable(),
                        backupContext.Users,
                        backupContext,
                        user => new BackupUsers
                        {
                            Id = user.Id,
                            Username = user.Username,
                            ServerId = user.ServerId,
                            ImageTag = user.ImageTag,
                        },
                        cancellationToken);

                    await loggingService.AddMessageAsync(log, $"Users Table Exported Successfully");

                    await ExportInBatchesAsync(
                        sourceContext.Permissions
                            .AsNoTracking()
                            .OrderBy(p => p.Id)
                            .AsAsyncEnumerable(),
                        backupContext.Permissions,
                        backupContext,
                        permission => new BackupPermissions
                        {
                            Id = permission.Id,
                            PermissionKey = permission.PermissionKey,
                            Description = permission.Description,
                            Active = permission.Active,
                        },
                        cancellationToken);

                    await loggingService.AddMessageAsync(log, $"Permissions Table Exported Successfully");

                    await ExportInBatchesAsync(
                        sourceContext.UserPermissions
                            .AsNoTracking()
                            .OrderBy(up => up.PermissionId)
                            .ThenBy(up => up.UserId)
                            .AsAsyncEnumerable(),
                        backupContext.UserPermissions,
                        backupContext,
                        userPermission => new BackupUserPermissions
                        {
                            PermissionId = userPermission.PermissionId,
                            UserId = userPermission.UserId,
                        },
                        cancellationToken);

                    await loggingService.AddMessageAsync(log, $"User Permissions Table Exported Successfully");

                    await ExportInBatchesAsync(
                        sourceContext.LocalUsers
                            .AsNoTracking()
                            .OrderBy(u => u.Id)
                            .AsAsyncEnumerable(),
                        backupContext.LocalUsers,
                        backupContext,
                        user => new BackupLocalUsers
                        {
                            Id = user.Id,
                            Username = user.Username,
                            Password = user.Password,
                            ServerId = user.ServerId,
                            ImageTag = user.ImageTag,
                        },
                        cancellationToken);

                    await loggingService.AddMessageAsync(log, $"Local Users Table Exported Successfully");

                    await ExportInBatchesAsync(
                        sourceContext.Servers
                            .AsNoTracking()
                            .OrderBy(s => s.Id)
                            .AsAsyncEnumerable(),
                        backupContext.Servers,
                        backupContext,
                        server => new BackupServer
                        {
                            Id = server.Id,
                            URL = server.URL,
                            ExternalURL = server.ExternalURL,
                            APIKey = server.APIKey,
                            Name = server.Name,
                            Type = (int)server.Type,
                            AllowRemoteAuth = server.AllowRemoteAuth,
                            TaskSettings = Serialize(server.TaskSettings),
                        },
                        cancellationToken);

                    await loggingService.AddMessageAsync(log, $"Servers Table Exported Successfully");

                    await ExportInBatchesAsync(
                        sourceContext.Libraries
                            .AsNoTracking()
                            .AsAsyncEnumerable(),
                        backupContext.Libraries,
                        backupContext,
                        library => new BackupLibraries
                        {
                            Id = library.Id,
                            ServerId = library.ServerId,
                            Name = library.Name,
                            Type = (int)library.Type,
                            ImageTag = library.ImageTag,
                            ImageHash = library.ImageHash,
                            archived = library.archived,
                        },
                        cancellationToken);

                    await loggingService.AddMessageAsync(log, $"Libraries Table Exported Successfully");

                    await ExportInBatchesAsync(
                        sourceContext.ExcludedLibraries
                            .AsNoTracking()
                            .OrderBy(e => e.ServerId)
                            .ThenBy(e => e.LibraryId)
                            .AsAsyncEnumerable(),
                        backupContext.ExcludedLibraries,
                        backupContext,
                        excludedLibrary => new BackupExcludedLibraries
                        {
                            ServerId = excludedLibrary.ServerId,
                            LibraryId = excludedLibrary.LibraryId,
                        },
                        cancellationToken);

                    await loggingService.AddMessageAsync(log, $"Excluded Libraries Table Exported Successfully");

                    await ExportInBatchesAsync(
                        sourceContext.ExcludedUsers
                            .AsNoTracking()
                            .OrderBy(e => e.ServerId)
                            .ThenBy(e => e.UserId)
                            .AsAsyncEnumerable(),
                        backupContext.ExcludedUsers,
                        backupContext,
                        excludedUser => new BackupExcludedUsers
                        {
                            ServerId = excludedUser.ServerId,
                            UserId = excludedUser.UserId,
                        },
                        cancellationToken);

                    await loggingService.AddMessageAsync(log, $"Excluded Users Table Exported Successfully");

                    await ExportInBatchesAsync(
                     sourceContext.Logs
                         .AsNoTracking()
                         .OrderBy(e => e.ServerId)
                         .AsAsyncEnumerable(),
                     backupContext.Logs,
                     backupContext,
                     logEntry => new BackupLogs
                     {
                         Id = logEntry.Id,
                         ServerId = logEntry.ServerId,
                         Task = logEntry.Task,
                         DateCreated = logEntry.DateCreated,
                         Duration = logEntry.Duration,
                         Messages = logEntry.Messages,
                         Success = logEntry.Success,
                     },
                     cancellationToken);

                    await loggingService.AddMessageAsync(log, $"Logs Table Exported Successfully");

                    await loggingService.AddMessageAsync(log, $"Backup Task Complete");
                    await loggingService.SuccessAsync(log);

                    var dbConnection = backupContext.Database.GetDbConnection();

                    await backupContext.DisposeAsync();

                    if (dbConnection is SqliteConnection sqliteConnection)
                    {
                        await sqliteConnection.CloseAsync();
                        SqliteConnection.ClearPool(sqliteConnection);
                    }


                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    DeleteBackupFile(backupFilePath);
                    await loggingService.AddMessageAsync(log, $"Backup task has been cancelled");
                    await loggingService.ErrorAsync(log);
                    throw;
                }
                catch (Exception ex)
                {
                    DeleteBackupFile(backupFilePath);
                    // Handle exceptions (e.g., log them)
                    Console.WriteLine($"Error during backup: {ex.Message}");
                    await loggingService.AddMessageAsync(log, $"Error during backup: {ex.Message}");
                    await loggingService.ErrorAsync(log);
                }
                finally
                {
                    IsRunning = false;
                }
            }
        }

        private static async Task ExportInBatchesAsync<TSource, TDestination>(
            IAsyncEnumerable<TSource> source,
            DbSet<TDestination> destinationSet,
            BackupExportSqliteContext backupContext,
            Func<TSource, TDestination> map,
            CancellationToken cancellationToken)
            where TDestination : class
        {
            List<TDestination> batch = [];

            await foreach (var item in source.WithCancellation(cancellationToken))
            {
                batch.Add(map(item));

                if (batch.Count < ExportBatchSize)
                {
                    continue;
                }

                await destinationSet.AddRangeAsync(batch, cancellationToken);
                await backupContext.SaveChangesAsync(cancellationToken);
                backupContext.ChangeTracker.Clear();
                batch.Clear();
            }

            if (batch.Count == 0)
            {
                return;
            }

            await destinationSet.AddRangeAsync(batch, cancellationToken);
            await backupContext.SaveChangesAsync(cancellationToken);
            backupContext.ChangeTracker.Clear();
        }

        private static void DeleteBackupFile(string? backupFilePath)
        {
            if (!string.IsNullOrWhiteSpace(backupFilePath) && File.Exists(backupFilePath))
            {
                File.Delete(backupFilePath);
            }
        }

        private static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value);
        }
    }
}
