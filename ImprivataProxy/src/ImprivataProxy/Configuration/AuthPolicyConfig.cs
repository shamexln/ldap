namespace ImprivataProxy.Configuration;

public class AuthPolicyConfig
{
    public int PwdMaxFails { get; set; } = 5;
    public int PwdLockoutMinutes { get; set; } = 15;
    public int PinMaxFails { get; set; } = 3;
    public int PinLockoutMinutes { get; set; } = 15;
    public int PwdHashTtlDays { get; set; } = 7;
    public int AuthSessionTtlSeconds { get; set; } = 60;
}
