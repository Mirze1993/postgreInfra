using Npgsql;

namespace PgControl.Api.Services;

public class RestoreJobService
{
    private readonly string _metadataConn;
    private readonly PgBackRestService _pgBackRest;
    private readonly ILogger<RestoreJobService> _logger;

    public RestoreJobService(
        IConfiguration cfg,
        PgBackRestService pgBackRest,
        ILogger<RestoreJobService> logger)
    {
        _metadataConn = cfg.GetConnectionString("Metadata")!;
        _pgBackRest = pgBackRest;
        _logger = logger;
    }

    public async Task<int> CreateJobAsync(DateTime targetTime, string targetAction, string requestedBy)
    {
        await using var conn = new NpgsqlConnection(_metadataConn);
        await conn.OpenAsync();

        const string sql = @"
            INSERT INTO restore_jobs (target_time, target_action, requested_by, status)
            VALUES (@t, @a, @by, 'pending')
            RETURNING id;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("t", targetTime);
        cmd.Parameters.AddWithValue("a", targetAction);
        cmd.Parameters.AddWithValue("by", requestedBy);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task StartJobAsync(int jobId)
    {
        // Status: pending → running
        await using var conn = new NpgsqlConnection(_metadataConn);
        await conn.OpenAsync();

        var job = await GetJobAsync(conn, jobId);
        if (job == null || job.Status != "pending")
            throw new InvalidOperationException("Job hazır deyil");

        const string sql = @"
            UPDATE restore_jobs
            SET status = 'running', confirmed_at = now(), started_at = now()
            WHERE id = @id;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", jobId);
        await cmd.ExecuteNonQueryAsync();

        // Arxa planda icra
        _ = Task.Run(() => RunJobAsync(jobId, job.TargetTime, job.TargetAction));
    }

    private async Task RunJobAsync(int jobId, DateTime targetTime, string targetAction)
    {
        var logLines = new List<string>();
        var progress = new Progress<string>(line =>
        {
            lock (logLines) logLines.Add($"[{DateTime.UtcNow:HH:mm:ss}] {line}");
            _logger.LogInformation("Restore #{Id}: {Line}", jobId, line);
        });

        try
        {
            var result = await _pgBackRest.RestoreAsync(targetTime, targetAction, progress);

            await using var conn = new NpgsqlConnection(_metadataConn);
            await conn.OpenAsync();

            var finalStatus = result.Success ? "completed" : "failed";
            const string sql = @"
                UPDATE restore_jobs
                SET status = @s, finished_at = now(), log = @log, error = @err
                WHERE id = @id;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("s", finalStatus);
            cmd.Parameters.AddWithValue("log", string.Join("\n", logLines));
            cmd.Parameters.AddWithValue("err", (object?)result.Error ?? DBNull.Value);
            cmd.Parameters.AddWithValue("id", jobId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore job #{Id} uğursuz", jobId);
            await using var conn = new NpgsqlConnection(_metadataConn);
            await conn.OpenAsync();
            const string sql = @"UPDATE restore_jobs SET status='failed', finished_at=now(), error=@e WHERE id=@id;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("e", ex.Message);
            cmd.Parameters.AddWithValue("id", jobId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task<RestoreJob?> GetJobAsync(int id)
    {
        await using var conn = new NpgsqlConnection(_metadataConn);
        await conn.OpenAsync();
        return await GetJobAsync(conn, id);
    }

    private async Task<RestoreJob?> GetJobAsync(NpgsqlConnection conn, int id)
    {
        const string sql = @"
            SELECT id, backup_label, target_time, target_action, status,
                   requested_by, requested_at, confirmed_at, started_at, finished_at,
                   log, error
            FROM restore_jobs WHERE id = @id;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new RestoreJob
        {
            Id = reader.GetInt32(0),
            BackupLabel = reader.IsDBNull(1) ? null : reader.GetString(1),
            TargetTime = reader.GetDateTime(2),
            TargetAction = reader.GetString(3),
            Status = reader.GetString(4),
            RequestedBy = reader.GetString(5),
            RequestedAt = reader.GetDateTime(6),
            ConfirmedAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
            StartedAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
            FinishedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
            Log = reader.IsDBNull(10) ? null : reader.GetString(10),
            Error = reader.IsDBNull(11) ? null : reader.GetString(11)
        };
    }

    public async Task<List<RestoreJob>> ListJobsAsync()
    {
        await using var conn = new NpgsqlConnection(_metadataConn);
        await conn.OpenAsync();
        const string sql = @"
            SELECT id, backup_label, target_time, target_action, status,
                   requested_by, requested_at, confirmed_at, started_at, finished_at,
                   log, error
            FROM restore_jobs ORDER BY requested_at DESC LIMIT 50;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        var list = new List<RestoreJob>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new RestoreJob
            {
                Id = reader.GetInt32(0),
                BackupLabel = reader.IsDBNull(1) ? null : reader.GetString(1),
                TargetTime = reader.GetDateTime(2),
                TargetAction = reader.GetString(3),
                Status = reader.GetString(4),
                RequestedBy = reader.GetString(5),
                RequestedAt = reader.GetDateTime(6),
                ConfirmedAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                StartedAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                FinishedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                Log = reader.IsDBNull(10) ? null : reader.GetString(10),
                Error = reader.IsDBNull(11) ? null : reader.GetString(11)
            });
        }
        return list;
    }
}

public class RestoreJob
{
    public int Id { get; set; }
    public string? BackupLabel { get; set; }
    public DateTime TargetTime { get; set; }
    public string TargetAction { get; set; } = "promote";
    public string Status { get; set; } = "pending";
    public string RequestedBy { get; set; } = "";
    public DateTime RequestedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Log { get; set; }
    public string? Error { get; set; }
}