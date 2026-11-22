using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

var controllerToken = builder.Configuration["Controller:Token"]
    ?? throw new InvalidOperationException("Controller:Token təyin olunmayıb");

// ⚠️ VACİB: Bu HOST yolu olmalıdır (konteyner daxilindəki deyil).
// Konteyner bu qovluğu EYNİ YOL ilə mount edir:
//   -v /opt/postgreHA:/opt/postgreHA:ro
// Belə ki, docker daemon (HOST) bu faylı görə bilsin.
var hostComposeDir = builder.Configuration["Controller:HostComposeDir"]
    ?? "/opt/postgreHA";

var app = builder.Build();

// ── Autentifikasiya middleware ─────────────────────────
app.Use(async (ctx, next) =>
{
    if (!ctx.Request.Path.StartsWithSegments("/internal"))
    {
        await next();
        return;
    }

    var provided = ctx.Request.Headers["X-Controller-Token"].ToString();
    if (provided != controllerToken)
    {
        ctx.Response.StatusCode = 401;
        await ctx.Response.WriteAsync("Unauthorized");
        return;
    }
    await next();
});

app.MapGet("/health", () => new { status = "ok", hostComposeDir });

// ── NODE START ─────────────────────────────────────────
app.MapPost("/internal/nodes/{name}/start", async (string name, ILogger<Program> log) =>
    await RunDockerAsync("start", name, log));

// ── NODE STOP ──────────────────────────────────────────
app.MapPost("/internal/nodes/{name}/stop", async (string name, ILogger<Program> log) =>
    await RunDockerAsync("stop", name, log));

// ── NODE RESTART ───────────────────────────────────────
app.MapPost("/internal/nodes/{name}/restart", async (string name, ILogger<Program> log) =>
    await RunDockerAsync("restart", name, log));

// ── REPLICA CREATE (docker run) ────────────────────────
app.MapPost("/internal/replicas/create", async (
    CreateReplicaReq req, ILogger<Program> log) =>
{
    if (!System.Text.RegularExpressions.Regex.IsMatch(req.Name, @"^pg\d+$"))
        return Results.BadRequest(new { error = "Ad yalnız 'pg' + rəqəm olmalıdır" });

    // 1) Konfiq faylını HOST yolundan yoxla
    var hostPatroniConfig = $"{hostComposeDir}/patroni-{req.Name}.yml";
    if (!File.Exists(hostPatroniConfig))
        return Results.Json(
            new { success = false, error = $"Konfiq faylı yoxdur: {hostPatroniConfig}" },
            statusCode: 400);

    // 2) Network adını pg1-dən dinamik tap
    // 2) Network adını pg1-dən dinamik tap (DÜZGÜN — separator ilə)
    var (netExit, netOut, netErr) = await RunProcessAsync(
        "docker",
        "inspect pg1 --format \"{{ range $k, $v := .NetworkSettings.Networks }}{{ $k }}|{{ end }}\"");

    if (netExit != 0)
        return Results.Json(
            new { success = false, error = $"Network aşkar edilə bilmədi: {netErr}" },
            statusCode: 500);

// netOut = "pg-ha-lab_pgnet|proxy|"
// 1) Əvvəlcə "_pgnet" ilə bitən network-ü seç (HA internal)
// 2) Yoxdursa, "proxy" olmayanı seç
// 3) Heç biri yoxdursa, ilkini seç
    var networks = netOut
        .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();

    var networkName = networks.FirstOrDefault(n => n.EndsWith("_pgnet"))
                      ?? networks.FirstOrDefault(n => !n.Contains("proxy"))
                      ?? networks.FirstOrDefault()
                      ?? "";

    if (string.IsNullOrEmpty(networkName))
        return Results.Json(
            new { success = false, error = "pg1 üçün heç bir network tapılmadı" },
            statusCode: 500);

// Əlavə: "proxy" içərisində "pgnet" ola bilər — yoxla
    if (networkName.Contains("pgnetproxy"))
        return Results.Json(
            new { success = false, error = $"Network adı səhv birləşdirilib: {networkName}" },
            statusCode: 500);
    if (string.IsNullOrEmpty(networkName))
        return Results.Json(
            new { success = false, error = "pg1-in network-ü yoxdur" },
            statusCode: 500);

    // 3) Volume adını pg1-dən dinamik tap
    var (volExit, volOut, volErr) = await RunProcessAsync(
        "docker",
        "inspect pg1 --format \"{{ range .Mounts }}{{ if eq .Destination \\\"/var/lib/postgresql/data\\\" }}{{ .Name }}{{ end }}{{ end }}\"");
    if (volExit != 0 || string.IsNullOrWhiteSpace(volOut))
        return Results.Json(
            new { success = false, error = $"Volume aşkar edilə bilmədi: {volErr}" },
            statusCode: 500);

    var pg1Volume = volOut.Trim();
    var projectPrefix = pg1Volume.Replace("_pg1_data", "");
    if (projectPrefix == pg1Volume)
        return Results.Json(
            new { success = false, error = $"Volume adı gözlənilən formatda deyil: {pg1Volume}" },
            statusCode: 500);

    var newVolume = $"{projectPrefix}_{req.Name}_data";

    log.LogInformation(
        "Creating {Name}: network={Net}, volume={Vol}, config={Cfg}",
        req.Name, networkName, newVolume, hostPatroniConfig);

    // 4) Volume yarat (əgər yoxdursa)
    await RunProcessAsync("docker", $"volume create {newVolume}");

    // 5) docker run (compose deyil!)
    var runArgs =
        $"run -d " +
        $"--name {req.Name} " +
        $"--hostname {req.Name} " +
        $"--network {networkName} " +
        $"--user root " +
        $"--restart unless-stopped " +
        $"-v {newVolume}:/var/lib/postgresql/data " +
        $"-v {hostPatroniConfig}:/etc/patroni.yml:ro " +
        $"pg18-patroni:local " +
        $"bash -c 'mkdir -p /var/lib/postgresql/data && " +
        $"chown -R postgres:postgres /var/lib/postgresql && " +
        $"chmod 700 /var/lib/postgresql/data && " +
        $"exec gosu postgres patroni /etc/patroni.yml'";

    log.LogInformation("Run: docker {Args}", runArgs);

    var (exit, output, error) = await RunProcessAsync("docker", runArgs);
    log.LogInformation("Create replica {Name}: exit={Exit}", req.Name, exit);

    return exit == 0
        ? Results.Ok(new { success = true, containerId = output.Trim(), network = networkName })
        : Results.Json(new { success = false, error }, statusCode: 500);
});

