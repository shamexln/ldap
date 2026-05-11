namespace ImprivataProxy.IdpCore.Authorization;

public class GroupAuthorizationChecker
{
    public bool IsAuthorized(IReadOnlyList<string> userGroups, string[] requiredGroups)
    {
        if (requiredGroups.Length == 0) return true;
        return userGroups.Any(g => requiredGroups.Contains(g, StringComparer.OrdinalIgnoreCase));
    }
}
