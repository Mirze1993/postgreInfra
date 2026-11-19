using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PgControl.Api.Services;

namespace PgControl.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // JWT tələb olunur
public class ClusterController : ControllerBase
{
    private readonly PatroniService _patroni;
    private readonly PostgresMetricsService _metrics;

    public ClusterController(PatroniService patroni, PostgresMetricsService metrics)
    {
        _patroni = patroni;
        _metrics = metrics;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var cluster = await _patroni.GetClusterStatusAsync();
        var replication = await _metrics.GetReplicationMetricsAsync();

        return Ok(new
        {
            cluster.LeaderExists,
            cluster.LeaderName,
            cluster.HasQuorum,
            Nodes = cluster.Nodes.Select(n => new
            {
                n.Url,
                n.State,
                n.Role,
                n.Timeline
            }),
            Replication = new
            {
                replication.HasReplica,
                replication.ReplayLagSeconds,
                replication.State,
                replication.SyncState
            }
        });
    }

    [HttpGet("nodes")]
    public async Task<IActionResult> GetNodes()
    {
        var members = await _patroni.GetClusterMembersAsync();
        return Ok(members?.Members ?? new List<PatroniMember>());
    }

    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics()
    {
        var replication = await _metrics.GetReplicationMetricsAsync();
        var connections = await _metrics.GetConnectionMetricsAsync();
        var wal = await _metrics.GetWalArchiveMetricsAsync();

        return Ok(new { replication, connections, wal , replication.ReplayLagBytes });
    }
}