namespace ImprivataProxy.Configuration;

public class TicketConfig
{
    public string SigningKeyPath { get; set; } = "./certs/ticket-signing.pem";
    public int TtlHours { get; set; } = 8;
    public string Issuer { get; set; } = "imprivata-proxy";
}
