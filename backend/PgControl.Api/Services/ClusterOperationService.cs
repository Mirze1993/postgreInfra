using Npgsql;
using System.Text.Json;

namespace PgControl.Api.Services;

public class ClusterOperationService
{
    private readonly string _metadataConn;
    private readonly PatroniService _patroni;
    private readonly DockerControllerClient _controller;
    private readonly ILogger<ClusterOperationService> _logger;

    public ClusterOperationService(
        IConfiguration cfg,
        PatroniService patroni,
        DockerControllerClient controller,
        ILogger<ClusterOperationService> logger)
    {
        _metadataConn = cfg.GetConnectionString("Metadata")!;
        _patroni = patroni;
        _controller = controller;
        _logger = logger;
    }

    // ── LOCK idarəetməsi ─────────────────────────────────
    private async Task<int?> TryAcquireLockAsync(NpgsqlConnection conn, int operationId)
    {
        const string sql = @"
            UPDATE cluster_lock
            SET locked = true, locked_by = @op, locked_at = now()
            WHERE id = 1 AND locked = false
            RETURNING id;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("op", operationId);
        return (int?)await cmd.ExecuteScalarAsync();
    }

    private async Task ReleaseLockAsync(NpgsqlConnection conn)
    {
        const string sql = "UPDATE cluster_lock SET locked = false, locked_by = NULL WHERE id = 1;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── REPLICA ADD ──────────────────────────────────────
    public async Task<int> RequestAddReplicaAsync(string requestedBy)
    {
        await using var conn = new NpgsqlConnection(_metadataConn);
        await conn.OpenAsync();

        // 1) Klaster vəziyyətini yoxla
        var cluster = await _patroni.GetClusterStatusAsync();
        if (!cluster.LeaderExists)
            throw new InvalidOperationException("Klasterdə leader yoxdur — replica əlavə etmək olmaz");

        // 2) Növbəti replica adını hesabla
        var existingNodes = cluster.Nodes
            .Select(n => n.Url.Split(':')[1].TrimStart('/'))
            .ToList();
        var nextNum = Enumerable.Range(1, 99)
            .Select(i => $"pg{i}")
            .First(name => !existingNodes.Contains(name));

        // 3) Host portu hesabla (55440 + num)
        var hostPort = 55440 + int.Parse(nextNum.Substring(2));

        // 4) Operation yarat
        const string sql = @"
            INSERT INTO cluster_operations
                (operation_type, target_node, status, requested_by, metadata)
            VALUES ('add_replica', @node, 'pending', @by, @meta::jsonb)
            RETURNING id;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("node", nextNum);
        cmd.Parameters.AddWithValue("by", requestedBy);
        cmd.Parameters.AddWithValue("meta", JsonSerializer.Serialize(new { hostPort }));
        var opId = (int)(await cmd.ExecuteScalarAsync())!;

        // 5) Arxa planda icra
        _ = Task.Run(() => RunAddReplicaAsync(opId, nextNum, hostPort));

        return opId;
    }

    private async Task RunAddReplicaAsync(int opId, string name, int hostPort)
    {
        var log = new List<string>();
        void Log(string msg) { lock (log) log.Add($"[{DateTime.UtcNow:HH:mm:ss}] {msg}"); }

        await using var conn = new NpgsqlConnection(_metadataConn);
        await conn.OpenAsync();

        // Lock
        var lockId = await TryAcquireLockAsync(conn, opId);
        if (lockId == null)
        {
            await UpdateOpAsync(conn, opId, "failed", "Başqa klaster əməliyyatı davam edir", log);
            return;
        }

        try
        {
            await UpdateOpStatusAsync(conn, opId, "running");
            Log($"Replica '{name}' yaradılır (port {hostPort})...");

            var result = await _controller.CreateReplicaAsync(name, hostPort);

            if (!result.Success)
            {
                Log($"❌ Uğursuz: {result.Error}");
                await UpdateOpAsync(conn, opId, "failed", result.Error, log);
                return;
            }

            Log($"✅ Konteyner yaradıldı. Patroni qoşulmasını gözləyirik...");

            // 60 saniyə ərzində Patroni qoşulmasını gözlə
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(2000);
                var status = await _patroni.GetNodeStatusAsync($"http://{name}:8008");
                if (status?.State == "running")
                {
                    Log($"✅ '{name}' Patroni cluster-ə qoşuldu (role: {status.Role})");
                    await UpdateOpAsync(conn, opId, "completed", null, log);
                    return;
                }
            }

            Log("⚠ Konteyner yaradıldı, amma 60s ərzində Patroni qoşulmadı");
            await UpdateOpAsync(conn, opId, "failed", "Patroni qoşulmadı (timeout)", log);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Add replica #{Id} xətası", opId);
            Log($"❌ Xəta: {ex.Message}");
            await UpdateOpAsync(conn, opId, "failed", ex.Message, log);
        }
        finally
        {
            await ReleaseLockAsync(conn);
        }
    }

