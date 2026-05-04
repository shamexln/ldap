using ImprivataProxy.Sources.Local.Entities;
using ImprivataProxy.IdpCore.Tokens;

namespace ImprivataProxy.Tests.Helpers;

/// <summary>
/// Minimal in-test ticket issuer. Produces a deterministic-ish opaque string.
/// Tests asserting "ticket is non-empty" only; they should not depend on content.
/// </summary>
public sealed class FakeTicketIssuer : ITicketIssuer
{
    public List<User> Issued { get; } = new();

    public string Issue(User user)
    {
        Issued.Add(user);
        return $"fake-ticket-for-{user.Id}";
    }
}
