using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

var controllerToken = builder.Configuration["Controller:Token"]
    ?? throw new InvalidOperationException("Controller:Token təyin olunmayıb");

var app = builder.Build();

// ── Autentifikasiya middleware ─────────────────────────
app.Use(async (ctx, next) =>
{
    // Yalnız /internal/* endpoint-ləri qorunur
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

app.MapGet("/health", () => new { status = "ok" });

// ── NODE START ─────────────────────────────────────────
app.MapPost("/internal/nodes/{name}/start", async (string name, ILogger<Program> log) =>
{
    return await RunDockerAsync("start", name, log);
});

// ── NODE STOP ──────────────────────────────────────────
app.MapPost("/internal/nodes/{name}/stop", async (string name, ILogger<Program> log) =>
{
    return await RunDockerAsync("stop", name, log);
});

// ── NODE RESTART ───────────────────────────────────────
app.MapPost("/internal/nodes/{name}/restart", async (string name, ILogger<Program> log) =>
{
    return await RunDockerAsync("restart", name, log);
});

// ── REPLICA CREATE (docker compose up) ─────────────────
app.MapPost("/internal/replicas/create", async (
    CreateReplicaReq req, ILogger<Program> log) =>
{
    // Yalnız icazəli adlar
    if (!System.Text.RegularExpressions.Regex.IsMatch(req.Name, @"^pg\d+$"))
        return Results.BadRequest(new { error = "Ad yalnız 'pg' + rəqəm olmalıdır" });

    if (!System.Text.RegularExpressions.Regex.IsMatch(req.HostPort.ToString(), @"^\d+$"))
        return Results.BadRequest(new { error = "HostPort rəqəm olmalıdır" });

    // docker compose -f /compose/docker-compose.yml up -d <name>
    var (exit, output, error) = await RunProcessAsync(
        "docker",
        $"compose -f /compose/docker-compose.yml up -d {req.Name}");

    log.LogInformation("Create replica {Name}: exit={Exit}", req.Name, exit);

    return exit == 0
        ? Results.Ok(new { success = true, output })
        : Results.Json(new { success = false, error }, statusCode: 500);
});

// ── REPLICA REMOVE (docker compose rm) ─────────────────
app.MapPost("/internal/replicas/{name}/remove", async (string name, ILogger<Program> log) =>
{
    if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^pg\d+$"))
        return Results.BadRequest(new { error = "Ad yalnız 'pg' + rəqəm olmalıdır" });

    var (exit, output, error) = await RunProcessAsync(
        "docker",
        $"compose -f /compose/docker-compose.yml rm -sf {name}");

    log.LogInformation("Remove replica {Name}: exit={Exit}", name, exit);

    return exit == 0
        ? Results.Ok(new { success = true, output })
        : Results.Json(new { success = false, error }, statusCode: 500);
});

// ── PATRONICTL SWITCHOVER ──────────────────────────────
app.MapPost("/internal/switchover", async (
    SwitchoverReq req, ILogger<Program> log) =>
{
    if (!System.Text.RegularExpressions.Regex.IsMatch(req.Leader, @"^pg\d+$") ||
        !System.Text.RegularExpressions.Regex.IsMatch(req.Candidate, @"^pg\d+$"))
        return Results.BadRequest(new { error = "Adlar 'pgN' formatında olmalıdır" });

    var (exit, output, error) = await RunProcessAsync(
        "docker",
        $"exec -u postgres {req.Leader} " +
        $"patronictl -c /etc/patroni.yml switchover --leader={req.Leader} " +
        $"--candidate={req.Candidate} --force --scheduled=now");

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