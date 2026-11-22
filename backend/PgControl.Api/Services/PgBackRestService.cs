using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PgControl.Api.Services;

public class PgBackRestService
{
    private readonly ILogger<PgBackRestService> _logger;
    private readonly string _containerName;
    private readonly string _stanza;

    public PgBackRestService(IConfiguration cfg, ILogger<PgBackRestService> logger)
    {
        _logger = logger;
        _containerName = cfg["PgBackRest:ContainerName"] ?? "pg1";
        _stanza = cfg["PgBackRest:Stanza"] ?? "pgcluster";
    }

    public async Task<PgBackRestStanza?> GetInfoAsync()
    {
        var args = $"exec -u postgres {_containerName} pgbackrest --stanza={_stanza} info --output=json";
        var (exitCode, output, error) = await RunProcessAsync("docker", args);

        var trimmed = output.TrimStart();

        if (string.IsNullOrWhiteSpace(trimmed) || !trimmed.StartsWith("["))
        {
            _logger.LogWarning(
                "pgbackrest info JSON olmayan çıxış. ExitCode={ExitCode}, Output(ilk 300): {Output}",
                exitCode,
                trimmed.Length > 300 ? trimmed[..300] : trimmed);
            return null;
        }

        try
        {
            // pgBackRest kökdə ARRAY qaytarır → List<PgBackRestStanza>
            var stanzas = JsonSerializer.Deserialize<List<PgBackRestStanza>>(trimmed,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var stanza = stanzas?.FirstOrDefault();
            if (stanza == null)
            {
                _logger.LogWarning("pgbackrest info boş array qaytardı");
                return null;
            }

            if (exitCode != 0 || stanza.Status.Code != 0)
            {
                _logger.LogWarning(
                    "pgbackrest stanza '{Name}' status: {Code} - {Message}",
                    stanza.Name, stanza.Status.Code, stanza.Status.Message);
            }

            return stanza;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "pgbackrest JSON parse xətası. Output(ilk 500): {Output}",
                trimmed.Length > 500 ? trimmed[..500] : trimmed);
            return null;
        }
    }
public async Task<bool> CreateBackupAsync(string type, string? label = null)
{
    // type: full | diff | incr
    var labelArg = string.IsNullOrEmpty(label) ? "" : $"--annotation=label:{label}";
    var args = $"exec -u postgres {_containerName} pgbackrest --stanza={_stanza} " +
               $"--type={type} {labelArg} backup";

    var (exit, output, err) = await RunProcessAsync("docker", args);
    _logger.LogInformation("Backup {Type}: exit={Exit}", type, exit);

    if (exit != 0)
        _logger.LogError("Backup xətası: {Err}", err);

    return exit == 0;
}

public async Task<bool> VerifyAsync()
{
    var args = $"exec -u postgres {_containerName} pgbackrest --stanza={_stanza} verify";
    var (exit, output, err) = await RunProcessAsync("docker", args);
    return exit == 0;
}

/// <summary>
/// PITR bərpa preview — heç nə etmir, yalnız mövcud backup/WAL aralığını yoxlayır.
/// </summary>
public async Task<RestorePreview> PreviewRestoreAsync(DateTime targetTime)
{
    var info = await GetInfoAsync();
    if (info == null)
        return new RestorePreview
        {
            Valid = false,
            Reason = "pgBackRest əlçatmazdır"
        };

    var backups = info.Backups.OrderBy(b => b.Timestamp.Stop).ToList();
    if (backups.Count == 0)
        return new RestorePreview { Valid = false, Reason = "Heç bir backup yoxdur" };

    // Verilmiş vaxt üçün ən uyğun backup-ı tap
    var backup = backups
        .Where(b => DateTimeOffset.FromUnixTimeSeconds(b.Timestamp.Stop).UtcDateTime <= targetTime)
        .OrderByDescending(b => b.Timestamp.Stop)
        .FirstOrDefault();

    if (backup == null)
        return new RestorePreview
        {
            Valid = false,
            Reason = $"Bu vaxtdan ({targetTime:yyyy-MM-dd HH:mm:ss}) əvvəl backup yoxdur."
        };

    var backupStop = DateTimeOffset.FromUnixTimeSeconds(backup.Timestamp.Stop).UtcDateTime;
    var age = targetTime - backupStop;

    return new RestorePreview
    {
        Valid = true,
        BackupLabel = backup.Label,
        BackupType = backup.Type,
        BackupStopTime = backupStop,
        TargetTime = targetTime,
        AgeAfterBackup = age.ToString(@"dd\.hh\:mm\:ss"),
        WalStart = backup.Archive.Start,
        WalStop = backup.Archive.Stop,
        Warning = "⚠ Bu əməliyyat klasteri dayandıracaq. Bütün yazmalar dayanacaq."
    };
}

/// <summary>
/// PITR bərpa — ÇOX RİSKLİ. Yalnız `confirmed=true` ilə çağırılmalıdır.
/// </summary>
public async Task<RestoreResult> RestoreAsync(
    DateTime targetTime, string targetAction, IProgress<string>? progress = null)
{
    progress?.Report("[1/6] Bərpa əməliyyatı başlayır...");

    // 1) Patroni pause et (avtomatik failover söndür)
    progress?.Report("[2/6] Patroni pause edilir...");
    var (pauseExit, pauseOut, pauseErr) = await RunProcessAsync(
        "docker",
        $"exec -u postgres {_containerName} patronictl -c /etc/patroni.yml pause");
    if (pauseExit != 0)
        return new RestoreResult { Success = false, Error = $"Pause xətası: {pauseErr}" };

    try
    {
        // 2) PostgreSQL dayandır
        progress?.Report("[3/6] PostgreSQL dayandırılır...");
        var (stopExit, _, stopErr) = await RunProcessAsync(
            "docker",
            $"exec -u postgres {_containerName} pg_ctl -D /var/lib/postgresql/data/pgdata stop -m fast");
        // Bu əmr xəta qaytara bilər — Patroni artıq idarə etmirsə problem deyil
        _logger.LogInformation("Stop: exit={Exit}", stopExit);

        // 3) pgBackRest restore
        progress?.Report($"[4/6] Bərpa edilir (target: {targetTime:yyyy-MM-dd HH:mm:ss UTC})...");
        var targetStr = targetTime.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fff") + "+00";
        var restoreArgs = $"exec -u postgres {_containerName} " +
            $"pgbackrest --stanza={_stanza} --delta --type=time " +
            $"--target=\"{targetStr}\" --target-action={targetAction} " +
            $"--pg1-path=/var/lib/postgresql/data/pgdata restore";

        var (restoreExit, restoreOut, restoreErr) = await RunProcessAsync("docker", restoreArgs);

        if (restoreExit != 0)
        {
            progress?.Report($"❌ Bərpa uğursuz: {restoreErr}");
            return new RestoreResult
            {
                Success = false,
                Error = restoreErr,
                Log = restoreOut
            };
        }

        // 4) PostgreSQL başlat
        progress?.Report("[5/6] PostgreSQL başladılır...");
        var (startExit, _, startErr) = await RunProcessAsync(
            "docker",
            $"exec -u postgres {_containerName} pg_ctl -D /var/lib/postgresql/data/pgdata -l /tmp/restore-startup.log start");

        // 5) Patroni resume
        progress?.Report("[6/6] Patroni resume edilir...");
        var (resumeExit, resumeOut, resumeErr) = await RunProcessAsync(
            "docker",
            $"exec -u postgres {_containerName} patronictl -c /etc/patroni.yml resume");

        progress?.Report("✅ Bərpa tamamlandı");

        return new RestoreResult
        {
            Success = true,
            Log = restoreOut,
            TargetTime = targetTime
        };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Restore xətası");
        // Hər halda Patroni resume etməyə çalış
        try
        {
            await RunProcessAsync("docker",
                $"exec -u postgres {_containerName} patronictl -c /etc/patroni.yml resume");
        }
        catch { /* ignore */ }

        return new RestoreResult { Success = false, Error = ex.Message };
    }
}
    private static async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(
        string fileName, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
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

// ─── DTO-lar (pgBackRest JSON strukturu) ───
public class PgBackRestStanza
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("status")]
    public PgBackRestStatus Status { get; set; } = new();

