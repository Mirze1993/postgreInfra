using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PgControl.Api.Services;

namespace PgControl.Api.Controllers;

[ApiController]
[Route("api/cluster-operations")]
[Authorize]
public class ClusterOperationsController : ControllerBase
{
    private readonly ClusterOperationService _svc;
    private readonly PatroniService _patroni;

    public ClusterOperationsController(ClusterOperationService svc, PatroniService patroni)
    {
        _svc = svc;
        _patroni = patroni;
    }

    private string CurrentUser => User.Identity?.Name ?? "unknown";

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await _svc.ListAsync());

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var op = await _svc.GetAsync(id);
        return op == null ? NotFound() : Ok(op);
    }

    // ── REPLICA ADD ──────────────────────────────────────
    [HttpPost("replicas/add")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AddReplica()
    {
        try
        {
            var id = await _svc.RequestAddReplicaAsync(CurrentUser);
            return Accepted(new { operationId = id });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ── REPLICA REMOVE ───────────────────────────────────
    public record RemoveReplicaReq(string NodeName, string Confirmation);

    [HttpPost("replicas/remove")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RemoveReplica([FromBody] RemoveReplicaReq req)
    {
        if (req.Confirmation != "REMOVE")
            return BadRequest(new { error = "Təsdiq üçün 'REMOVE' yazın" });

        try
        {
            var id = await _svc.RequestRemoveReplicaAsync(req.NodeName, CurrentUser);
            return Accepted(new { operationId = id });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ── SWITCHOVER ───────────────────────────────────────
    public record SwitchoverReq(string Leader, string Candidate, string Confirmation);

    [HttpPost("switchover")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Switchover([FromBody] SwitchoverReq req)
    {
        if (req.Confirmation != "SWITCHOVER")
            return BadRequest(new { error = "Təsdiq üçün 'SWITCHOVER' yazın" });

        try
        {
            var id = await _svc.RequestSwitchoverAsync(req.Leader, req.Candidate, CurrentUser);
            return Accepted(new { operationId = id });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ── NODE RESTART ─────────────────────────────────────
    [HttpPost("nodes/{name}/restart")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RestartNode(string name)
    {
        // Sadə birbaşa əməliyyat (background job olmadan)
        var controller = HttpContext.RequestServices.GetRequiredService<DockerControllerClient>();
        var result = await controller.RestartNodeAsync(name);
        return result.Success ? Ok(new { success = true }) : StatusCode(500, new { error = result.Error });
    }
}