namespace ImprivataProxy.Configuration;

public class AdminConfig
{
    public string Username { get; set; } = "admin";
    public string PasswordEnvVar { get; set; } = "ADMIN_PASSWORD";
    public string Realm { get; set; } = "imprivata-proxy-admin";
}
