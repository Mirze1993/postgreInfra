using System.Diagnostics;

namespace PgControl.Api.Services;

public class FlywayService
{
    private readonly ILogger<FlywayService> _logger;
    private readonly string _stagingConn;
    private readonly string _productionConn;

    public FlywayService(IConfiguration cfg, ILogger<FlywayService> logger)
    {
        _logger = logger;
        _stagingConn = cfg["Flyway:StagingConnection"]!;
        _productionConn = cfg["Flyway:ProductionConnection"]!;
    }

    public async Task<FlywayResult> ValidateAndMigrateStagingAsync(string migrationDir)
    {
        // 1) validate — mövcud schema_history ilə uyğunluğu yoxla
        var (vExit, vOut, vErr) = await RunFlywayAsync(
            $"validate -url={_stagingConn} -locations=filesystem:{migrationDir}");
        if (vExit != 0)
            return FlywayResult.Fail("validate", vOut + vErr);

        // 2) migrate — staging-də tətbiq et
        var (mExit, mOut, mErr) = await RunFlywayAsync(
            $"migrate -url={_stagingConn} -locations=filesystem:{migrationDir}");
        if (mExit != 0)
            return FlywayResult.Fail("migrate", mOut + mErr);

        return FlywayResult.Ok(mOut);
    }

    public async Task<FlywayResult> MigrateProductionAsync(string migrationDir)
    {
        // Production-da yalnız migrate (validate staging-də artıq keçib)
        var (exit, output, err) = await RunFlywayAsync(
            $"migrate -url={_productionConn} -locations=filesystem:{migrationDir}");
        return exit == 0 ? FlywayResult.Ok(output) : FlywayResult.Fail("migrate", output + err);
    }

    public async Task<FlywayInfo> GetInfoAsync(string connectionString, string migrationDir)
    {
        var (exit, output, err) = await RunFlywayAsync(
            $"info -url={connectionString} -locations=filesystem:{migrationDir}");
        return new FlywayInfo { Success = exit == 0, Output = output + err };
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunFlywayAsync(string args)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
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

public class FlywayResult
{
    public bool Success { get; set; }
    public string Phase { get; set; } = "";
    public string Output { get; set; } = "";

    public static FlywayResult Ok(string output) => new() { Success = true, Phase = "ok", Output = output };
    public static FlywayResult Fail(string phase, string output) => new() { Success = false, Phase = phase, Output = output };
}

public class FlywayInfo
{
    public bool Success { get; set; }
    public string Output { get; set; } = "";
}