    [JsonPropertyName("backup")]
    public List<PgBackRestBackup> Backups { get; set; } = new();

    [JsonPropertyName("archive")]
    public List<PgBackRestArchive> Archives { get; set; } = new();

    [JsonPropertyName("repo")]
    public List<PgBackRestRepo> Repos { get; set; } = new();
}

public class PgBackRestStatus
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
public class PgBackRestBackup
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public PgBackRestTimestamp Timestamp { get; set; } = new();

    // WAL aralığı → "archive" (pgBackRest 2.45+ / 3.x)
    [JsonPropertyName("archive")]
    public PgBackRestArchiveRange Archive { get; set; } = new();

    // Əsas ölçülər → "info"
    [JsonPropertyName("info")]
    public PgBackRestInfo Info { get; set; } = new();

    // LSN aralığı (bonus - göstərmək istəsən)
    [JsonPropertyName("lsn")]
    public PgBackRestLsn Lsn { get; set; } = new();

    // Referans (full backup üçün null ola bilər!)
    [JsonPropertyName("reference")]
    public List<string>? Reference { get; set; }

    // Backup zamanı xəta olub?
    [JsonPropertyName("error")]
    public bool Error { get; set; }

    // Geriyə uyğunluq üçün (bəzi yerlərdə lazım ola bilər)
    [JsonPropertyName("database")]
    public PgBackRestDatabase Database { get; set; } = new();
}

