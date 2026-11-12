using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Ai.Domain;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Ai.Infrastructure.Persistence;

/// <summary>Ai module data in schema <c>ai</c> (SYSTEM_DESIGN.md §10.4).</summary>
public sealed class AiDbContext(DbContextOptions<AiDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "ai";

    public override string Schema => SchemaName;

    public DbSet<AnalysisJob> Jobs => Set<AnalysisJob>();

    public DbSet<AnalysisResult> Results => Set<AnalysisResult>();

    public DbSet<AiRequest> Requests => Set<AiRequest>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnalysisJob>(entity =>
        {
            entity.ToTable("analysis_jobs", t =>
            {
                t.HasCheckConstraint("ck_analysis_jobs_kind", "kind IN ('stock_analysis','signal_explanation','news_summary')");
                t.HasCheckConstraint("ck_analysis_jobs_status", "status IN ('pending','processing','completed','failed','cancelled')");
            });
            entity.HasKey(j => j.Id);
            entity.Property(j => j.Symbol).HasMaxLength(12);
            entity.Property(j => j.Kind).HasMaxLength(32);
            entity.Property(j => j.Timeframe).HasMaxLength(8).HasDefaultValue("1d");
            entity.Property(j => j.Status).HasMaxLength(16);
            entity.Property(j => j.IdempotencyKey).HasMaxLength(200);
            entity.Property(j => j.RequestHash).HasMaxLength(64);
            entity.Property(j => j.LockedBy).HasMaxLength(200);
            entity.Property(j => j.MaxAttempts).HasDefaultValue(3);
            entity.Property(j => j.ErrorCode).HasMaxLength(64);
            entity.Property(j => j.ErrorDetail).HasMaxLength(500);
            entity.HasIndex(j => new { j.UserId, j.IdempotencyKey }).IsUnique();
            entity.HasIndex(j => new { j.UserId, j.InstrumentId, j.Kind }).IsUnique()
                .HasFilter("status IN ('pending','processing')").HasDatabaseName("ux_analysis_jobs_one_active");
            entity.HasIndex(j => new { j.Status, j.RunAfter }).HasFilter("status IN ('pending','processing')").HasDatabaseName("ix_analysis_jobs_claim");
            entity.HasIndex(j => new { j.UserId, j.CreatedAt });
        });

        modelBuilder.Entity<AnalysisResult>(entity =>
        {
            entity.ToTable("analysis_results", t =>
            {
                t.HasCheckConstraint("ck_analysis_results_validation_status", "validation_status IN ('valid','valid_with_rejections','invalid')");
                t.HasCheckConstraint("ck_analysis_results_sentiment", "sentiment IS NULL OR sentiment IN ('bullish','bearish','neutral','mixed')");
                t.HasCheckConstraint("ck_analysis_results_confidence", "confidence IS NULL OR confidence BETWEEN 0 AND 1");
            });
            entity.HasKey(r => r.Id);
            entity.HasIndex(r => r.JobId).IsUnique();
            entity.HasOne<AnalysisJob>().WithMany().HasForeignKey(r => r.JobId).OnDelete(DeleteBehavior.Cascade);
            entity.Property(r => r.Symbol).HasMaxLength(12);
            entity.Property(r => r.Provider).HasMaxLength(32);
            entity.Property(r => r.Model).HasMaxLength(100);
            entity.Property(r => r.ModelDigest).HasMaxLength(100);
            entity.Property(r => r.PromptId).HasMaxLength(64);
            entity.Property(r => r.PromptVersion).HasMaxLength(32);
            entity.Property(r => r.OutputSchemaVersion).HasMaxLength(32);
            entity.Property(r => r.Context).HasColumnType("jsonb");
            entity.Property(r => r.Parsed).HasColumnType("jsonb");
            entity.Property(r => r.ValidationStatus).HasMaxLength(32);
            entity.Property(r => r.ValidationIssues).HasColumnType("jsonb");
            entity.Property(r => r.LevelValidation).HasColumnType("jsonb");
            entity.Property(r => r.Sentiment).HasMaxLength(16);
            entity.Property(r => r.Confidence).HasPrecision(4, 3);
            entity.Property(r => r.RewardRisk).HasPrecision(8, 4);
            entity.HasIndex(r => new { r.UserId, r.InstrumentId, r.AnalysisAt }).IsDescending(false, false, true);
        });

        modelBuilder.Entity<AiRequest>(entity =>
        {
            entity.ToTable("ai_requests", t => t.HasCheckConstraint("ck_ai_requests_outcome", "outcome IN ('ok','timeout','http_error','parse_error','schema_error','cancelled')"));
            entity.HasKey(r => r.Id);
            entity.HasOne<AnalysisJob>().WithMany().HasForeignKey(r => r.JobId).OnDelete(DeleteBehavior.Cascade);
            entity.Property(r => r.Provider).HasMaxLength(32);
            entity.Property(r => r.Model).HasMaxLength(100);
            entity.Property(r => r.PromptVersion).HasMaxLength(64);
            entity.Property(r => r.Outcome).HasMaxLength(16);
            entity.Property(r => r.ErrorMessage).HasMaxLength(500);
            entity.HasIndex(r => r.StartedAt).IsDescending();
            entity.HasIndex(r => r.JobId);
        });
    }
}
