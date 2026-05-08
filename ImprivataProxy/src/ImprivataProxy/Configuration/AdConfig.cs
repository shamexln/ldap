namespace ImprivataProxy.Configuration;

public class AdConfig
{
    public string Mode { get; set; } = "Sync";
    public string LdapUrl { get; set; } = "ldaps://localhost:636";
    public string BaseDn { get; set; } = "";
    public string LoginAttribute { get; set; } = "sAMAccountName";
    public string ServiceAccountDn { get; set; } = "";
    public string ServiceAccountPasswordEnvVar { get; set; } = "AD_SVC_PASSWORD";
    public int SyncIntervalMinutes { get; set; } = 30;
    public int BindTimeoutSeconds { get; set; } = 10;
    public int SearchTimeoutSeconds { get; set; } = 30;
    public int PageSize { get; set; } = 1000;
    public bool SkipCertValidation { get; set; } = false;
}
