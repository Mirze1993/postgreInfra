using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace PgControl.Api.Services;

public class MigrationScriptService
{
    private readonly string _metadataConn;
    private readonly string _stagingConn;
    private readonly string _productionConn;
    private readonly ILogger<MigrationScriptService> _logger;

    public MigrationScriptService(IConfiguration cfg, ILogger<MigrationScriptService> logger)
    {
        _metadataConn = cfg.GetConnectionString("Metadata")!;
        _stagingConn = cfg["Flyway:StagingConnection"]!;
        _productionConn = cfg["Flyway:ProductionConnection"]!;
        _logger = logger;
    }

    // ── 1. CREATE (draft) ─────────────────────────────────
    public async Task<MigrationScript> CreateAsync(
        string version, string description, string sqlContent,
        string? rollbackSql, string createdBy)
    {
        // Version formatı yoxla: V + rəqəm
        if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"^V\d+$"))
            throw new ArgumentException("Version formatı 'V1', 'V2'... olmalıdır");

        var checksum = ComputeSha256(sqlContent);

        await using var conn = new NpgsqlConnection(_metadataConn);
        await conn.OpenAsync();

        const string sql = @"
            INSERT INTO migration_scripts
                (version, description, sql_content, checksum, status,
                 created_by, rollback_sql)
            VALUES (@v, @d, @s, @c, 'draft', @by, @rb)
            RETURNING id;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("v", version);
        cmd.Parameters.AddWithValue("d", description);
        cmd.Parameters.AddWithValue("s", sqlContent);
        cmd.Parameters.AddWithValue("c", checksum);
        cmd.Parameters.AddWithValue("by", createdBy);
        cmd.Parameters.AddWithValue("rb", (object?)rollbackSql ?? DBNull.Value);

        var id = (int)(await cmd.ExecuteScalarAsync())!;

        await LogAuditAsync(conn, id, version, "created", createdBy,
            new { description, checksum });

        return await GetByIdAsync(id);
    }

    // ── 2. SUBMIT (draft → pending → validating → ...) ────
    public async Task SubmitAsync(int id, string submittedBy)
    {
        await using var conn = new NpgsqlConnection(_metadataConn);
        await conn.OpenAsync();

        // Status yoxla
        var current = await GetStatusAsync(conn, id);
        if (current != "draft")
            throw new InvalidOperationException($"Yalnız 'draft' statuslu skript təqdim edilə bilər (indi: {current})");

        // Status-u 'validating' et
        await SetStatusAsync(conn, id, "validating", submittedBy: submittedBy);
        await LogAuditAsync(conn, id, null, "submitted", submittedBy, null);

        // Arxa planda validasiya (staging-də)
        _ = Task.Run(() => ValidateAndRunAsync(id));
    }

    // ── 3. VALIDATE (staging-də icra) ─────────────────────
    private async Task ValidateAndRunAsync(int id)
    {
        await using var conn = new NpgsqlConnection(_metadataConn);
        await conn.OpenAsync();

        var script = await GetByIdAsync(id, conn);
        if (script == null) return;

        try
        {
            // Müvəqqəti qovluq yarat, SQL faylını ora yaz
            var tempDir = Path.Combine(Path.GetTempPath(), $"mig-{id}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            var filePath = Path.Combine(tempDir, $"{script.Version}__{script.Description}.sql");
            await File.WriteAllTextAsync(filePath, script.SqlContent);

            // Flyway staging-də icra et
            var (exit, output, err) = await FlywayRunner.RunAsync(
                $"migrate -url={_stagingConn} -locations=filesystem:{tempDir} -baselineOnMigrate=true");

            Directory.Delete(tempDir, true);

            if (exit == 0)
            {
                await SetStatusAsync(conn, id, "pending", validationLog: output);
                await LogAuditAsync(conn, id, script.Version, "validated", "system",
                    new { success = true });
            }
            else
            {
                await SetStatusAsync(conn, id, "validation_failed",
                    validationLog: output + "\n" + err);
                await LogAuditAsync(conn, id, script.Version, "failed", "system",
                    new { success = false, error = err });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validasiya xətası: {Id}", id);
            await SetStatusAsync(conn, id, "validation_failed", validationLog: ex.Message);
        }
    }

    // ── 4. APPROVE ────────────────────────────────────────
    public async Task ApproveAsync(int id, string approvedBy)
    {
        await using var conn = new NpgsqlConnection(_metadataConn);
        await conn.OpenAsync();

        var current = await GetStatusAsync(conn, id);
        if (current != "pending")
            throw new InvalidOperationException($"Yalnız 'pending' statuslu skript təsdiqlənə bilər (indi: {current})");

        await SetStatusAsync(conn, id, "approved", reviewedBy: approvedBy);
        await LogAuditAsync(conn, id, null, "approved", approvedBy, null);
    }

    // ── 5. APPLY (production-a tətbiq) ────────────────────
    public async Task ApplyAsync(int id, string appliedBy)
    {
        await using var conn = new NpgsqlConnection(_metadataConn);
        await conn.OpenAsync();

        var current = await GetStatusAsync(conn, id);
        if (current != "approved")
            throw new InvalidOperationException($"Yalnız 'approved' statuslu skript tətbiq edilə bilər (indi: {current})");

        var script = await GetByIdAsync(id, conn);
        if (script == null) throw new InvalidOperationException("Skript tapılmadı");

        // Checksum yoxla (dəyişməyibmi?)
        if (ComputeSha256(script.SqlContent) != script.Checksum)
            throw new InvalidOperationException("SQL məzmunu dəyişib! Checksum uyğun deyil.");

        try
        {
            // Bütün 'applied' + bu skripti bir qovluğa yaz (Flyway tarixçəsi üçün)
            var appliedScripts = await GetAppliedScriptsAsync(conn);
            var tempDir = Path.Combine(Path.GetTempPath(), $"prod-mig-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            // Əvvəl tətbiq edilmişləri yaz
            foreach (var s in appliedScripts)
            {
                var p = Path.Combine(tempDir, $"{s.Version}__{s.Description}.sql");
                await File.WriteAllTextAsync(p, s.SqlContent);
            }

            // Bu skripti də yaz
            var newFile = Path.Combine(tempDir, $"{script.Version}__{script.Description}.sql");
            await File.WriteAllTextAsync(newFile, script.SqlContent);

            // Flyway ilə production-a tətbiq
            var (exit, output, err) = await FlywayRunner.RunAsync(
                $"migrate -url={_productionConn} -locations=filesystem:{tempDir} -baselineOnMigrate=true");

            Directory.Delete(tempDir, true);

            if (exit == 0)
            {
                await SetStatusAsync(conn, id, "applied",
                    appliedBy: appliedBy, applyLog: output);
                await LogAuditAsync(conn, id, script.Version, "applied", appliedBy,
                    new { success = true });
            }
            else
            {
                await SetStatusAsync(conn, id, "approved", applyLog: output + "\n" + err);
                await LogAuditAsync(conn, id, script.Version, "failed", appliedBy,
                    new { success = false, error = err });
                throw new InvalidOperationException($"Flyway xətası: {err}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Apply xətası: {Id}", id);
            throw;
        }
    }

    // ── 6. REJECT ─────────────────────────────────────────
    public async Task RejectAsync(int id, string rejectedBy, string reason)
    {
        await using var conn = new NpgsqlConnection(_metadataConn);
        await conn.OpenAsync();

        var current = await GetStatusAsync(conn, id);
        if (current != "pending")
            throw new InvalidOperationException($"Yalnız 'pending' statuslu skript rədd edilə bilər");

        await SetStatusAsync(conn, id, "rejected", reviewedBy: rejectedBy);
        await LogAuditAsync(conn, id, null, "rejected", rejectedBy, new { reason });
    }

    // ── 7. LIST & GET ─────────────────────────────────────
    public async Task<List<MigrationScript>> ListAsync(string? status = null)
    {
        await using var conn = new NpgsqlConnection(_metadataConn);
        await conn.OpenAsync();

        var sql = @"
            SELECT id, version, description, sql_content, checksum, status,
                   created_by, created_at, submitted_at, reviewed_by, reviewed_at,
                   applied_by, applied_at, validation_log, apply_log, rollback_sql, notes
            FROM migration_scripts";
        if (status != null) sql += " WHERE status = @status";
        sql += " ORDER BY created_at DESC;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        if (status != null) cmd.Parameters.AddWithValue("status", status);

        var list = new List<MigrationScript>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(MapReader(reader));
        }
        return list;
    }

    public async Task<MigrationScript?> GetByIdAsync(int id, NpgsqlConnection? existingConn = null)
    {
        var conn = existingConn ?? new NpgsqlConnection(_metadataConn);
        if (existingConn == null) await conn.OpenAsync();

        const string sql = @"
            SELECT id, version, description, sql_content, checksum, status,
                   created_by, created_at, submitted_at, reviewed_by, reviewed_at,
                   applied_by, applied_at, validation_log, apply_log, rollback_sql, notes
            FROM migration_scripts WHERE id = @id;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapReader(reader) : null;
    }

    public async Task<List<MigrationAuditEntry>> GetAuditLogAsync(int? migrationId = null)
    {
        await using var conn = new NpgsqlConnection(_metadataConn);
        await conn.OpenAsync();

        var sql = @"SELECT id, migration_id, version, action, performed_by, performed_at, details
                    FROM migration_audit_log";
        if (migrationId.HasValue) sql += " WHERE migration_id = @m";
        sql += " ORDER BY performed_at DESC LIMIT 200;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        if (migrationId.HasValue) cmd.Parameters.AddWithValue("m", migrationId.Value);

        var list = new List<MigrationAuditEntry>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new MigrationAuditEntry
            {
                Id = reader.GetInt64(0),
                MigrationId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                Version = reader.IsDBNull(2) ? null : reader.GetString(2),
                Action = reader.GetString(3),
                PerformedBy = reader.GetString(4),
                PerformedAt = reader.GetDateTime(5),
                Details = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }
        return list;
    }

    // ── Köməkçi metodlar ──────────────────────────────────
    private static string ComputeSha256(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task<string> GetStatusAsync(NpgsqlConnection conn, int id)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT status FROM migration_scripts WHERE id = @id;", conn);
        cmd.Parameters.AddWithValue("id", id);
        var r = await cmd.ExecuteScalarAsync();
        if (r == null) throw new InvalidOperationException($"Skript #{id} tapılmadı");
        return (string)r;
    }

    private async Task SetStatusAsync(NpgsqlConnection conn, int id, string status,
        string? submittedBy = null, string? reviewedBy = null,
        string? appliedBy = null, string? validationLog = null, string? applyLog = null)
    {
        var sets = new List<string> { "status = @s" };
        await using var cmd = new NpgsqlCommand();
        cmd.Connection = conn;
        cmd.Parameters.AddWithValue("s", status);
        cmd.Parameters.AddWithValue("id", id);

        if (submittedBy != null) { sets.Add("submitted_at = now()"); }
        if (reviewedBy != null) { sets.Add("reviewed_by = @rb"); sets.Add("reviewed_at = now()"); cmd.Parameters.AddWithValue("rb", reviewedBy); }
        if (appliedBy != null) { sets.Add("applied_by = @ab"); sets.Add("applied_at = now()"); cmd.Parameters.AddWithValue("ab", appliedBy); }
        if (validationLog != null) { sets.Add("validation_log = @vl"); cmd.Parameters.AddWithValue("vl", validationLog); }
        if (applyLog != null) { sets.Add("apply_log = @al"); cmd.Parameters.AddWithValue("al", applyLog); }

        cmd.CommandText = $"UPDATE migration_scripts SET {string.Join(", ", sets)} WHERE id = @id;";
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task LogAuditAsync(NpgsqlConnection conn, int migrationId, string? version,
        string action, string performedBy, object? details)
    {
        const string sql = @"
            INSERT INTO migration_audit_log
                (migration_id, version, action, performed_by, details)
            VALUES (@mid, @v, @a, @by, @d::jsonb);";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("mid", migrationId);
        cmd.Parameters.AddWithValue("v", (object?)version ?? DBNull.Value);
        cmd.Parameters.AddWithValue("a", action);
        cmd.Parameters.AddWithValue("by", performedBy);
        cmd.Parameters.AddWithValue("d",
            details != null ? JsonSerializer.Serialize(details) : (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<List<MigrationScript>> GetAppliedScriptsAsync(NpgsqlConnection conn)
    {
        const string sql = @"
            SELECT id, version, description, sql_content, checksum, status,
                   created_by, created_at, submitted_at, reviewed_by, reviewed_at,
                   applied_by, applied_at, validation_log, apply_log, rollback_sql, notes
            FROM migration_scripts
            WHERE status = 'applied'
            ORDER BY version;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        var list = new List<MigrationScript>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) list.Add(MapReader(reader));
        return list;
    }

    private static MigrationScript MapReader(NpgsqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Version = r.GetString(1),
        Description = r.GetString(2),
        SqlContent = r.GetString(3),
        Checksum = r.GetString(4),
        Status = r.GetString(5),
        CreatedBy = r.GetString(6),
        CreatedAt = r.GetDateTime(7),
        SubmittedAt = r.IsDBNull(8) ? null : r.GetDateTime(8),
        ReviewedBy = r.IsDBNull(9) ? null : r.GetString(9),
        ReviewedAt = r.IsDBNull(10) ? null : r.GetDateTime(10),
        AppliedBy = r.IsDBNull(11) ? null : r.GetString(11),
        AppliedAt = r.IsDBNull(12) ? null : r.GetDateTime(12),
        ValidationLog = r.IsDBNull(13) ? null : r.GetString(13),
        ApplyLog = r.IsDBNull(14) ? null : r.GetString(14),
        RollbackSql = r.IsDBNull(15) ? null : r.GetString(15),
        Notes = r.IsDBNull(16) ? null : r.GetString(16)
    };
}

// ─── DTO-lar ───
public class MigrationScript
{
    public int Id { get; set; }
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public string SqlContent { get; set; } = "";
    public string Checksum { get; set; } = "";
    public string Status { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? AppliedBy { get; set; }
    public DateTime? AppliedAt { get; set; }
    public string? ValidationLog { get; set; }
    public string? ApplyLog { get; set; }
    public string? RollbackSql { get; set; }
    public string? Notes { get; set; }
}

public class MigrationAuditEntry
{
    public long Id { get; set; }
    public int? MigrationId { get; set; }
    public string? Version { get; set; }
    public string Action { get; set; } = "";
    public string PerformedBy { get; set; } = "";
    public DateTime PerformedAt { get; set; }
    public string? Details { get; set; }
}

// ─── Flyway runner (köhnə FlywayService-i əvəz edir) ───
public static class FlywayRunner
{
    public static async Task<(int ExitCode, string Output, string Error)> RunAsync(string args)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "flyway",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output, error);
    }
}