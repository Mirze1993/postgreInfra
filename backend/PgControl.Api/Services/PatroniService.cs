using System.Text.Json;

namespace PgControl.Api.Services;

public class PatroniService
{
    private readonly HttpClient _http;
    private readonly ILogger<PatroniService> _logger;

    // Konfiqurasiyadan node ünvanlarını oxuyuruq (appsettings.json)
    private readonly string[] _nodeUrls;

    public PatroniService(HttpClient http, IConfiguration cfg, ILogger<PatroniService> logger)
    {
        _http = http;
        _logger = logger;
        _nodeUrls = cfg.GetSection("Patroni:Nodes").Get<string[]>()
                    ?? new[] { "http://pg1:8008", "http://pg2:8008" };
    }

    public async Task<PatroniNodeStatus?> GetNodeStatusAsync(string nodeUrl)
    {
        try
        {
            var resp = await _http.GetAsync($"{nodeUrl}/patroni");
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new PatroniNodeStatus
            {
                Url = nodeUrl,
                State = root.GetProperty("state").GetString() ?? "unknown",
                Role = root.GetProperty("role").GetString() ?? "unknown",
                Timeline = root.TryGetProperty("timeline", out var tl) ? tl.GetInt64() : 0,
                ClusterUnlocked = root.TryGetProperty("cluster_unlocked", out var cu) && cu.GetBoolean()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Patroni node {Url} əlçatmazdır", nodeUrl);
            return new PatroniNodeStatus { Url = nodeUrl, State = "unreachable", Role = "unknown" };
        }
    }

    public async Task<PatroniClusterStatus> GetClusterStatusAsync()
    {
        var members = await GetClusterMembersAsync();

        var nodes = new List<PatroniNodeStatus>();

        if (members?.Members != null && members.Members.Count > 0)
        {
            foreach (var member in members.Members)
            {
                var nodeUrl = $"http://{member.Name}:8008";

                var status = await GetNodeStatusAsync(nodeUrl);

                if (status != null)
                    nodes.Add(status);
            }
        }
        else
        {
            foreach (var url in _nodeUrls)
            {
                var status = await GetNodeStatusAsync(url);

                if (status != null)
                    nodes.Add(status);
            }
        }

        var leader = nodes.FirstOrDefault(n =>
            n.Role.Equals("primary", StringComparison.OrdinalIgnoreCase) ||
            n.Role.Equals("master", StringComparison.OrdinalIgnoreCase) ||
            n.Role.Equals("leader", StringComparison.OrdinalIgnoreCase));

        return new PatroniClusterStatus
        {
            Nodes = nodes,
            LeaderName = leader?.Url,
            LeaderExists = leader != null,
            HasQuorum = nodes.Count(n =>
                n.State.Equals("running", StringComparison.OrdinalIgnoreCase)) >= 2
        };
    }

    public async Task<PatroniClusterMembers?> GetClusterMembersAsync()
    {
        // /cluster endpoint-i bütün topologiyanı qaytarır
        foreach (var url in _nodeUrls)
        {
            try
            {
                var resp = await _http.GetAsync($"{url}/cluster");
                if (!resp.IsSuccessStatusCode) continue;

                var json = await resp.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<PatroniClusterMembers>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { /* növbəti node-u yoxla */ }
        }
        return null;
    }
}

// ─── DTO-lar ───

public class PatroniNodeStatus
{
    public string Url { get; set; } = "";
    public string State { get; set; } = "unknown";
    public string Role { get; set; } = "unknown";
    public long Timeline { get; set; }
    public bool ClusterUnlocked { get; set; }
}

public class PatroniClusterStatus
{
    public List<PatroniNodeStatus> Nodes { get; set; } = new();
    public string? LeaderName { get; set; }
    public bool LeaderExists { get; set; }
    public bool HasQuorum { get; set; }
}

public class PatroniClusterMembers
{
    public List<PatroniMember> Members { get; set; } = new();
}

public class PatroniMember
{
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string State { get; set; } = "";
    public long Timeline { get; set; }
    public long? Lag { get; set; }
}