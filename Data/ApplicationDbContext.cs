using Jellystat_API_Core.Data.Converters;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Data.Schema.Views;
using Jellystat_API_Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Jellystat_API_Core.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.Properties<DateTime>()
                .HaveConversion<UtcDateTimeConverter>();
            configurationBuilder.Properties<DateTime?>()
                .HaveConversion<UtcNullableDateTimeConverter>();
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
        public DbSet<Permissions> Permissions { get; set; }
        public DbSet<UserPermissions> UserPermissions { get; set; }
        public DbSet<ItemsWithParentData> ItemsWithParentData { get; set; }
        public DbSet<ItemsPath> ItemsPath { get; set; }
        public DbSet<Logs> Logs { get; set; }
        public DbSet<GlobalTasks> GlobalTasks { get; set; }
        public DbSet<ApiKeys> ApiKeys { get; set; }
        public DbSet<Reports> Reports { get; set; }
        public DbSet<ReportSchedules> ReportSchedules { get; set; }
        public DbSet<Webhooks> Webhooks { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.HasPostgresExtension("pg_trgm");

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
                entity.HasKey(e => new { e.ServerId, e.Id }).HasName("Id");
                entity.Property(e => e.Id).HasColumnName("Id").IsRequired();
                entity.Property(e => e.ServerId).HasColumnName("ServerId").IsRequired();
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
                entity.Property(e => e.ServerId).HasColumnName("ServerId").IsRequired();
                entity.Property(e => e.LibraryId).HasColumnName("LibraryId").IsRequired();

                entity.HasOne(e => e.Server)
                    .WithMany()
                    .HasForeignKey(e => e.ServerId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Library)
                        .WithMany()
                        .HasForeignKey(e => new { e.ServerId, e.LibraryId })
                        .HasPrincipalKey(e => new { e.ServerId, e.Id })
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
                entity.Property(e => e.ServerId).HasColumnName("ServerId").IsRequired();
                entity.Property(e => e.LibraryId).HasColumnName("LibraryId").IsRequired();
                entity.HasOne(e => e.Server)
                    .WithMany()
                    .HasForeignKey(e => e.ServerId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Library)
                .WithMany()
                .HasForeignKey(e => new { e.ServerId, e.LibraryId })
                .HasPrincipalKey(e => new { e.ServerId, e.Id })
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
                entity.Property(e => e.Codec).HasDefaultValue("");
                entity.Property(e => e.Resolution).HasDefaultValue("");

            });

            modelBuilder.Entity<ItemsStaging>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).IsRequired();
                entity.Property(e => e.ServerId).HasColumnName("ServerId").IsRequired();
                entity.Property(e => e.LibraryId).HasColumnName("LibraryId").IsRequired();
                entity.HasOne(e => e.Server)
                    .WithMany()
                    .HasForeignKey(e => e.ServerId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Library)
                .WithMany()
                .HasForeignKey(e => new { e.ServerId, e.LibraryId })
                .HasPrincipalKey(e => new { e.ServerId, e.Id })
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
                entity.Property(e => e.Codec).HasDefaultValue("");
                entity.Property(e => e.Resolution).HasDefaultValue("");

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
                entity.Property(e => e.PlayMethod);

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

            modelBuilder.Entity<ItemsPath>(entity =>
            {
                entity.HasKey(e => new { e.AncestorId, e.DescendantId });
                entity.Property(e => e.AncestorId).IsRequired();
                entity.Property(e => e.DescendantId).IsRequired();
                entity.Property(e => e.Depth).IsRequired();
                entity.HasOne(e => e.Ancestor)
                    .WithMany()
                    .HasForeignKey(e => e.AncestorId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Descendant)
                        .WithMany()
                        .HasForeignKey(e => e.DescendantId)
                        .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.AncestorId)
                      .HasDatabaseName("idx_itempaths_ancestor");

                entity.HasIndex(e => e.DescendantId)
                      .HasDatabaseName("idx_itempaths_descendant");

                entity.HasIndex(e => new { e.AncestorId, e.Depth })
                      .HasDatabaseName("idx_itempaths_ancestor_depth");
            });

            modelBuilder.Entity<ItemsWithParentData>(entity =>
            {
                entity.HasBaseType((Type)null);
                entity.HasNoKey();
                entity.ToView("ItemsWithParentData");
                entity.Property(e => e.Parent).HasColumnType("jsonb");
                entity.Property(e => e.MediaStreams).HasColumnType("jsonb");
                entity.Property(e => e.Genres).HasColumnType("jsonb");
                entity.Property(e => e.ServerId).HasColumnName("ServerId");
                entity.Property(e => e.LibraryId).HasColumnName("LibraryId");

                entity.HasOne(e => e.Library)
                    .WithMany()
                    .HasForeignKey(e => new { e.ServerId, e.LibraryId })
                    .HasPrincipalKey(e => new { e.ServerId, e.Id });
            });


            modelBuilder.Entity<Activity>()
                .HasIndex(a => a.ItemId)
                .IncludeProperties(a => a.Duration)
                .HasDatabaseName("idx_activity_item_lookup");

            modelBuilder.Entity<Logs>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Task).IsRequired();
                entity.Property(e => e.DateCreated).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.Duration).HasDefaultValue(0);
                entity.Property(e => e.Messages).HasColumnType("jsonb");
                entity.Property(e => e.Success).HasDefaultValue(null);
                entity.Property(e => e.ServerId).HasDefaultValue(null);
            });

            modelBuilder.Entity<GlobalTasks>(entity =>
            {
                entity.HasKey(e => e.Task);
                entity.Property(e => e.Task).IsRequired();
                entity.Property(e => e.IntervalMinutes).IsRequired();
                entity.Property(e => e.Enabled).HasDefaultValue(true);
            });

            modelBuilder.Entity<ApiKeys>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.KeyHash).IsRequired();
                entity.HasIndex(e => e.KeyHash).IsUnique();
                entity.Property(e => e.KeyPrefix).IsRequired();
                entity.Property(e => e.Permission).HasConversion<string>().IsRequired();

                // Read-only keys are scoped to one server; removing the server removes its keys
                entity.HasOne(e => e.Server)
                      .WithMany()
                      .HasForeignKey(e => e.ServerId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Reports>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Definition).IsRequired().HasColumnType("jsonb");
            });

            modelBuilder.Entity<ReportSchedules>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Format).IsRequired().HasDefaultValue("pdf");
                entity.Property(e => e.WebhookUrl).IsRequired();
                entity.Property(e => e.WebhookType).IsRequired().HasDefaultValue("generic");
                entity.Property(e => e.Parameters).IsRequired().HasColumnType("jsonb").HasDefaultValueSql("'{}'");
                entity.Property(e => e.Enabled).HasDefaultValue(true);

                entity.HasOne(e => e.Report)
                      .WithMany()
                      .HasForeignKey(e => e.ReportId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Server)
                      .WithMany()
                      .HasForeignKey(e => e.ServerId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Webhooks>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Url).IsRequired();
                entity.Property(e => e.Type).IsRequired().HasDefaultValue("generic");
                entity.Property(e => e.Events).IsRequired();
                entity.Property(e => e.Headers).IsRequired().HasColumnType("jsonb").HasDefaultValueSql("'{}'");
                entity.Property(e => e.Enabled).HasDefaultValue(true);

                // A webhook scoped to a server goes with it; null ServerId means every server
                entity.HasOne(e => e.Server)
                      .WithMany()
                      .HasForeignKey(e => e.ServerId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<GlobalTasks>().HasData(
                new GlobalTasks { Task = TaskTypes.Backup, IntervalMinutes = (24 * 60), Enabled = true },
                new GlobalTasks { Task = TaskTypes.Restore, IntervalMinutes = 0, Enabled = true }
            );


        }
    }
}
