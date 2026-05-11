using System.Text.Json;
using ImprivataProxy.Sources.Contracts;
using ImprivataProxy.Sources.Local;
using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.Sources.Local.Entities;
using ImprivataProxy.IdpCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ImprivataProxy.Facades.Admin;

[ApiController]
[Route("admin/users")]
[Authorize(AuthenticationSchemes = AdminAuthenticationHandler.SchemeName)]
public class UsersController : ControllerBase
{
    private readonly IUserStore _users;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditSink _audit;

    public UsersController(IUserStore users, IPasswordHasher hasher, IAuditSink audit)
    {
        _users = users;
        _hasher = hasher;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] bool? enabled,
        [FromQuery] int take = 100,
        CancellationToken ct = default)
    {
        var rows = await _users.ListUsersAsync(search, enabled, take, ct);
        var items = rows.Select(u => new UserListItemDto(
            u.Id, u.Username, u.Domain, u.DisplayName,
            u.GivenName, u.Sn,
            u.Enabled, u.PinHash != null, u.Cards.Count,
            u.PwdLockedUntil, u.PinLockedUntil, u.LastSyncedAt,
            ParseGroups(u.AttributesJson)))
            .ToList();

        return Ok(items);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var user = await _users.FindByIdWithCardsAsync(id, ct);
        if (user is null) return NotFound();

        return Ok(ToDetail(user));
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Patch(string id, [FromBody] PatchUserDto dto, CancellationToken ct)
    {
        if (dto.Enabled is null) return NoContent();

        var ok = await _users.SetUserEnabledAsync(id, dto.Enabled.Value, ct);
        if (!ok) return NotFound();

        var user = await _users.FindByIdAsync(id, ct);
        await _audit.LogAsync("admin_user_patch",
            user?.Username, user?.Domain,
            detail: new { enabled = dto.Enabled }, ct: ct);

        return NoContent();
    }

    [HttpPut("{id}/pin")]
    public async Task<IActionResult> SetPin(string id, [FromBody] SetPinDto dto, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(dto.Pin) || dto.Pin.Length < 4)
        {
            return BadRequest(new { error = "PIN must be at least 4 characters" });
        }

        var hash = _hasher.Hash(dto.Pin);
        var ok = await _users.SetPinHashAsync(id, hash, ct);
        if (!ok) return NotFound();

        var user = await _users.FindByIdAsync(id, ct);
        await _audit.LogAsync("admin_pin_set", user?.Username, user?.Domain, ct: ct);

        return NoContent();
    }

    [HttpDelete("{id}/pin")]
    public async Task<IActionResult> ClearPin(string id, CancellationToken ct)
    {
        var ok = await _users.SetPinHashAsync(id, null, ct);
        if (!ok) return NotFound();

        var user = await _users.FindByIdAsync(id, ct);
        await _audit.LogAsync("admin_pin_clear", user?.Username, user?.Domain, ct: ct);

        return NoContent();
    }

    [HttpPost("{id}/unlock")]
    public async Task<IActionResult> Unlock(string id, CancellationToken ct)
    {
        var ok = await _users.UnlockUserAsync(id, ct);
        if (!ok) return NotFound();

        var user = await _users.FindByIdAsync(id, ct);
        await _audit.LogAsync("admin_unlock", user?.Username, user?.Domain, ct: ct);

        return NoContent();
    }

    private static UserDetailDto ToDetail(User u) => new(
        u.Id, u.Username, u.Domain, u.DisplayName,
        u.GivenName, u.Sn,
        ParseStringAttr(u.AttributesJson, "upn"),
        u.AdDistinguishedName, u.AdObjectGuid,
        u.Enabled, u.PinHash != null,
        u.PwdFailCount, u.PinFailCount,
        u.PwdLockedUntil, u.PinLockedUntil,
        u.PwdHashUpdatedAt, u.LastSyncedAt,
        ParseGroups(u.AttributesJson),
        u.Cards.Select(c => new CardDto(
            c.Id, c.UserId, c.Label, c.CardUidLast4,
            c.IssuedAt, c.ExpiresAt, c.Revoked)).ToList());

    private static IReadOnlyList<string>? ParseGroups(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("groups", out var arr) ||
                arr.ValueKind != JsonValueKind.Array) return null;
            return arr.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }
        catch { return null; }
    }

    private static string? ParseStringAttr(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(key, out var prop) &&
                prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }
        catch { }
        return null;
    }
}
