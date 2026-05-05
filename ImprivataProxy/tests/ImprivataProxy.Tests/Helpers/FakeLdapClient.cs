using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.Sources.ActiveDirectory;
using ImprivataProxy.Sources.Contracts;

namespace ImprivataProxy.Tests.Helpers;

/// <summary>
/// Reusable fake for integration tests. Implements BOTH <see cref="ILdapClient"/>
/// (for AD sync tests — exposes <see cref="Users"/>) and
/// <see cref="IRemotePasswordVerifier"/> (for PWD authentication tests — exposes
/// <see cref="VerifyResults"/> keyed by (DN, password)). The same single
/// instance is registered twice in the DI container by IntegrationAppFactory.
/// </summary>
public class FakeLdapClient : ILdapClient, IRemotePasswordVerifier
{
    /// <summary>Maps (distinguishedName, password) → outcome. Missing keys default to <see cref="RemoteVerifyOutcome.Invalid"/>.</summary>
    public Dictionary<(string dn, string pwd), RemoteVerifyOutcome> VerifyResults { get; } = new();

    /// <summary>Users yielded by <see cref="SearchAllUsersAsync"/>.</summary>
    public List<AdUserDto> Users { get; } = new();

    public Exception? ThrowOnSearch { get; set; }

    public Task<RemoteVerifyResult> VerifyAsync(
        UserIdentity identity, string password, CancellationToken ct)
    {
        var dn = identity.DistinguishedName ?? "";
        var outcome = VerifyResults.GetValueOrDefault((dn, password), RemoteVerifyOutcome.Invalid);
        return Task.FromResult(new RemoteVerifyResult(outcome));
    }

    public async IAsyncEnumerable<AdUserDto> SearchAllUsersAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (ThrowOnSearch is not null) throw ThrowOnSearch;
        foreach (var u in Users)
        {
            yield return u;
            await Task.Yield();
        }
    }
}
