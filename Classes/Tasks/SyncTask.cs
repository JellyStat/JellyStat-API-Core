using EFCore.BulkExtensions;
using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Models.API.Base;
using Jellystat_API_Core.Services.TaskLogging;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace Jellystat_API_Core.Classes.Tasks
{
    public class SyncTask : BaseTask
    {

        public iAPI api { get; set; }
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public SyncTask(iAPI api, IServiceScopeFactory serviceScopeFactory)
        {
            this.api = api;
            _serviceScopeFactory = serviceScopeFactory;
        }

        public async Task FullSync(CancellationToken cancellationToken)
        {
            if (IsRunning)
            {
                Debug.WriteLine($"Sync already running for server {api.config.Name}, skipping.");
                return;
            }

            IsRunning = true;

            try
            {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var loggingService = scope.ServiceProvider.GetRequiredService<ITaskLogging>();
                    Logs log = await loggingService.CreateLogAsync(TaskTypes.FullSync, api.config.Id);

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await loggingService.AddMessageAsync(log, $"Starting full sync for server {api.config.Name}");
                        await SyncUsers(context, log, loggingService, cancellationToken);
                        List<Libraries> libraries = await SyncLibraries(context, log, loggingService, cancellationToken);
                        List<ExcludedLibraries> excludedLibraries = await context.ExcludedLibraries.Where(l => l.ServerId == api.config.Id).ToListAsync(cancellationToken);
                        await SyncLibraryItems(context, libraries.Where(l => !excludedLibraries.Any(el => el.LibraryId == l.Id)).ToList(), log, loggingService, cancellationToken);
                        await UpdateFromStaging(context, log, loggingService, true, cancellationToken);
                        await MigrateArchivedActivity(context, log, loggingService);
                        await loggingService.AddMessageAsync(log, $"Full sync completed for server {api.config.Name}");
                        await loggingService.SuccessAsync(log);

                        context.ChangeTracker.Clear();
                    }
                    catch (OperationCanceledException)
                    {
                        await loggingService.AddMessageAsync(log, $"Full sync cancelled for server {api.config.Name}");
                        await loggingService.ErrorAsync(log);
                        Debug.WriteLine($"Full sync cancelled for server {api.config.Name}");
                    }
                    catch (Exception ex)
                    {
                        await loggingService.AddMessageAsync(log, $"Error running Full Sync: {ex.Message}");
                        await loggingService.ErrorAsync(log);
                        Debug.WriteLine($"Error running Full Sync: {ex.Message}");
                    }
                }
            }
            finally
            {
                IsRunning = false;
            }
        }

        public async Task PartialSync(CancellationToken cancellationToken)
        {
            if (IsRunning)
            {
                Debug.WriteLine($"Partial sync already running for server {api.config.Name}, skipping.");
                return;
            }

            IsRunning = true;

            try
            {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var loggingService = scope.ServiceProvider.GetRequiredService<ITaskLogging>();
                    Logs log = await loggingService.CreateLogAsync(TaskTypes.PartialSync, api.config.Id);

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await loggingService.AddMessageAsync(log, $"Starting partial sync for server {api.config.Name}");
                        await SyncUsers(context, log, loggingService, cancellationToken);
                        List<Libraries> libraries = await SyncLibraries(context, log, loggingService, cancellationToken);
                        await SyncLibraryItems(context, libraries, log, loggingService, cancellationToken, 1);
                        await UpdateFromStaging(context, log, loggingService, false, cancellationToken);
                        await MigrateArchivedActivity(context, log, loggingService);
                        await loggingService.AddMessageAsync(log, $"Partial sync completed for server {api.config.Name}");
                        await loggingService.SuccessAsync(log);

                        context.ChangeTracker.Clear();
                    }
                    catch (OperationCanceledException)
                    {
                        await loggingService.AddMessageAsync(log, $"Partial sync cancelled for server {api.config.Name}");
                        await loggingService.ErrorAsync(log);
                        Debug.WriteLine($"Partial sync cancelled for server {api.config.Name}");
                    }
                    catch (Exception ex)
                    {
                        await loggingService.AddMessageAsync(log, $"Error running Partial Sync: {ex.Message}");
                        await loggingService.ErrorAsync(log);
                        Debug.WriteLine($"Error running Partial Sync: {ex.Message}");
                    }
                }
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task SyncUsers(ApplicationDbContext context, Logs log, ITaskLogging loggingService, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IList<BaseUser> users = (await api.GetUsers()).ToList();
            await loggingService.AddMessageAsync(log, $"Fetching Users");
            List<Users> existingUsers = await context.Users.Where(u => u.ServerId == api.config.Id).ToListAsync(cancellationToken);

            List<Users> usersToRemove = existingUsers.Where(u => !users.Any(eu => eu.Id == u.Id)).ToList();
            List<BaseUser> usersToAdd = users.Where(u => !existingUsers.Any(eu => eu.Id == u.Id)).ToList();
            List<BaseUser> usersToUpdate = users.Where(u => existingUsers.Any(eu => eu.Id == u.Id)).ToList();

            foreach (BaseUser user in usersToUpdate)
            {
                Users existingUser = existingUsers.First(u => u.Id == user.Id);
                existingUser.Username = user.Name;
                existingUser.ImageTag = user.ImageTag;
            }

            if (usersToUpdate.Any())
            {
                await loggingService.AddMessageAsync(log, $"Updating {usersToUpdate.Count} users");
            }

            cancellationToken.ThrowIfCancellationRequested();
            await context.BulkInsertAsync(usersToAdd.Select(u => new Users
            {
                Id = u.Id,
                ServerId = api.config.Id,
                Username = u.Name,
                ImageTag = u.ImageTag
            }));

            if (usersToAdd.Any())
            {
                await loggingService.AddMessageAsync(log, $"Added {usersToAdd.Count} users");
            }

            cancellationToken.ThrowIfCancellationRequested();
            await context.BulkDeleteAsync(usersToRemove);

            if (usersToRemove.Any())
            {
                await loggingService.AddMessageAsync(log, $"Removed {usersToRemove.Count} users");
            }

            await context.SaveChangesAsync(cancellationToken);

            await loggingService.AddMessageAsync(log, $"Synced users for server {api.config.Name}");
        }

        private async Task<List<Libraries>> SyncLibraries(ApplicationDbContext context, Logs log, ITaskLogging loggingService, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await loggingService.AddMessageAsync(log, $"Fetching Libraries");
            List<BaseLibrary> libraries = (await api.GetLibraries()).ToList();

            libraries = libraries.Where(l => l.Type != LibraryTypes.BoxSets && l.Type != LibraryTypes.Unknown).ToList();

            await loggingService.AddMessageAsync(log, $"Fetched {libraries.Count} libraries");
            List<Libraries> existingLibraries = await context.Libraries.Where(l => l.ServerId == api.config.Id).ToListAsync(cancellationToken);

            List<Libraries> librariesToArchive = existingLibraries.Where(l => !l.archived && !libraries.Any(el => el.Id == l.Id)).ToList();
            List<BaseLibrary> librariesToAdd = libraries.Where(l => !existingLibraries.Any(el => el.Id == l.Id)).ToList();
            List<BaseLibrary> librariesToUpdate = libraries.Where(l => existingLibraries.Any(el => el.Id == l.Id)).ToList();

            foreach (BaseLibrary library in librariesToUpdate)
            {
                Libraries existingLibrary = existingLibraries.First(l => l.Id == library.Id);
                existingLibrary.Name = library.Name;
                existingLibrary.Type = library.Type;
                existingLibrary.ImageTag = library.ImageTag;
                existingLibrary.ImageHash = library.ImageHash;
                existingLibrary.archived = library.archived;
            }

            if (librariesToAdd.Any())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await context.BulkInsertAsync(librariesToAdd.Select(l => new Libraries
                {
                    Id = l.Id,
                    ServerId = api.config.Id,
                    Name = l.Name,
                    Type = l.Type,
                    ImageTag = l.ImageTag,
                    ImageHash = l.ImageHash,
                    archived = l.archived
                }));

                await loggingService.AddMessageAsync(log, $"Added {librariesToAdd.Count} libraries");
            }

            if (librariesToUpdate.Any())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await context.BulkUpdateAsync(librariesToUpdate.Select(l => new Libraries
                {
                    Id = l.Id,
                    ServerId = api.config.Id,
                    Name = l.Name,
                    Type = l.Type,
                    ImageTag = l.ImageTag,
                    ImageHash = l.ImageHash,
                    archived = l.archived
                }));

                await loggingService.AddMessageAsync(log, $"Updating {librariesToUpdate.Count} libraries");
            }

            foreach (Libraries library in librariesToArchive)
            {
                library.archived = true;
            }

            if (librariesToArchive.Any())
            {
                await loggingService.AddMessageAsync(log, $"Archived {librariesToArchive.Count} libraries");
            }

            await context.SaveChangesAsync(cancellationToken);

            await loggingService.AddMessageAsync(log, $"Synced libraries for server {api.config.Name}");

            return libraries.Select(l => new Libraries
            {
                Id = l.Id,
                ServerId = api.config.Id,
                Name = l.Name,
                Type = l.Type,
                ImageTag = l.ImageTag,
                ImageHash = l.ImageHash,
                archived = l.archived
            }).ToList();

        }

        private async Task SyncLibraryItems(ApplicationDbContext context, List<Libraries> libraries, Logs log, ITaskLogging loggingService, CancellationToken cancellationToken, int limit = 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await loggingService.AddMessageAsync(log, $"Starting library items sync for server {api.config.Name}");
            await context.ItemsStaging.ExecuteDeleteAsync(cancellationToken);
            foreach (Libraries library in libraries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int total = 0;
                IList<BaseItem> items = new List<BaseItem>();
                int index = 0;

                int limitCounter = limit;

                await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
                {

                    try
                    {
                        await loggingService.AddMessageAsync(log, $"Fetching items for library {library.Name} (id: {library.Id})");
                        while ((items = await api.GetItemsFromParentId(library.Id, null, index)).Count > 0 && (limit > 0 ? limitCounter-- > 0 : true))
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            total += items.Count;
                            await context.BulkInsertAsync(items.Select(i => new ItemsStaging
                            {
                                Id = i.Id,
                                ServerId = i.ServerId,
                                Name = i.Name,
                                LibraryId = library.Id,
                                DateCreated = i.DateCreated,
                                MediaStreams = i.MediaStreams,
                                Duration = i.RunTimeTicks ?? 0,
                                Type = i.Type,
                                ImageTag = i.ImageTag,
                                ImageHash = i.ImageHash,
                                Genres = i.Genres,
                                Path = i.Path,
                                Size = i.Size,
                                Bitrate = i.Bitrate,
                                ParentId = i.ParentId,
                                ParentIndex = i.ParentIndex,
                                Index = i.Index,
                                Codec = i.Codec,
                                Resolution = i.Resolution,
                            }));

                            index += items.Count;
                            items = []; // frees memory before next fetch, which is important for large libraries
                            await Task.Delay(100, cancellationToken); // small delay to reduce load on server and database
                        }

                        await loggingService.AddMessageAsync(log, $"Finished library items sync for library {library.Name} (total: {total})");

                        // Commit if everything is successful
                        await transaction.CommitAsync(cancellationToken);

                    }
                    catch (OperationCanceledException)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        throw;
                    }
                    catch (Exception)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        await loggingService.AddMessageAsync(log, $"Error during library items sync for library {library.Name}.");
                    }
                }
            }

        }
        private async Task UpdateFromStaging(ApplicationDbContext context, Logs log, ITaskLogging loggingService, bool performArchiveStep = true, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await loggingService.AddMessageAsync(log, $"Starting update from staging for server {api.config.Name}");
            await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
            {
                try
                {
                    var serverId = api.config.Id;

                    // Step 1: Update Seasons by summing their child Episodes
                    // This generates a single UPDATE ... FROM ... SQL statement
                    await context.ItemsStaging
                        .Where(s => s.Type == ItemTypes.Season) // Seasons
                        .ExecuteUpdateAsync(setters => setters.SetProperty(
                            s => s.Size,
                            s => context.ItemsStaging
                                .Where(e => e.Type == ItemTypes.Episode && e.ParentId == s.Id) // Episodes
                                .Sum(e => e.Size)
                        ), cancellationToken);

                    // Step 2: Update Series by summing their child Seasons
                    // Since the previous command finished, the Season sizes are now accurate
                    await context.ItemsStaging
                        .Where(series => series.Type == ItemTypes.Series) // Series
                        .ExecuteUpdateAsync(setters => setters.SetProperty(
                            series => series.Size,
                            series => context.ItemsStaging
                                .Where(s => s.Type == ItemTypes.Season && s.ParentId == series.Id) // Seasons
                                .Sum(s => s.Size)
                        ), cancellationToken);

                    // Update existing items from staging (pure EF, no materialization)
                    var updateQuery = from target in context.Items
                                      join source in context.ItemsStaging on target.Id equals source.Id
                                      where source.ServerId == serverId
                                      select new { target, source };

                    int updatedCount = await updateQuery.ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.target.ServerId, p => p.source.ServerId)
                        .SetProperty(p => p.target.LibraryId, p => p.source.LibraryId)
                        .SetProperty(p => p.target.Name, p => p.source.Name)
                        .SetProperty(p => p.target.Type, p => p.source.Type)
                        .SetProperty(p => p.target.ImageTag, p => p.source.ImageTag)
                        .SetProperty(p => p.target.ImageHash, p => p.source.ImageHash)
                        .SetProperty(p => p.target.Genres, p => p.source.Genres)
                        .SetProperty(p => p.target.Duration, p => p.source.Duration)
                        .SetProperty(p => p.target.Path, p => p.source.Path)
                        .SetProperty(p => p.target.Size, p => p.source.Size)
                        .SetProperty(p => p.target.Bitrate, p => p.source.Bitrate)
                        .SetProperty(p => p.target.MediaStreams, p => p.source.MediaStreams)
                        .SetProperty(p => p.target.ParentId, p => p.source.ParentId)
                        .SetProperty(p => p.target.ParentIndex, p => p.source.ParentIndex)
                        .SetProperty(p => p.target.Index, p => p.source.Index)
                        .SetProperty(p => p.target.DateCreated, p => p.source.DateCreated)
                        .SetProperty(p => p.target.archived, false)
                        .SetProperty(p => p.target.Codec, p => p.source.Codec)
                        .SetProperty(p => p.target.Resolution, p => p.source.Resolution)
                        , cancellationToken);

                    await loggingService.AddMessageAsync(log, $"Updated {updatedCount} existing items from staging for server {api.config.Name}");

                    // Insert new items using AddRangeAsync with IQueryable
                    var newItemsQuery = context.ItemsStaging
                        .AsNoTracking()
                        .Where(s => s.ServerId == serverId && !context.Items.Any(i => i.Id == s.Id))
                        .Select(s => new Items
                        {
                            Id = s.Id,
                            ServerId = s.ServerId,
                            LibraryId = s.LibraryId,
                            Name = s.Name,
                            Type = s.Type,
                            ImageTag = s.ImageTag,
                            ImageHash = s.ImageHash,
                            Genres = s.Genres,
                            Duration = s.Duration,
                            Path = s.Path,
                            Size = s.Size,
                            Bitrate = s.Bitrate,
                            MediaStreams = s.MediaStreams,
                            ParentId = s.ParentId,
                            ParentIndex = s.ParentIndex,
                            Index = s.Index,
                            DateCreated = s.DateCreated,
                            archived = false,
                            Codec = s.Codec,
                            Resolution = s.Resolution,
                        });

                    await context.Items.AddRangeAsync(newItemsQuery, cancellationToken);
                    int insertedCount = await context.SaveChangesAsync(cancellationToken);
                    await loggingService.AddMessageAsync(log, $"Inserted {insertedCount} new items from staging for server {api.config.Name}");

                    int archivedCount = 0;
                    if (performArchiveStep)
                    {
                        // Archive items not in staging
                        archivedCount = await context.Items
                            .Where(i => i.ServerId == serverId &&
                                        !context.ItemsStaging.Any(s => s.Id == i.Id && s.ServerId == serverId))
                            .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.archived, true), cancellationToken);
                    }

                    await loggingService.AddMessageAsync(log, $"Archived {archivedCount} items not present in staging for server {api.config.Name}");

                    // Clean up staging
                    await context.ItemsStaging
                        .Where(s => s.ServerId == serverId)
                        .ExecuteDeleteAsync(cancellationToken);

                    // Rebuild the ItemsPath table using a recursive CTE, which is much faster than doing it in memory
                    await context.Database.ExecuteSqlRawAsync(@"
                            DELETE FROM ""ItemsPath"";
                            INSERT INTO ""ItemsPath"" (""AncestorId"", ""DescendantId"", ""Depth"")
                            WITH RECURSIVE tree AS (
                                -- 1. Base Case: Select from Items directly
                                SELECT
                                    ""Id"" AS ancestor,
                                    ""Id"" AS descendant,
                                    0 AS depth
                                FROM ""Items""

                                UNION ALL

                                -- 2. Recursive Step: Reference the aliases defined above
                                SELECT
                                    t.ancestor,
                                    i.""Id"",
                                    t.depth + 1
                                FROM tree t
                                JOIN ""Items"" i ON i.""ParentId"" = t.descendant
                            )
                            SELECT ancestor, descendant, depth FROM tree;", cancellationToken);

                    // Update Activity table with Names from Items
                    int activityUpdatedCount = await context.Activity
                        .Where(a => a.ServerId == serverId)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(a => a.Name, a => context.Items
                                .Where(i => i.Id == a.ItemId && a.ServerId == serverId)
                                .Select(i => i.Name)
                                .FirstOrDefault() ?? a.Name)
                            .SetProperty(a => a.SeriesName, a => a.SeriesId != null
                                ? context.Items
                                    .Where(i => i.Id == a.SeriesId && a.ServerId == serverId)
                                    .Select(i => i.Name)
                                    .FirstOrDefault() ?? a.SeriesName
                                : a.SeriesName), cancellationToken);

                    await loggingService.AddMessageAsync(log, $"Updated {activityUpdatedCount} activity records with new item names for server {api.config.Name}");

                    await transaction.CommitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    Debug.WriteLine($"Error during updating items from staging for server '{api.config.Name}'. Transaction rolled back. Exception: {ex.Message}");
                    throw;
                }
            }
        }

        private async Task MigrateArchivedActivity(ApplicationDbContext context, Logs log, ITaskLogging loggingService)
        {
            try
            {
                List<Data.Schema.Activity> archivedActivity = await context.Activity
                    .Where(a => a.ServerId == api.config.Id)
                    .Join(context.Items, a => a.ItemId, i => i.Id, (a, i) => new { Activity = a, Item = i })
                    .Where(joined => joined.Item.archived)
                    .Select(joined => joined.Activity)
                    .ToListAsync();

                if (archivedActivity.IsEmpty())
                {
                    Debug.WriteLine($"No archived activity found for server '{api.config.Name}'.");
                    await loggingService.AddMessageAsync(log, $"No archived activity found for server '{api.config.Name}'.");
                    return;
                }

                // Separate into movie and episode activity
                var movieNames = archivedActivity.Where(a => a.SeriesId == null).Select(a => a.Name).ToHashSet();
                var episodeData = archivedActivity.Where(a => a.SeriesId != null)
                    .GroupBy(a => new { a.SeriesName, a.Name })
                    .ToDictionary(g => g.Key, g => g.ToList());

                var seriesNames = episodeData.Keys.Select(k => k.SeriesName).ToHashSet();

                // Fetch all matching items in one optimized query
                var movieItems = await context.Items
                    .Where(i => i.ServerId == api.config.Id && !i.archived && movieNames.Contains(i.Name))
                    .ToDictionaryAsync(i => i.Name, i => i);

                // For episodes: get all series and their descendants in one query
                var episodeItems = await (
                    from series in context.Items
                    where series.ServerId == api.config.Id
                          && !series.archived
                          && seriesNames.Contains(series.Name)
                    join seriesPath in context.ItemsPath on series.Id equals seriesPath.AncestorId
                    join episodePath in context.ItemsPath on seriesPath.DescendantId equals episodePath.AncestorId
                    join episode in context.Items on episodePath.DescendantId equals episode.Id
                    where episode.ServerId == api.config.Id && !episode.archived && episode.ParentId != null
                    select new
                    {
                        SeriesName = series.Name,
                        SeriesId = series.Id,
                        EpisodeName = episode.Name,
                        EpisodeId = episode.Id,
                        SeasonId = episode.ParentId
                    })
                    .ToListAsync();

                // Create a lookup for fast matching
                var episodeLookup = episodeItems
                    .GroupBy(e => new { e.SeriesName, e.EpisodeName })
                    .ToDictionary(g => g.Key, g => g.First());

                int migratedCount = 0;

                // Migrate movie activity
                foreach (var activity in archivedActivity.Where(a => a.SeriesId == null))
                {
                    if (movieItems.TryGetValue(activity.Name, out var item))
                    {
                        activity.ItemId = item.Id;
                        activity.SeriesName = null;
                        migratedCount++;
                    }
                }

                // Migrate episode activity
                foreach (var activity in archivedActivity.Where(a => a.SeriesId != null))
                {
                    var key = new { SeriesName = activity.SeriesName, EpisodeName = activity.Name };
                    if (episodeLookup.TryGetValue(key, out var item))
                    {
                        activity.ItemId = item.EpisodeId;
                        activity.SeriesId = item.SeriesId;
                        activity.SeasonId = item.SeasonId;
                        migratedCount++;
                    }
                }

                await context.SaveChangesAsync();

                await loggingService.AddMessageAsync(log, $"Migrated {migratedCount} archived activity records for server '{api.config.Name}'");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during migrating archived activity for server '{api.config.Name}'. Exception: {ex.Message}");
                throw;
            }
        }




    }
}
