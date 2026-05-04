namespace ImprivataProxy.Shared.Contracts;

/// <summary>
/// Which credential the lockout / rate-limit applies to.
/// Lives in <c>Shared.Contracts</c> because both IdpCore (lockout policy)
/// and Sources (lockout repo) reference it — ADR-0002 §8.3 forbids Sources
/// from depending on IdpCore, so the enum needs a layer-neutral home.
/// </summary>
public enum PwdOrPin
{
    Pwd,
    Pin,
}