public class PgBackRestArchiveRange
{
    [JsonPropertyName("start")]
    public string Start { get; set; } = "";

    [JsonPropertyName("stop")]
    public string Stop { get; set; } = "";
}

public class PgBackRestInfo
{
    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("delta")]
    public long Delta { get; set; }

    [JsonPropertyName("repository")]
    public PgBackRestRepository Repository { get; set; } = new();
}

public class PgBackRestRepository
{
    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("delta")]
    public long Delta { get; set; }
}

public class PgBackRestLsn
{
    [JsonPropertyName("start")]
    public string Start { get; set; } = "";

    [JsonPropertyName("stop")]
    public string Stop { get; set; } = "";
}

public class PgBackRestDatabase
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("repo-key")]
    public int RepoKey { get; set; }
}

public class PgBackRestTimestamp
{
    [JsonPropertyName("start")]
    public long Start { get; set; }

    [JsonPropertyName("stop")]
    public long Stop { get; set; }
}

public class PgBackRestWal
{
    [JsonPropertyName("start")]
    public string Start { get; set; } = "";

    [JsonPropertyName("stop")]
    public string Stop { get; set; } = "";
}



public class PgBackRestRepo
{
    [JsonPropertyName("key")]
    public int Key { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("status")]
    public PgBackRestStatus Status { get; set; } = new();
}

public class PgBackRestArchive
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("min")]
    public string Min { get; set; } = "";

    [JsonPropertyName("max")]
    public string Max { get; set; } = "";
}
public class RestorePreview
{
    public bool Valid { get; set; }
    public string? Reason { get; set; }
    public string? BackupLabel { get; set; }
    public string? BackupType { get; set; }
    public DateTime? BackupStopTime { get; set; }
    public DateTime? TargetTime { get; set; }
    public string? AgeAfterBackup { get; set; }
    public string? WalStart { get; set; }
    public string? WalStop { get; set; }
    public string? Warning { get; set; }
}

public class RestoreResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Log { get; set; }
    public DateTime? TargetTime { get; set; }
}