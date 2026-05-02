using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Data.Schema.Views;
using Microsoft.EntityFrameworkCore;

namespace Jellystat_API_Core.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Server> Servers { get; set; }
        public DbSet<Users> Users { get; set; }
        public DbSet<LocalUsers> LocalUsers { get; set; }
        public DbSet<Libraries> Libraries { get; set; }
        public DbSet<ExcludedLibraries> ExcludedLibraries { get; set; }
        public DbSet<ExcludedUsers> ExcludedUsers { get; set; }
        public DbSet<Items> Items { get; set; }
        public DbSet<ItemsStaging> ItemsStaging { get; set; }

        public DbSet<Activity> Activity { get; set; }
        public DbSet<ActivityStaging> ActivityStaging { get; set; }
        public DbSet<Permissions> Permissions { get; set; }
        public DbSet<UserPermissions> UserPermissions { get; set; }
        public DbSet<ItemsWithParentData> ItemsWithParentData { get; set; }
        public DbSet<ItemsWithStats> ItemsWithStats { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Server>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).IsRequired();
                entity.Property(e => e.URL).IsRequired();
                entity.Property(e => e.APIKey).IsRequired();
                entity.Property(e => e.Name).IsRequired();
                entity.Property(e => e.ExternalURL).HasDefaultValue("");
                entity.Property(e => e.Type).IsRequired();
                entity.Property(e => e.TaskSettings).HasColumnType("jsonb").HasDefaultValueSql("'[]'");
            });

            modelBuilder.Entity<Users>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).IsRequired();
                entity.Property(e => e.Username).IsRequired();
                entity.Property(e => e.ServerId).IsRequired();
                entity.Property(e => e.ImageTag).HasDefaultValue("");

                entity.HasOne(e => e.Server)
                      .WithMany()
                      .HasForeignKey(e => e.ServerId)
                      .OnDelete(DeleteBehavior.Cascade);
            });


            modelBuilder.Entity<LocalUsers>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).IsRequired();
                entity.Property(e => e.Username).IsRequired();
                entity.Property(e => e.Password).IsRequired();
                entity.Property(e => e.ServerId).HasDefaultValue("");
                entity.Property(e => e.ImageTag).HasDefaultValue("");
            });

            modelBuilder.Entity<Libraries>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).IsRequired();
                entity.Property(e => e.ServerId).IsRequired();
                entity.Property(e => e.Name).IsRequired();
                entity.Property(e => e.Type).IsRequired();
                entity.Property(e => e.ImageTag).HasDefaultValue("");
                entity.Property(e => e.ImageHash).HasDefaultValue("");
                entity.Property(e => e.archived).HasDefaultValue(false);

                entity.HasOne(e => e.Server)
                      .WithMany()
                      .HasForeignKey(e => e.ServerId)
                      .OnDelete(DeleteBehavior.Cascade);

            });

            modelBuilder.Entity<ExcludedLibraries>(entity =>
            {
                entity.HasKey(e => new { e.ServerId, e.LibraryId });
                entity.Property(e => e.ServerId).IsRequired();
                entity.Property(e => e.LibraryId).IsRequired();

                entity.HasOne(e => e.Server)
                    .WithMany()
                    .HasForeignKey(e => e.ServerId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Library)
                        .WithMany()
                        .HasForeignKey(e => e.LibraryId)
                        .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ExcludedUsers>(entity =>
            {
                entity.HasKey(e => new { e.ServerId, e.UserId });
                entity.Property(e => e.ServerId).IsRequired();
                entity.Property(e => e.UserId).IsRequired();
                entity.HasOne(e => e.Server)
                    .WithMany()
                    .HasForeignKey(e => e.ServerId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.User)
                        .WithMany()
                        .HasForeignKey(e => e.UserId)
                        .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Items>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).IsRequired();
                entity.Property(e => e.ServerId).IsRequired();
                entity.Property(e => e.LibraryId).IsRequired();
                entity.HasOne(e => e.Server)
                    .WithMany()
                    .HasForeignKey(e => e.ServerId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Library)
                .WithMany()
                .HasForeignKey(e => e.LibraryId)
                .OnDelete(DeleteBehavior.Cascade);

                entity.Property(e => e.Name).IsRequired();
                entity.Property(e => e.DateCreated).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.MediaStreams).HasColumnType("jsonb").HasDefaultValueSql("'[]'");
                entity.Property(e => e.Duration).HasDefaultValue(0);
                entity.Property(e => e.Type).IsRequired();
                entity.Property(e => e.ImageTag).HasDefaultValue("");
                entity.Property(e => e.ImageHash).HasDefaultValue("");
                entity.Property(e => e.Genres).HasColumnType("jsonb").HasDefaultValueSql("'[]'");
                entity.Property(e => e.Path).HasDefaultValue("");
                entity.Property(e => e.Size).HasDefaultValue(0);
                entity.Property(e => e.Bitrate).HasDefaultValue(0);
                entity.Property(e => e.ParentId).HasDefaultValue("");
                entity.Property(e => e.ParentIndex);
                entity.Property(e => e.Index);
                entity.Property(e => e.archived).HasDefaultValue(false);


            });

            modelBuilder.Entity<ItemsStaging>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).IsRequired();
                entity.Property(e => e.ServerId).IsRequired();
                entity.Property(e => e.LibraryId).IsRequired();
                entity.HasOne(e => e.Server)
                    .WithMany()
                    .HasForeignKey(e => e.ServerId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Library)
                .WithMany()
                .HasForeignKey(e => e.LibraryId)
                .OnDelete(DeleteBehavior.Cascade);

                entity.Property(e => e.Name).IsRequired();
                entity.Property(e => e.DateCreated).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.MediaStreams).HasColumnType("jsonb").HasDefaultValueSql("'[]'");
                entity.Property(e => e.Duration).HasDefaultValue(0);
                entity.Property(e => e.Type).IsRequired();
                entity.Property(e => e.ImageTag).HasDefaultValue("");
                entity.Property(e => e.ImageHash).HasDefaultValue("");
                entity.Property(e => e.Genres).HasColumnType("jsonb").HasDefaultValueSql("'[]'");
                entity.Property(e => e.Path).HasDefaultValue("");
                entity.Property(e => e.Size).HasDefaultValue(0);
                entity.Property(e => e.Bitrate).HasDefaultValue(0);
                entity.Property(e => e.ParentId).HasDefaultValue("");
                entity.Property(e => e.ParentIndex);
                entity.Property(e => e.Index);
                entity.Property(e => e.archived).HasDefaultValue(false);


            });

            modelBuilder.Entity<Activity>(entity =>
            {

                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
                entity.Property(e => e.ServerId).IsRequired();
                entity.Property(e => e.Name).IsRequired();
                entity.Property(e => e.SeriesId);
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.UserName).IsRequired();
                entity.Property(e => e.Client);
                entity.Property(e => e.Device);
                entity.Property(e => e.DeviceId);
                entity.Property(e => e.ItemId).IsRequired();
                entity.Property(e => e.SeriesId);
                entity.Property(e => e.SeasonId);
                entity.Property(e => e.Duration).HasDefaultValue(0);
                entity.Property(e => e.DirectPlay).HasDefaultValue(true);
                entity.Property(e => e.MediaStreams).HasColumnType("jsonb").HasDefaultValueSql("'[]'");
                entity.Property(e => e.TranscodingInfo).HasColumnType("jsonb");
                entity.Property(e => e.Imported).HasDefaultValue(false);
                entity.Property(e => e.DateCreated).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.Container);
                entity.Property(e => e.IpAddress).IsRequired();
                entity.Property(e => e.RuntimeTicks).HasDefaultValue(0);

            });

            modelBuilder.Entity<ActivityStaging>(entity =>
            {

                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
                entity.Property(e => e.ServerId).IsRequired();
                entity.Property(e => e.Name).IsRequired();
                entity.Property(e => e.SeriesId);
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.UserName).IsRequired();
                entity.Property(e => e.Client);
                entity.Property(e => e.Device);
                entity.Property(e => e.DeviceId);
                entity.Property(e => e.ItemId).IsRequired();
                entity.Property(e => e.SeriesId);
                entity.Property(e => e.SeasonId);
                entity.Property(e => e.Duration).HasDefaultValue(0);
                entity.Property(e => e.DirectPlay).HasDefaultValue(true);
                entity.Property(e => e.MediaStreams).HasColumnType("jsonb").HasDefaultValueSql("'[]'");
                entity.Property(e => e.TranscodingInfo).HasColumnType("jsonb");
                entity.Property(e => e.Imported).HasDefaultValue(false);
                entity.Property(e => e.DateCreated).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.LastPaused).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.Container);
                entity.Property(e => e.IpAddress).IsRequired();
                entity.Property(e => e.IsPaused).HasDefaultValue(false);
                entity.Property(e => e.RuntimeTicks).HasDefaultValue(0);

            });

            modelBuilder.Entity<Permissions>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).UseIdentityColumn();
                entity.Property(e => e.PermissionKey).IsRequired();
                entity.Property(e => e.Description).IsRequired();
                entity.Property(e => e.Active).HasDefaultValue(true);

            });


            modelBuilder.Entity<UserPermissions>(entity =>
            {
                entity.HasKey(e => new { e.PermissionId, e.UserId });
                entity.Property(e => e.PermissionId).IsRequired();
                entity.Property(e => e.UserId).IsRequired();
                entity.HasOne(e => e.Permissions)
                    .WithMany()
                    .HasForeignKey(e => e.PermissionId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.User)
                        .WithMany()
                        .HasForeignKey(e => e.UserId)
                        .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ItemsWithParentData>(entity =>
            {
                entity.HasBaseType((Type)null);
                entity.HasNoKey();
                entity.ToView("ItemsWithParentData");
                entity.Property(e => e.Parent).HasColumnType("jsonb");
            });

            modelBuilder.Entity<ItemsWithStats>(entity =>
            {
                entity.HasBaseType((Type)null);
                entity.HasNoKey();
                entity.ToView("ItemsWithStats");
                entity.Property(e => e.PlayCount);
                entity.Property(e => e.PlayDuration);
                entity.Property(e => e.LastPlayedDate);
            });

            // Matches: CREATE INDEX idx_items_parent_lookup ON "Items" ("ParentId", "Id");
            modelBuilder.Entity<Items>()
                .HasIndex(i => new { i.ParentId, i.Id })
                .HasDatabaseName("idx_items_parent_lookup");

            // 2. Covering Index for Activity Sums
            // Matches: CREATE INDEX idx_activity_item_lookup ON "Activity" ("ItemId") INCLUDE ("Duration");
            modelBuilder.Entity<Activity>()
                .HasIndex(a => a.ItemId)
                .IncludeProperties(a => a.Duration) // This is the 'INCLUDE' part
                .HasDatabaseName("idx_activity_item_lookup");

            modelBuilder.Entity<Activity>()
                .HasIndex(a => a.ItemId)
                .IncludeProperties(a => a.DateCreated) // This is the 'INCLUDE' part
                .HasDatabaseName("idx_activity_item_date");

            modelBuilder.Entity<Activity>()
                .HasIndex(a => a.SeriesId)
                .IncludeProperties(a => a.DateCreated) // This is the 'INCLUDE' part
                .HasDatabaseName("idx_activity_series_date");

            modelBuilder.Entity<Activity>()
                .HasIndex(a => a.SeasonId)
                .IncludeProperties(a => a.DateCreated) // This is the 'INCLUDE' part
                .HasDatabaseName("idx_activity_season_date");





        }
    }
}
