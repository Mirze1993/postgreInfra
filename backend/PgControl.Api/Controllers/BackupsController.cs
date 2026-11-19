using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PgControl.Api.Services;

namespace PgControl.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BackupsController : ControllerBase
{
    private readonly PgBackRestService _pgBackRest;
    private readonly PostgresMetricsService _metrics;

    public BackupsController(PgBackRestService pgBackRest, PostgresMetricsService metrics)
    {
        _pgBackRest = pgBackRest;
        _metrics = metrics;
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