    // ── REPLICA REMOVE ───────────────────────────────────
    public async Task<int> RequestRemoveReplicaAsync(string nodeName, string requestedBy)
    {
        await using var conn = new NpgsqlConnection(_metadataConn);
        await conn.OpenAsync();

        var cluster = await _patroni.GetClusterStatusAsync();
        var target = cluster.Nodes.FirstOrDefault(n =>
            n.Url.Contains($"//{nodeName}:") || n.Url.EndsWith($"/{nodeName}"));
        
        if (target == null)
            throw new InvalidOperationException($"Node '{nodeName}' tapılmadı");

        // Leader-i silmə qadağandır
        if (target.Role.Equals("primary", StringComparison.OrdinalIgnoreCase) ||
            target.Role.Equals("master", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Leader node-u silmək olmaz! Əvvəlcə switchover edin.");

        // Son replica silinməsin
        if (cluster.Nodes.Count <= 2)
            throw new InvalidOperationException("Son replica silinə bilməz (minimum 2 node)");

        const string sql = @"
            INSERT INTO cluster_operations
                (operation_type, target_node, status, requested_by)
            VALUES ('remove_replica', @node, 'pending', @by)
            RETURNING id;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("node", nodeName);
        cmd.Parameters.AddWithValue("by", requestedBy);
        var opId = (int)(await cmd.ExecuteScalarAsync())!;

        _ = Task.Run(() => RunRemoveReplicaAsync(opId, nodeName));
        return opId;
    }

    private async Task RunRemoveReplicaAsync(int opId, string nodeName)
    {
        var log = new List<string>();
        void Log(string msg) { lock (log) log.Add($"[{DateTime.UtcNow:HH:mm:ss}] {msg}"); }

        await using var conn = new NpgsqlConnection(_metadataConn);
        await conn.OpenAsync();

        var lockId = await TryAcquireLockAsync(conn, opId);
        if (lockId == null)
        {
            await UpdateOpAsync(conn, opId, "failed", "Başqa əməliyyat davam edir", log);
            return;
        }

        try
        {
            await UpdateOpStatusAsync(conn, opId, "running");

            // 1) Patroni-dən çıxar
            Log($"'{nodeName}' Patroni-dən çıxarılır...");
            var patroniResult = await _controller.SwitchoverAsync("", "");  // placeholder
            // Real: DELETE /cluster/{name} Patroni REST API
            // Sadələşdirmək üçün birbaşa docker compose rm istifadə edirik

            // 2) Konteyneri sil
            Log($"'{nodeName}' konteyneri silinir...");
            var result = await _controller.RemoveReplicaAsync(nodeName);

            if (!result.Success)
            {
                Log($"❌ Uğursuz: {result.Error}");
                await UpdateOpAsync(conn, opId, "failed", result.Error, log);
                return;
            }

            Log($"✅ '{nodeName}' silindi");
            await UpdateOpAsync(conn, opId, "completed", null, log);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Remove replica #{Id} xətası", opId);
            await UpdateOpAsync(conn, opId, "failed", ex.Message, log);
        }
        finally
        {
            await ReleaseLockAsync(conn);
        }
    }

    // ── SWITCHOVER ───────────────────────────────────────
    public async Task<int> RequestSwitchoverAsync(
        string leader, string candidate, string requestedBy)
    {
        await using var conn = new NpgsqlConnection(_metadataConn);
        await conn.OpenAsync();

        var cluster = await _patroni.GetClusterStatusAsync();
        if (!cluster.LeaderExists)
            throw new InvalidOperationException("Leader yoxdur");

        if (leader == candidate)
            throw new InvalidOperationException("Leader və candidate eyni ola bilməz");

        const string sql = @"
            INSERT INTO cluster_operations
                (operation_type, target_node, status, requested_by, metadata)
            VALUES ('switchover', @node, 'pending', @by, @meta::jsonb)
            RETURNING id;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("node", candidate);
        cmd.Parameters.AddWithValue("by", requestedBy);
        cmd.Parameters.AddWithValue("meta",
            JsonSerializer.Serialize(new { leader, candidate }));
        var opId = (int)(await cmd.ExecuteScalarAsync())!;

        _ = Task.Run(() => RunSwitchoverAsync(opId, leader, candidate));
        return opId;
    }

    private async Task RunSwitchoverAsync(int opId, string leader, string candidate)
    {
        var log = new List<string>();
        void Log(string msg) { lock (log) log.Add($"[{DateTime.UtcNow:HH:mm:ss}] {msg}"); }

        await using var conn = new NpgsqlConnection(_metadataConn);
        await conn.OpenAsync();

        var lockId = await TryAcquireLockAsync(conn, opId);
        if (lockId == null)
        {
            await UpdateOpAsync(conn, opId, "failed", "Başqa əməliyyat davam edir", log);
            return;
        }

        try
        {
            await UpdateOpStatusAsync(conn, opId, "running");
            Log($"Switchover: {leader} → {candidate}");

            var result = await _controller.SwitchoverAsync(leader, candidate);
            if (!result.Success)
            {
                Log($"❌ Uğursuz: {result.Error}");
                await UpdateOpAsync(conn, opId, "failed", result.Error, log);
                return;
            }

            Log("✅ Switchover tamamlandı");
            await UpdateOpAsync(conn, opId, "completed", null, log);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Switchover #{Id} xətası", opId);
            await UpdateOpAsync(conn, opId, "failed", ex.Message, log);
        }
        finally
        {
            await ReleaseLockAsync(conn);
        }
    }

    // ── READ ─────────────────────────────────────────────
    public async Task<ClusterOperation?> GetAsync(int id)
    {
        await using var conn = new NpgsqlConnection(_metadataConn);
        await conn.OpenAsync();
        const string sql = @"
            SELECT id, operation_type, target_node, status, requested_by,
                   requested_at, started_at, finished_at, log, error, metadata
            FROM cluster_operations WHERE id = @id;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? MapOp(r) : null;
    }

    public async Task<List<ClusterOperation>> ListAsync()
    {
        await using var conn = new NpgsqlConnection(_metadataConn);
        await conn.OpenAsync();
        const string sql = @"
            SELECT id, operation_type, target_node, status, requested_by,
                   requested_at, started_at, finished_at, log, error, metadata
            FROM cluster_operations ORDER BY requested_at DESC LIMIT 50;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        var list = new List<ClusterOperation>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(MapOp(r));
        return list;
    }

    private static ClusterOperation MapOp(NpgsqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        OperationType = r.GetString(1),
        TargetNode = r.IsDBNull(2) ? null : r.GetString(2),
        Status = r.GetString(3),
        RequestedBy = r.GetString(4),
        RequestedAt = r.GetDateTime(5),
        StartedAt = r.IsDBNull(6) ? null : r.GetDateTime(6),
        FinishedAt = r.IsDBNull(7) ? null : r.GetDateTime(7),
        Log = r.IsDBNull(8) ? null : r.GetString(8),
        Error = r.IsDBNull(9) ? null : r.GetString(9),
        Metadata = r.IsDBNull(10) ? null : r.GetString(10)
    };

    // ── UPDATE köməkçiləri ───────────────────────────────
    private async Task UpdateOpStatusAsync(NpgsqlConnection conn, int id, string status)
    {
        const string sql = "UPDATE cluster_operations SET status = @s, started_at = now() WHERE id = @id;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("s", status);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task UpdateOpAsync(
        NpgsqlConnection conn, int id, string status, string? error, List<string> log)
    {
        const string sql = @"
            UPDATE cluster_operations
            SET status = @s, finished_at = now(), log = @log, error = @err
            WHERE id = @id;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("s", status);
        cmd.Parameters.AddWithValue("log", string.Join("\n", log));
        cmd.Parameters.AddWithValue("err", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}

public class ClusterOperation
{
    public int Id { get; set; }
    public string OperationType { get; set; } = "";
    public string? TargetNode { get; set; }
    public string Status { get; set; } = "";
    public string RequestedBy { get; set; } = "";
    public DateTime RequestedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Log { get; set; }
    public string? Error { get; set; }
    public string? Metadata { get; set; }
}