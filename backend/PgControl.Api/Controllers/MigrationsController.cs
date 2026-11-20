using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PgControl.Api.Services;

namespace PgControl.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MigrationsController : ControllerBase
{
    private readonly MigrationScriptService _svc;
    public MigrationsController(MigrationScriptService svc) => _svc = svc;

    private string CurrentUser => User.Identity?.Name ?? "unknown";

    // ── LIST ──
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status)
        => Ok(await _svc.ListAsync(status));

    // ── GET by id ──
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var s = await _svc.GetByIdAsync(id);
        return s == null ? NotFound() : Ok(s);
    }

    // ── CREATE (draft) ──
    public record CreateReq(string Version, string Description, string SqlContent, string? RollbackSql, string? Notes);

    [HttpPost]
    [Authorize(Roles = "Admin,Operator")]
    public async Task<IActionResult> Create([FromBody] CreateReq req)
    {
        try
        {
            var s = await _svc.CreateAsync(req.Version, req.Description,
                req.SqlContent, req.RollbackSql, CurrentUser);
            return Ok(s);
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
        {
            return BadRequest(new { error = $"'{req.Version}' versiyası artıq mövcuddur" });
        }
    }

    // ── SUBMIT (validasiya başlat) ──
    [HttpPost("{id:int}/submit")]
    [Authorize(Roles = "Admin,Operator")]
    public async Task<IActionResult> Submit(int id)
    {
        try
        {
            await _svc.SubmitAsync(id, CurrentUser);
            return Ok(new { message = "Validasiya başladı" });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ── APPROVE ──
    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Approve(int id)
    {
        try
        {
            await _svc.ApproveAsync(id, CurrentUser);
            return Ok(new { message = "Təsdiqləndi" });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ── APPLY (production) ──
    [HttpPost("{id:int}/apply")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Apply(int id)
    {
        try
        {
            await _svc.ApplyAsync(id, CurrentUser);
            return Ok(new { message = "Production-a tətbiq edildi" });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ── REJECT ──
    public record RejectReq(string Reason);

    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectReq req)
    {
        try
        {
            await _svc.RejectAsync(id, CurrentUser, req.Reason);
            return Ok(new { message = "Rədd edildi" });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ── AUDIT ──
    [HttpGet("audit")]
    public async Task<IActionResult> Audit([FromQuery] int? migrationId)
        => Ok(await _svc.GetAuditLogAsync(migrationId));
}