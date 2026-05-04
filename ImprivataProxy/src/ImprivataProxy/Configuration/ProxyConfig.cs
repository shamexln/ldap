namespace ImprivataProxy.Configuration;

public class ProxyConfig
{
    public string ListenAddress { get; set; } = "127.0.0.1";
    public int ListenPort { get; set; } = 80;
}
