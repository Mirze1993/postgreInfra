using Npgsql;

namespace PgControl.Api.Services;

public class PostgresMetricsService
{
    private readonly string _connString;
    private readonly ILogger<PostgresMetricsService> _logger;

    public PostgresMetricsService(IConfiguration cfg, ILogger<PostgresMetricsService> logger)
    {
        _logger = logger;
        // HAProxy-nin yazma portu (primary-a yönləndirir)
        _connString = cfg.GetConnectionString("HaProxyWrite")
                      ?? "Host=haproxy;Port=5000;Database=postgres;Username=postgres;Password=postgrespass";
    }

    public async Task<ReplicationMetrics> GetReplicationMetricsAsync()
    {
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        // replay_lag NULL ola bilər (replika tam sinxron, WAL aktiv deyil).
        // Buna görə həm vaxt, həm də bayt əsaslı lag qaytarırıq.
        const string sql = @"
        SELECT
            COALESCE(EXTRACT(EPOCH FROM replay_lag)::int, 0)                       AS replay_lag_seconds,
            COALESCE(pg_wal_lsn_diff(pg_current_wal_lsn(), replay_lsn)::bigint, 0) AS replay_lag_bytes,
            state,
            sync_state
        FROM pg_stat_replication
        LIMIT 1;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new ReplicationMetrics
            {
                HasReplica = true,
                ReplayLagSeconds = reader.GetInt32(0),
                ReplayLagBytes = reader.GetInt64(1),
                State = reader.GetString(2),
                SyncState = reader.GetString(3)
            };
        }

        return new ReplicationMetrics { HasReplica = false };
    }

    public async Task<ConnectionMetrics> GetConnectionMetricsAsync()
    {
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        const string sql = @"
            SELECT
                count(*) FILTER (WHERE backend_type = 'client backend') AS total,
                count(*) FILTER (WHERE state = 'active') AS active,
                count(*) FILTER (WHERE state = 'idle in transaction') AS idle_in_tx,
                (SELECT setting::int FROM pg_settings WHERE name = 'max_connections') AS max_conn
            FROM pg_stat_activity;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new ConnectionMetrics
        {
            Total = reader.GetInt64(0),
            Active = reader.GetInt64(1),
            IdleInTransaction = reader.GetInt64(2),
            MaxConnections = reader.GetInt32(3)
        };
    }

    public async Task<WalArchiveMetrics> GetWalArchiveMetricsAsync()
    {
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        const string sql = @"
            SELECT
                failed_count,
                COALESCE(EXTRACT(EPOCH FROM (now() - last_archived_time))::int, -1) AS seconds_since_last_archive
            FROM pg_stat_archiver;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new WalArchiveMetrics
        {
            FailedCount = reader.GetInt64(0),
            SecondsSinceLastArchive = reader.GetInt32(1)
        };
    }
}

// ─── DTO-lar ───

public class ReplicationMetrics
{
    public bool HasReplica { get; set; }
    public int ReplayLagSeconds { get; set; }
    public string State { get; set; } = "";
    public string SyncState { get; set; } = "";
    public long ReplayLagBytes { get; set; }
}

public class ConnectionMetrics
{
    public long Total { get; set; }
    public long Active { get; set; }
    public long IdleInTransaction { get; set; }
    public int MaxConnections { get; set; }
}

public class WalArchiveMetrics
{
    public long FailedCount { get; set; }
    public int SecondsSinceLastArchive { get; set; }
}