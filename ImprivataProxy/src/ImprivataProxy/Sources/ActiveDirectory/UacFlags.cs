namespace ImprivataProxy.Sources.ActiveDirectory;

/// <summary>
/// Subset of Active Directory userAccountControl bit flags.
/// Full reference: https://learn.microsoft.com/windows/win32/adschema/a-useraccountcontrol
/// </summary>
public static class UacFlags
{
    public const int ACCOUNTDISABLE = 0x0002;
    public const int LOCKOUT = 0x0010;
    public const int PASSWD_NOTREQD = 0x0020;
    public const int NORMAL_ACCOUNT = 0x0200;

    public static bool IsDisabled(int uac) => (uac & ACCOUNTDISABLE) != 0;

    /// <summary>
    /// Returns true if the account is considered enabled (not disabled).
    /// Locked-out is NOT the same as disabled for our purposes.
    /// </summary>
    public static bool IsEnabled(int uac) => !IsDisabled(uac);
}
