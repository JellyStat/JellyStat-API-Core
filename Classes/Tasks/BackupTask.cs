using Jellystat_API_Core.Data;
using Jellystat_API_Core.Data.BackupsSchema;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Services.TaskLogging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Jellystat_API_Core.Classes.Tasks
{
    public class BackupTask : BaseTask
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private const int ExportBatchSize = 500;
        private const int MaxRepeatedIdsReported = 10;

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

                    // Sensitive data logging only adds key values to tracking errors, so a failed export names the row it
                    // tripped on. The backup context holds nothing that isn't already written to the backup file.
                    var backupOptions = new DbContextOptionsBuilder<BackupExportSqliteContext>()
                        .UseSqlite(BackupConnectionString(backupFilePath))
                        .EnableSensitiveDataLogging()
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
                            // Media server API keys are admin credentials: never write them to backup files.
                            // Restoring onto an existing server keeps its stored key; new servers need it re-entered.
                            APIKey = string.Empty,
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

                    // Sorted on an expression so the database reads the table itself: a damaged primary key index can
                    // hide rows from an index scan, and can let the table hold two live copies of a row.
                    List<string> repeatedLogIds = [];

                    await ExportInBatchesAsync(
                     WithoutRepeatedLogs(
                         sourceContext.Logs
                             .AsNoTracking()
                             .OrderBy(e => e.Id + "")
                             .AsAsyncEnumerable(),
                         repeatedLogIds,
                         cancellationToken),
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

                    if (repeatedLogIds.Count > 0)
                    {
                        await loggingService.AddMessageAsync(log, $"Skipped {repeatedLogIds.Count} repeated log row(s), which points to a damaged table or index: {string.Join(", ", repeatedLogIds.Take(MaxRepeatedIdsReported))}");
                    }

                    await loggingService.AddMessageAsync(log, $"Logs Table Exported Successfully");

                    await loggingService.AddMessageAsync(log, $"Backup Task Complete");
                    var dbConnection = backupContext.Database.GetDbConnection();

                    await backupContext.DisposeAsync();

                    if (dbConnection is SqliteConnection sqliteConnection)
                    {
                        await sqliteConnection.CloseAsync();
                        SqliteConnection.ClearPool(sqliteConnection);
                    }

                    //get all backups and only keep the 5 most recent ones
                    var backupFiles = Directory.GetFiles(Global.BackupDirectory, "jellystat-backup-*.bac")
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(f => f.CreationTimeUtc)
                        .ToList();


                    var backupsToDelete = backupFiles.Skip(5).ToList();

                    foreach (var backupToDelete in backupsToDelete)
                    {
                        try
                        {
                            File.Delete(backupToDelete.FullName);
                            await loggingService.AddMessageAsync(log, $"Deleted old backup: {backupToDelete.FullName}");
                        }
                        catch (Exception ex)
                        {
                            await loggingService.AddMessageAsync(log, $"Failed to delete old backup: {backupToDelete.FullName}. Error: {ex.Message}");
                        }
                    }


                    await loggingService.SuccessAsync(log);



                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await loggingService.AddMessageAsync(log, $"Backup task has been cancelled");
                    await RemoveFailedBackupAsync(backupFilePath, loggingService, log);
                    await loggingService.ErrorAsync(log);
                    throw;
                }
                catch (Exception ex)
                {
                    // Report before cleaning up, so nothing that goes wrong below can hide why the backup failed
                    // EF's update errors only say "see the inner exception"; the database's reason is the useful part
                    string reason = ex.GetBaseException().Message;
                    Console.WriteLine($"Error during backup: {reason}");
                    await loggingService.AddMessageAsync(log, $"Error during backup: {reason}");
                    await RemoveFailedBackupAsync(backupFilePath, loggingService, log);
                    await loggingService.ErrorAsync(log);
                }
                finally
                {
                    IsRunning = false;
                }
            }
        }

        /// <summary>
        /// Passes on one copy of each log, the one with the most messages, from rows sorted by id. One bad row in the
        /// database shouldn't cost every backup, so repeats are collected for the task log instead of failing the export.
        /// </summary>
        private static async IAsyncEnumerable<Logs> WithoutRepeatedLogs(
            IAsyncEnumerable<Logs> logsSortedById,
            List<string> repeatedIds,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Logs? kept = null;

            await foreach (var row in logsSortedById.WithCancellation(cancellationToken))
            {
                if (kept == null)
                {
                    kept = row;
                }
                else if (row.Id == kept.Id)
                {
                    repeatedIds.Add(row.Id);
                    if ((row.Messages?.Count ?? 0) > (kept.Messages?.Count ?? 0))
                    {
                        kept = row;
                    }
                }
                else
                {
                    yield return kept;
                    kept = row;
                }
            }

            if (kept != null)
            {
                yield return kept;
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

        private static string BackupConnectionString(string backupFilePath) => $"Data Source={backupFilePath}";

        /// <summary>Removes a partial backup. Failing to is reported, and never stops the task's log being completed.</summary>
        private static async Task RemoveFailedBackupAsync(string? backupFilePath, ITaskLogging loggingService, Logs log)
        {
            if (string.IsNullOrWhiteSpace(backupFilePath) || !File.Exists(backupFilePath))
            {
                return;
            }

            try
            {
                // SQLite's pool keeps the file open after the context is disposed, and Windows won't delete an open file
                using var connection = new SqliteConnection(BackupConnectionString(backupFilePath));
                SqliteConnection.ClearPool(connection);
                File.Delete(backupFilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await loggingService.AddMessageAsync(log, $"Could not delete the incomplete backup {backupFilePath}: {ex.Message}");
            }
        }

        private static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value);
        }
    }
}
