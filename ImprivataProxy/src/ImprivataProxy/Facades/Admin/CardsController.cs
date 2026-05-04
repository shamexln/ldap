using ImprivataProxy.Sources.Contracts;
using ImprivataProxy.Sources.Local;
using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.Sources.Local.Entities;
using ImprivataProxy.IdpCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ImprivataProxy.Facades.Admin;

[ApiController]
[Route("admin/cards")]
[Authorize(AuthenticationSchemes = AdminAuthenticationHandler.SchemeName)]
public class CardsController : ControllerBase
{
    private readonly IUserStore _users;
    private readonly IAuditLogger _audit;

    public CardsController(IUserStore users, IAuditLogger audit)
    {
        _users = users;
        _audit = audit;
    }

    [HttpPost]
    public async Task<IActionResult> Issue([FromBody] IssueCardDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.UserId) || string.IsNullOrWhiteSpace(dto.CardUid))
        {
            return BadRequest(new { error = "userId and cardUid are required" });
        }

        var user = await _users.FindByIdAsync(dto.UserId, ct);
        if (user is null) return NotFound(new { error = "user not found" });

        var hash = CardHasher.Hash(dto.CardUid);

        // Uniqueness is enforced by a unique index on CardUidHash; catch that up-front
        // so we can return a clean error instead of DbUpdateException.
        var existing = await _users.FindCardByHashAsync(hash, ct);
        if (existing is not null)
        {
            return Conflict(new { error = "card already enrolled", cardId = existing.Id });
        }

        var card = new UserCard
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user.Id,
            CardUidHash = hash,
            CardUidLast4 = CardHasher.Last4(dto.CardUid),
            Label = dto.Label,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = dto.ExpiresAt,
            Revoked = false,
        };
        await _users.CreateCardAsync(card, ct);

        await _audit.LogAsync("admin_card_issue",
            user.Username, user.Domain,
            detail: new { cardId = card.Id, last4 = card.CardUidLast4 }, ct: ct);

        return CreatedAtAction(
            nameof(Get), new { id = card.Id },
            new CardDto(card.Id, card.UserId, card.Label, card.CardUidLast4,
                card.IssuedAt, card.ExpiresAt, card.Revoked));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var card = await _users.FindCardByIdWithUserAsync(id, ct);
        if (card is null) return NotFound();
        return Ok(new CardDto(card.Id, card.UserId, card.Label, card.CardUidLast4,
            card.IssuedAt, card.ExpiresAt, card.Revoked));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Revoke(string id, CancellationToken ct)
    {
        var card = await _users.FindCardByIdWithUserAsync(id, ct);
        if (card is null) return NotFound();

        var ok = await _users.RevokeCardAsync(id, ct);
        if (!ok) return NotFound();

        await _audit.LogAsync("admin_card_revoke",
            card.User?.Username, card.User?.Domain,
            detail: new { cardId = card.Id, last4 = card.CardUidLast4 }, ct: ct);

        return NoContent();
    }
}
