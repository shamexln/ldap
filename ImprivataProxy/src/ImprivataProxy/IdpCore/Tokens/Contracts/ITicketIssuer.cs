using ImprivataProxy.Sources.Local.Entities;

namespace ImprivataProxy.IdpCore.Tokens;

public interface ITicketIssuer
{
    /// <summary>
    /// Issue an OStick-compatible opaque ticket string for the given user.
    /// Phase 3 uses a random placeholder; Phase 4 replaces with a signed JWT.
    /// </summary>
    string Issue(User user);
}
