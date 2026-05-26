using Microsoft.EntityFrameworkCore;

namespace MCPServer.AgentRouter.Infrastructure.Persistence;

public sealed class AgentRouterSqliteDbContext : DbContext
{
    public AgentRouterSqliteDbContext(DbContextOptions<AgentRouterSqliteDbContext> options)
        : base(options)
    {
    }

    internal DbSet<AgentRunRecord> AgentRuns => Set<AgentRunRecord>();

    internal DbSet<AgentRunTraceRecord> AgentRunTraces => Set<AgentRunTraceRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<AgentRunRecord>(entity =>
        {
            entity.ToTable("agent_runs");
            entity.HasKey(static run => run.RunId);

            entity.Property(static run => run.RunId)
                .HasColumnName("run_id")
                .HasMaxLength(128)
                .IsRequired();

            entity.Property(static run => run.Objective)
                .HasColumnName("objective")
                .IsRequired();

            entity.Property(static run => run.Status)
                .HasColumnName("status")
                .HasMaxLength(64)
                .IsRequired();

            entity.Property(static run => run.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .IsRequired();

            entity.Property(static run => run.UpdatedAtUtc)
                .HasColumnName("updated_at_utc")
                .IsRequired();

            entity.Property(static run => run.Message)
                .HasColumnName("message");

            entity.Property(static run => run.Version)
                .HasColumnName("version")
                .IsRequired();

            entity.Property(static run => run.MetadataJson)
                .HasColumnName("metadata_json");

            entity.Property(static run => run.LeaseOwnerId)
                .HasColumnName("lease_owner_id")
                .HasMaxLength(128);

            entity.Property(static run => run.LeaseAcquiredAtUtc)
                .HasColumnName("lease_acquired_at_utc");

            entity.Property(static run => run.LeaseExpiresAtUtc)
                .HasColumnName("lease_expires_at_utc");

            entity.HasIndex(static run => run.LeaseExpiresAtUtc)
                .HasDatabaseName("ix_agent_runs_lease_expires_at_utc");

            entity.HasIndex(static run => run.Status)
                .HasDatabaseName("ix_agent_runs_status");

            entity.HasIndex(static run => run.UpdatedAtUtc)
                .HasDatabaseName("ix_agent_runs_updated_at_utc");
        });

        modelBuilder.Entity<AgentRunTraceRecord>(entity =>
        {
            entity.ToTable("agent_run_traces");
            entity.HasKey(static trace => trace.TraceId);

            entity.Property(static trace => trace.TraceId)
                .HasColumnName("trace_id")
                .IsRequired();

            entity.Property(static trace => trace.RunId)
                .HasColumnName("run_id")
                .HasMaxLength(128)
                .IsRequired();

            entity.Property(static trace => trace.Objective)
                .HasColumnName("objective")
                .IsRequired();

            entity.Property(static trace => trace.Status)
                .HasColumnName("status")
                .HasMaxLength(64)
                .IsRequired();

            entity.Property(static trace => trace.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .IsRequired();

            entity.Property(static trace => trace.UpdatedAtUtc)
                .HasColumnName("updated_at_utc")
                .IsRequired();

            entity.Property(static trace => trace.Message)
                .HasColumnName("message");

            entity.Property(static trace => trace.Version)
                .HasColumnName("version")
                .IsRequired();

            entity.Property(static trace => trace.MetadataJson)
                .HasColumnName("metadata_json");

            entity.Property(static trace => trace.LeaseOwnerId)
                .HasColumnName("lease_owner_id")
                .HasMaxLength(128);

            entity.Property(static trace => trace.LeaseAcquiredAtUtc)
                .HasColumnName("lease_acquired_at_utc");

            entity.Property(static trace => trace.LeaseExpiresAtUtc)
                .HasColumnName("lease_expires_at_utc");

            entity.Property(static trace => trace.WrittenAtUtc)
                .HasColumnName("written_at_utc")
                .IsRequired();

            entity.HasIndex(static trace => trace.RunId)
                .HasDatabaseName("ix_agent_run_traces_run_id");

            entity.HasIndex(static trace => trace.WrittenAtUtc)
                .HasDatabaseName("ix_agent_run_traces_written_at_utc");
        });
    }
}
