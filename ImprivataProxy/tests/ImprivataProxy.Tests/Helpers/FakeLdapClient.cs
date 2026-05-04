using ImprivataProxy.Sources.ActiveDirectory;

namespace ImprivataProxy.Tests.Helpers;

/// <summary>
/// Reusable fake ILdapClient for unit and integration tests.
/// </summary>
public class FakeLdapClient : ILdapClient
{
    public Dictionary<(string dn, string pwd), bool> BindResults { get; } = new();
    public List<AdUserDto> Users { get; } = new();
    public Exception? ThrowOnBind { get; set; }
    public Exception? ThrowOnSearch { get; set; }

    public Task<bool> BindAsUserAsync(string userDn, string password, CancellationToken ct)
    {
        if (ThrowOnBind is not null) throw ThrowOnBind;
        return Task.FromResult(BindResults.GetValueOrDefault((userDn, password), false));
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
