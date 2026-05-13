using EFCore.BulkExtensions;
using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.API.Base;
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

        public async Task FullSync()
        {
            if (IsRunning)
            {
                Debug.WriteLine($"Sync already running for server {api.config.Name}, skipping.");
                return;
            }
            IsRunning = true;
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                Debug.WriteLine($"Starting full sync for server {api.config.Name}");
                await SyncUsers(context);
                List<Libraries> libraries = await SyncLibraries(context);
                await SyncLibraryItems(context, libraries);
                await UpdateFromStaging(context);
                Debug.WriteLine($"Full sync completed for server {api.config.Name}");

                context.ChangeTracker.Clear();
            }
            IsRunning = false;
        }

        public async Task PartialSync()
        {
            if (IsRunning)
            {
                Debug.WriteLine($"Partial sync already running for server {api.config.Name}, skipping.");
                return;
            }
            IsRunning = true;
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                Debug.WriteLine($"Starting Partial sync for server {api.config.Name}");
                await SyncUsers(context);
                List<Libraries> libraries = await SyncLibraries(context);
                await SyncLibraryItems(context, libraries, 1);

                await UpdateFromStaging(context, false);
                Debug.WriteLine($"Partial sync completed for server {api.config.Name}");

                context.ChangeTracker.Clear();
            }
            IsRunning = false;
        }

        private async Task SyncUsers(ApplicationDbContext context)
        {
            List<ExcludedUsers> excludedUsers = await context.ExcludedUsers.Where(u => u.ServerId == api.config.Id).ToListAsync();

            IList<BaseUser> users = (await api.GetUsers()).Where(u => !excludedUsers.Any(eu => eu.UserId == u.Id)).ToList();
            List<Users> existingUsers = await context.Users.Where(u => u.ServerId == api.config.Id).ToListAsync();


            List<Users> usersToRemove = existingUsers.Where(u => !users.Any(eu => eu.Id == u.Id)).ToList();
            List<BaseUser> usersToAdd = users.Where(u => !existingUsers.Any(eu => eu.Id == u.Id)).ToList();
            List<BaseUser> usersToUpdate = users.Where(u => existingUsers.Any(eu => eu.Id == u.Id)).ToList();


            foreach (BaseUser user in usersToUpdate)
            {
                Users existingUser = existingUsers.First(u => u.Id == user.Id);
                existingUser.Username = user.Name;
                existingUser.ImageTag = user.ImageTag;
            }

            await context.BulkInsertAsync(usersToAdd.Select(u => new Users
            {
                Id = u.Id,
                ServerId = api.config.Id,
                Username = u.Name,
                ImageTag = u.ImageTag
            }));

            await context.BulkDeleteAsync(usersToRemove);
            await context.SaveChangesAsync();
            Debug.WriteLine($"Synced users for server {api.config.Name}: {usersToAdd.Count} added, {usersToRemove.Count} removed, {usersToUpdate.Count} updated.");
        }

        private async Task<List<Libraries>> SyncLibraries(ApplicationDbContext context)
        {
            List<ExcludedLibraries> excludedLibraries = await context.ExcludedLibraries.Where(l => l.ServerId == api.config.Id).ToListAsync();

            List<BaseLibrary> libraries = (await api.GetLibraries()).Where(l => !excludedLibraries.Any(el => el.LibraryId == l.Id)).ToList();
            List<Libraries> existingLibraries = await context.Libraries.Where(l => l.ServerId == api.config.Id).ToListAsync();

            List<Libraries> librariesToArchive = existingLibraries.Where(l => !libraries.Any(el => el.Id == l.Id)).ToList();
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

            foreach (Libraries library in librariesToArchive)
            {
                library.archived = true;
            }

            await context.SaveChangesAsync();

            Debug.WriteLine($"Synced libraries for server {api.config.Name}: {librariesToAdd.Count} added, {librariesToArchive.Count} archived, {librariesToUpdate.Count} updated.");

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

        private async Task SyncLibraryItems(ApplicationDbContext context, List<Libraries> libraries, int limit = 0)
        {
            await context.ItemsStaging.ExecuteDeleteAsync();
            foreach (Libraries library in libraries)
            {
                int total = 0;
                IList<BaseItem> items = new List<BaseItem>();
                int index = 0;

                int limitCounter = limit;

                using (var transaction = context.Database.BeginTransaction())
                {

                    try
                    {
                        while ((items = await api.GetItemsFromParentId(library.Id, null, index)).Count > 0 && (limit > 0 ? limitCounter-- > 0 : true))
                        {
                            total += items.Count;
                            Debug.WriteLine($"Fetched {items.Count} items from library {library.Name} (total: {total})");
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
                                Index = i.Index
                            }));

                            index += items.Count;
                            items = []; // frees memory before next fetch, which is important for large libraries
                            await Task.Delay(100); // small delay to reduce load on server and database
                        }

                        // Commit if everything is successful
                        await transaction.CommitAsync();

                    }
                    catch (Exception)
                    {
                        await transaction.RollbackAsync();
                    }
                }
            }

        }
        private async Task UpdateFromStaging(ApplicationDbContext context, bool performArchiveStep = true)
        {
            using (var transaction = context.Database.BeginTransaction())
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
                        ));

                    // Step 2: Update Series by summing their child Seasons
                    // Since the previous command finished, the Season sizes are now accurate
                    await context.ItemsStaging
                        .Where(series => series.Type == ItemTypes.Series) // Series
                        .ExecuteUpdateAsync(setters => setters.SetProperty(
                            series => series.Size,
                            series => context.ItemsStaging
                                .Where(s => s.Type == ItemTypes.Season && s.ParentId == series.Id) // Seasons
                                .Sum(s => s.Size)
                        ));

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
                        .SetProperty(p => p.target.archived, false));

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
                            archived = false
                        });

                    await context.Items.AddRangeAsync(newItemsQuery);
                    int insertedCount = await context.SaveChangesAsync();

                    int archivedCount = 0;
                    if (performArchiveStep)
                    {
                        // Archive items not in staging
                        archivedCount = await context.Items
                            .Where(i => i.ServerId == serverId &&
                                        !context.ItemsStaging.Any(s => s.Id == i.Id && s.ServerId == serverId))
                            .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.archived, true));
                    }

                    // Clean up staging
                    await context.ItemsStaging
                        .Where(s => s.ServerId == serverId)
                        .ExecuteDeleteAsync();

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
                            SELECT ancestor, descendant, depth FROM tree;");

                    // Migrate Archived Activity to Matching Item
                    await context.Database.ExecuteSqlRawAsync(@"
                            
                        ");


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
                                : a.SeriesName));

                    Debug.WriteLine($"Synced items: {insertedCount} inserted, {updatedCount} updated, {archivedCount} archived (pure EF, zero materialization)");
                    Debug.WriteLine($"Updated {activityUpdatedCount} activity records with item names");

                    await transaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Debug.WriteLine($"Error during updating items from staging for server '{api.config.Name}'. Transaction rolled back. Exception: {ex.Message}");
                    throw;
                }
            }
        }




    }
}
