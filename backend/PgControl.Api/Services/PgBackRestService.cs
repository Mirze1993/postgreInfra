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