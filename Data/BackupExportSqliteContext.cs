using Jellystat_API_Core.Data.BackupsSchema;
using Microsoft.EntityFrameworkCore;

namespace Jellystat_API_Core.Data
{
    internal sealed class BackupExportSqliteContext : DbContext
    {
        public BackupExportSqliteContext(DbContextOptions<BackupExportSqliteContext> options)
            : base(options)
        {
        }

        public DbSet<BackupActivity> Activity => Set<BackupActivity>();
        public DbSet<BackupUsers> Users => Set<BackupUsers>();
        public DbSet<BackupPermissions> Permissions => Set<BackupPermissions>();
        public DbSet<BackupUserPermissions> UserPermissions => Set<BackupUserPermissions>();
        public DbSet<BackupLocalUsers> LocalUsers => Set<BackupLocalUsers>();
        public DbSet<BackupServer> Servers => Set<BackupServer>();
        public DbSet<BackupExcludedLibraries> ExcludedLibraries => Set<BackupExcludedLibraries>();
        public DbSet<BackupExcludedUsers> ExcludedUsers => Set<BackupExcludedUsers>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<BackupActivity>(entity =>
            {
                entity.ToTable("Activity");
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<BackupUsers>(entity =>
            {
                entity.ToTable("Users");
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<BackupPermissions>(entity =>
            {
                entity.ToTable("Permissions");
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<BackupUserPermissions>(entity =>
            {
                entity.ToTable("UserPermissions");
                entity.HasKey(e => new { e.PermissionId, e.UserId });
            });

            modelBuilder.Entity<BackupLocalUsers>(entity =>
            {
                entity.ToTable("LocalUsers");
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<BackupServer>(entity =>
            {
                entity.ToTable("Servers");
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<BackupExcludedLibraries>(entity =>
            {
                entity.ToTable("ExcludedLibraries");
                entity.HasKey(e => new { e.ServerId, e.LibraryId });
            });

            modelBuilder.Entity<BackupExcludedUsers>(entity =>
            {
                entity.ToTable("ExcludedUsers");
                entity.HasKey(e => new { e.ServerId, e.UserId });
            });
        }
    }

}
