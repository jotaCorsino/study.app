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
    public DbSet<CourseRoadmapRecord> CourseRoadmaps => Set<CourseRoadmapRecord>();
    public DbSet<CourseMaterialsRecord> CourseMaterials => Set<CourseMaterialsRecord>();
    public DbSet<CourseGenerationStepRecord> CourseGenerationSteps => Set<CourseGenerationStepRecord>();
    public DbSet<ExternalLessonRuntimeStateRecord> ExternalLessonRuntimeStates => Set<ExternalLessonRuntimeStateRecord>();

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
            entity.Property(record => record.ExternalUrl)
                .HasColumnName("external_url");
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

        modelBuilder.Entity<CourseRoadmapRecord>(entity =>
        {
            entity.ToTable("course_roadmaps");
            entity.HasKey(record => record.CourseId);
            entity.Property(record => record.LevelsJson).IsRequired();
            entity.HasOne(record => record.Course)
                .WithOne()
                .HasForeignKey<CourseRoadmapRecord>(record => record.CourseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CourseMaterialsRecord>(entity =>
        {
            entity.ToTable("course_materials");
            entity.HasKey(record => record.CourseId);
            entity.Property(record => record.MaterialsJson).IsRequired();
            entity.HasOne(record => record.Course)
                .WithOne()
                .HasForeignKey<CourseMaterialsRecord>(record => record.CourseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CourseGenerationStepRecord>(entity =>
        {
            entity.ToTable("course_generation_runs");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.CourseId).HasColumnName("course_id");
            entity.Property(record => record.StepKey).HasColumnName("step_key").IsRequired();
            entity.Property(record => record.Provider).HasColumnName("provider").IsRequired();
            entity.Property(record => record.Status).HasColumnName("status").IsRequired();
            entity.Property(record => record.RequestJson).HasColumnName("request_json").IsRequired();
            entity.Property(record => record.ResponseJson).HasColumnName("response_json").IsRequired();
            entity.Property(record => record.ErrorMessage).HasColumnName("error_message").IsRequired();
            entity.Property(record => record.CreatedAt).HasColumnName("created_at");
            entity.Property(record => record.LastSucceededAt).HasColumnName("last_succeeded_at");
            entity.Property(record => record.LastFailedAt).HasColumnName("last_failed_at");
            entity.Property(record => record.LastErrorMessage).HasColumnName("last_error_message").IsRequired();
            entity.HasIndex(record => new { record.CourseId, record.StepKey });
        });

        modelBuilder.Entity<ExternalLessonRuntimeStateRecord>(entity =>
        {
            entity.ToTable("external_lesson_runtime_states");
            entity.HasKey(record => record.LessonId);
            entity.Property(record => record.LessonId).HasColumnName("lesson_id");
            entity.Property(record => record.CourseId).HasColumnName("course_id");
            entity.Property(record => record.Provider).HasColumnName("provider").IsRequired();
            entity.Property(record => record.Status).HasColumnName("status").IsRequired();
            entity.Property(record => record.ExternalUrl).HasColumnName("external_url").IsRequired();
            entity.Property(record => record.LastErrorCode).HasColumnName("last_error_code").IsRequired();
            entity.Property(record => record.LastErrorMessage).HasColumnName("last_error_message").IsRequired();
            entity.Property(record => record.FallbackLaunched).HasColumnName("fallback_launched");
            entity.Property(record => record.LastOpenedAt).HasColumnName("last_opened_at");
            entity.Property(record => record.LastSucceededAt).HasColumnName("last_succeeded_at");
            entity.Property(record => record.LastFailedAt).HasColumnName("last_failed_at");
            entity.Property(record => record.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(record => record.CourseId);
        });
    }
}