// ── REPLICA REMOVE (docker stop + rm) ──────────────────
app.MapPost("/internal/replicas/{name}/remove", async (string name, ILogger<Program> log) =>
{
    if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^pg\d+$"))
        return Results.BadRequest(new { error = "Ad yalnız 'pg' + rəqəm olmalıdır" });

    // pg1 və pg2 silinə bilməz (statik node-lar)
    if (name == "pg1" || name == "pg2")
        return Results.BadRequest(new { error = "pg1 və pg2 silinə bilməz" });

    // 1) Volume adını əvvəlcədən tap
    var (volExit, volOut, _) = await RunProcessAsync(
        "docker",
        $"inspect {name} --format \"{{{{ range .Mounts }}}}{{{{ if eq .Destination \\\"/var/lib/postgresql/data\\\" }}}}{{{{ .Name }}}}{{{{ end }}}}{{{{ end }}}}\" 2>/dev/null");

    var volumeName = volExit == 0 ? volOut.Trim() : "";

    // 2) Stop
    await RunProcessAsync("docker", $"stop {name}");
    // 3) Remove
    var (exit, output, error) = await RunProcessAsync("docker", $"rm -f {name}");

    log.LogInformation(
        "Remove replica {Name}: exit={Exit}, volume={Vol}",
        name, exit, volumeName);

    return exit == 0
        ? Results.Ok(new { success = true, volume = volumeName })
        : Results.Json(new { success = false, error }, statusCode: 500);
});

// ── VOLUME REMOVE (ayrıca endpoint) ────────────────────
app.MapPost("/internal/volumes/{name}/remove", async (string name, ILogger<Program> log) =>
{
    // Təhlükəsizlik: yalnız "_data" ilə bitənlər
    if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_]+_pg\d+_data$"))
        return Results.BadRequest(new { error = "Volume adı təhlükəsiz formatda deyil" });

    var (exit, output, error) = await RunProcessAsync("docker", $"volume rm {name}");
    log.LogInformation("Remove volume {Name}: exit={Exit}", name, exit);

    return exit == 0
        ? Results.Ok(new { success = true })
        : Results.Json(new { success = false, error }, statusCode: 500);
});

// ── PATRONICTL SWITCHOVER ──────────────────────────────
app.MapPost("/internal/switchover", async (
    SwitchoverReq req, ILogger<Program> log) =>
{
    if (!System.Text.RegularExpressions.Regex.IsMatch(req.Leader, @"^pg\d+$") ||
        !System.Text.RegularExpressions.Regex.IsMatch(req.Candidate, @"^pg\d+$"))
        return Results.BadRequest(new { error = "Adlar 'pgN' formatında olmalıdır" });

    if (req.Leader == req.Candidate)
        return Results.BadRequest(new { error = "Leader və candidate eyni ola bilməz" });

    var (exit, output, error) = await RunProcessAsync(
        "docker",
        $"exec -u postgres {req.Leader} " +
        $"patronictl -c /etc/patroni.yml switchover --leader={req.Leader} " +
        $"--candidate={req.Candidate} --force");

    log.LogInformation("Switchover {Leader}→{Candidate}: exit={Exit}",
        req.Leader, req.Candidate, exit);

    return exit == 0
        ? Results.Ok(new { success = true, output })
        : Results.Json(new { success = false, error }, statusCode: 500);
});

app.Run();

// ─── Köməkçi metodlar ───
static async Task<IResult> RunDockerAsync(string action, string name, ILogger log)
{
    if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^pg\d+$"))
        return Results.BadRequest(new { error = "Ad yalnız 'pg' + rəqəm olmalıdır" });

    var (exit, output, error) = await RunProcessAsync("docker", $"{action} {name}");
    log.LogInformation("Docker {Action} {Name}: exit={Exit}", action, name, exit);

    return exit == 0
        ? Results.Ok(new { success = true, output })
        : Results.Json(new { success = false, error }, statusCode: 500);
}

static async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(
    string file, string args)
{
    using var p = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };
    p.Start();
    var output = await p.StandardOutput.ReadToEndAsync();
    var error = await p.StandardError.ReadToEndAsync();
    await p.WaitForExitAsync();
    return (p.ExitCode, output, error);
}

record CreateReplicaReq(string Name, int HostPort);
record SwitchoverReq(string Leader, string Candidate);