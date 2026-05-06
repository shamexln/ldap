namespace ImprivataProxy.Facades.Imprivata;

/// <summary>
/// Imprivata ProveID Web API response codes.
/// disp: display code (0=success, 1=pending, 4=failure)
/// rtc: return code (specific reason)
/// </summary>
public static class ReturnCodes
{
    // AuthState @disp values
    public const int DispSuccess = 0;
    public const int DispPending = 1;
    public const int DispCredentialFailure = 2;
    public const int DispFailure = 4;

    // rtc values used by this proxy
    public const int RtcSuccess = 0;
    public const int RtcInvalidCredentials = 1001;
    public const int RtcAccountLocked = 1010;
    public const int RtcSessionExpired = 1020;
    public const int RtcModalityNotSupported = 1030;
    public const int RtcInvalidRequest = 1040;
    public const int RtcSystemError = 5000;
}
