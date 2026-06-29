using Microsoft.EntityFrameworkCore;
using studyhub.infrastructure.persistence.models;

namespace studyhub.infrastructure.persistence;

public class StudyHubDbContext(DbContextOptions<StudyHubDbContext> options) : DbContext(options)
{
    public DbSet<CourseRecord> Courses => Set<CourseRecord>();
    public DbSet<ModuleRecord> Modules => Set<ModuleRecord>();
    public DbSet<TopicRecord> Topics => Set<TopicRecord>();
    public DbSet<LessonRecord> Lessons => Set<LessonRecord>();
    public DbSet<CourseImportSnapshotRecord> CourseImportSnapshots => Set<CourseImportSnapshotRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CourseRecord>(entity =>
        {
            entity.ToTable("courses");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.RawTitle)
                .HasColumnName("raw_title")
                .IsRequired();
            entity.Property(record => record.RawDescription)
                .HasColumnName("raw_description")
                .IsRequired();
            entity.Property(record => record.Title).IsRequired();
            entity.Property(record => record.Category).IsRequired();
            entity.Property(record => record.SourceType)
                .HasColumnName("source_type")
                .HasConversion<int>();
            entity.Property(record => record.LifecycleStatus)
                .HasColumnName("lifecycle_status")
                .HasConversion<int>();
            entity.Property(record => record.SourceMetadataJson)
                .HasColumnName("source_metadata_json")
                .IsRequired();
            entity.Property(record => record.TotalDurationMinutes).IsRequired();
            entity.HasMany(record => record.Modules)
                .WithOne(record => record.Course)
                .HasForeignKey(record => record.CourseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ModuleRecord>(entity =>
        {
            entity.ToTable("modules");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.RawTitle)
                .HasColumnName("raw_title")
                .IsRequired();
            entity.Property(record => record.RawDescription)
                .HasColumnName("raw_description")
                .IsRequired();
            entity.Property(record => record.Title).IsRequired();
            entity.Property(record => record.Description)
                .HasColumnName("description")
                .IsRequired();
            entity.HasMany(record => record.Topics)
                .WithOne(record => record.Module)
                .HasForeignKey(record => record.ModuleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TopicRecord>(entity =>
        {
            entity.ToTable("topics");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.RawTitle)
                .HasColumnName("raw_title")
                .IsRequired();
            entity.Property(record => record.RawDescription)
                .HasColumnName("raw_description")
                .IsRequired();
            entity.Property(record => record.Title).IsRequired();
            entity.Property(record => record.Description)
                .HasColumnName("description")
                .IsRequired();
            entity.HasMany(record => record.Lessons)
                .WithOne(record => record.Topic)
                .HasForeignKey(record => record.TopicId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LessonRecord>(entity =>
        {
            entity.ToTable("lessons");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.RawTitle)
                .HasColumnName("raw_title")
                .IsRequired();
            entity.Property(record => record.RawDescription)
                .HasColumnName("raw_description")
                .IsRequired();
            entity.Property(record => record.Title).IsRequired();
            entity.Property(record => record.Description)
                .HasColumnName("description")
                .IsRequired();
            entity.Property(record => record.Status).HasConversion<int>();
            entity.Property(record => record.SourceType)
                .HasColumnName("source_type")
                .HasConversion<int>();
            entity.Property(record => record.LocalFilePath)
                .HasColumnName("local_file_path");
            entity.Property(record => record.Provider)
                .HasColumnName("provider");
            entity.Property(record => record.LastPlaybackPositionSeconds)
                .HasColumnName("last_playback_position_seconds");
        });

        modelBuilder.Entity<CourseImportSnapshotRecord>(entity =>
        {
            entity.ToTable("course_import_snapshots");
            entity.HasKey(record => record.CourseId);
            entity.Property(record => record.CourseId).HasColumnName("course_id");
            entity.Property(record => record.SourceKind).HasColumnName("source_kind").IsRequired();
            entity.Property(record => record.RootFolderPath).HasColumnName("root_folder_path").IsRequired();
            entity.Property(record => record.StructureJson).HasColumnName("structure_json").IsRequired();
            entity.Property(record => record.ImportedAt).HasColumnName("imported_at");
            entity.HasOne(record => record.Course)
                .WithOne()
                .HasForeignKey<CourseImportSnapshotRecord>(record => record.CourseId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
