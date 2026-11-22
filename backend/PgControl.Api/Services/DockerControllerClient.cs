namespace PgControl.Api.Services;

public class DockerControllerClient
{
    private readonly HttpClient _http;
    private readonly ILogger<DockerControllerClient> _logger;

    public DockerControllerClient(HttpClient http, IConfiguration cfg, ILogger<DockerControllerClient> logger)
    {
        _http = http;
        _logger = logger;

        var baseUrl = cfg["Controller:BaseUrl"]
            ?? throw new InvalidOperationException("Controller:BaseUrl təyin olunmayıb");
        var token = cfg["Controller:Token"]
            ?? throw new InvalidOperationException("Controller:Token təyin olunmayıb");

        _http.BaseAddress = new Uri(baseUrl);
        _http.DefaultRequestHeaders.Add("X-Controller-Token", token);
        _http.Timeout = TimeSpan.FromMinutes(5);
    }

    public Task<ControllerResult> StartNodeAsync(string name)
        => PostAsync($"/internal/nodes/{name}/start");

    public Task<ControllerResult> StopNodeAsync(string name)
        => PostAsync($"/internal/nodes/{name}/stop");

    public Task<ControllerResult> RestartNodeAsync(string name)
        => PostAsync($"/internal/nodes/{name}/restart");

    public Task<ControllerResult> CreateReplicaAsync(string name, int hostPort)
        => PostAsync("/internal/replicas/create", new { name, hostPort });

    public Task<ControllerResult> RemoveReplicaAsync(string name)
        => PostAsync($"/internal/replicas/{name}/remove");

    public Task<ControllerResult> SwitchoverAsync(string leader, string candidate)
        => PostAsync("/internal/switchover", new { leader, candidate });

    private async Task<ControllerResult> PostAsync(string path, object? body = null)
    {
        try
        {
            var resp = body == null
                ? await _http.PostAsync(path, null)
                : await _http.PostAsJsonAsync(path, body);

            var content = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Controller {Path} → {Status}: {Content}",
                    path, (int)resp.StatusCode, content);
                return new ControllerResult { Success = false, Error = content };
            }
            return new ControllerResult { Success = true, Output = content };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Controller {Path} xətası", path);
            return new ControllerResult { Success = false, Error = ex.Message };
        }
    }
}

public class ControllerResult
{
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
}