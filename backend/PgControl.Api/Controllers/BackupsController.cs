using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PgControl.Api.Services;

namespace PgControl.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BackupsController : ControllerBase
{
   
    private readonly PostgresMetricsService _metrics;

    private readonly PgBackRestService _pgBackRest;
    private readonly RestoreJobService _restoreJobs;
    private readonly IConfiguration _cfg;

// Konstruktoru yenilə:
    public BackupsController(
        PgBackRestService pgBackRest,
        RestoreJobService restoreJobs,
        PostgresMetricsService metrics,
        IConfiguration cfg)
    {
        _pgBackRest = pgBackRest;
        _restoreJobs = restoreJobs;
        _metrics = metrics;
        _cfg = cfg;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var stanza = await _pgBackRest.GetInfoAsync();

        if (stanza == null)
        {
            return StatusCode(503, new { error = "pgBackRest əlçatmazdır" });
        }

        var backups = stanza.Backups
            .OrderByDescending(b => b.Timestamp.Stop)
            .Select(b => new
            {
                label = b.Label,
                type = NormalizeType(b.Type),
                timestamp = new
                {
                    start = DateTimeOffset.FromUnixTimeSeconds(b.Timestamp.Start).UtcDateTime,
                    stop = DateTimeOffset.FromUnixTimeSeconds(b.Timestamp.Stop).UtcDateTime
                },
                // WAL aralığı → archive.start/stop
                wal = new { start = b.Archive.Start, stop = b.Archive.Stop },
                // LSN aralığı (əlavə)
                lsn = new { start = b.Lsn.Start, stop = b.Lsn.Stop },
                // Backup ölçüsü → info.size (sıxılmamış)
                sizeBytes = b.Info.Size,
                sizePretty = BytesToPretty(b.Info.Size),
                // Repo ölçüsü → info.repository.size (sıxılmış)
                repoSizeBytes = b.Info.Repository.Size,
                repoSizePretty = BytesToPretty(b.Info.Repository.Size),
                // Xəta statusu
                hasError = b.Error,
                // Referans null ola bilər → boş listə çevir
                references = b.Reference ?? new List<string>()
            })
            .ToList();

        var archive = stanza.Archives.FirstOrDefault();

        return Ok(new
        {
            stanza = stanza.Name,
            status = new { code = stanza.Status.Code, message = stanza.Status.Message },
            backups,
            wal = new
            {
                min = archive?.Min,
                max = archive?.Max
            }
        });
    }

    [HttpGet("wal-archive")]
    public async Task<IActionResult> WalArchive()
    {
        var wal = await _metrics.GetWalArchiveMetricsAsync();

        // Boş lab mühitində WAL 5-15 dəqiqə ərzində də yarana bilər (yazma azdır).
        // Production-da 300s (5 dəq) yaxşı threshold-dur.
        // `failedCount` kumulyativdir – yalnız məlumat üçün, sağlamlıq üçün deyil.
        const int warningSeconds = 600; // 10 dəqiqə
        const int criticalSeconds = 1800; // 30 dəqiqə

        var isHealthy = wal.SecondsSinceLastArchive >= 0
                        && wal.SecondsSinceLastArchive < criticalSeconds;

        var isWarning = isHealthy && wal.SecondsSinceLastArchive >= warningSeconds;

        return Ok(new
        {
            failedCount = wal.FailedCount,
            secondsSinceLastArchive = wal.SecondsSinceLastArchive,
            isHealthy,
            isWarning,
            status = !isHealthy ? "critical" : (isWarning ? "warning" : "ok")
        });
    }


// ── BACKUP YARAT ──
public record CreateBackupReq(string Type);  // full | diff | incr

[HttpPost]
[Authorize(Roles = "Admin,Operator")]
public async Task<IActionResult> Create([FromBody] CreateBackupReq req)
{
    if (req.Type is not ("full" or "diff" or "incr"))
        return BadRequest(new { error = "Type 'full', 'diff' və ya 'incr' olmalıdır" });

    // Arxa planda işə sal (uzun sürə bilər)
    _ = Task.Run(async () =>
    {
        try { await _pgBackRest.CreateBackupAsync(req.Type); }
        catch (Exception ex) { /* log */ }
    });

    return Accepted(new { message = $"Backup ({req.Type}) başladıldı. Bir neçə saniyə sonra siyahını yeniləyin." });
}

// ── VERIFY ──
[HttpPost("verify")]
[Authorize(Roles = "Admin,Operator")]
public async Task<IActionResult> Verify()
{
    var ok = await _pgBackRest.VerifyAsync();
    return ok ? Ok(new { message = "Backup bütövlüyü təsdiqləndi" })
              : StatusCode(500, new { error = "Verify uğursuz" });
}

// ── PITR PREVIEW ──
public record RestorePreviewReq(DateTime TargetTime);

[HttpPost("restore/preview")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> PreviewRestore([FromBody] RestorePreviewReq req)
{
    var preview = await _pgBackRest.PreviewRestoreAsync(req.TargetTime);
    return Ok(preview);
}

// ── PITR RESTORE (İKİ ADDIMLI TƏSDİQ) ──
public record RestoreReq(DateTime TargetTime, string TargetAction, string Confirmation);

[HttpPost("restore")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> Restore([FromBody] RestoreReq req)
{
    // 1) İki addımlı təsdiq
    if (req.Confirmation != "RESTORE")
        return BadRequest(new { error = "Təsdiq üçün 'RESTORE' yazın" });

    // 2) Target action yoxla
    if (req.TargetAction is not ("promote" or "pause" or "shutdown"))
        return BadRequest(new { error = "TargetAction 'promote', 'pause' və ya 'shutdown' olmalıdır" });

    // 3) Preview əvvəlcə yoxla
    var preview = await _pgBackRest.PreviewRestoreAsync(req.TargetTime);
    if (!preview.Valid)
        return BadRequest(new { error = preview.Reason });

    // 4) Job yarat
    var user = User.Identity?.Name ?? "unknown";
    var jobId = await _restoreJobs.CreateJobAsync(req.TargetTime, req.TargetAction, user);

    // 5) İşə sal
    await _restoreJobs.StartJobAsync(jobId);

    return Accepted(new { jobId, message = "Bərpa başladıldı. Job statusunu izləyin." });
}

// ── JOB STATUS ──
[HttpGet("restore/{jobId:int}")]
[Authorize]
public async Task<IActionResult> GetRestoreJob(int jobId)
{
    var job = await _restoreJobs.GetJobAsync(jobId);
    return job == null ? NotFound() : Ok(job);
}

[HttpGet("restore")]
[Authorize]
public async Task<IActionResult> ListRestoreJobs()
    => Ok(await _restoreJobs.ListJobsAsync());

    private static string NormalizeType(string type) =>
        type.ToLowerInvariant() switch
        {
            "full" => "full",
            "diff" => "diff",
            "incr" => "incr",
            _ => type
        };

    private static string BytesToPretty(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int i = 0;
        while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
        return $"{size:0.##} {units[i]}";
    }
}