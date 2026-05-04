namespace ImprivataProxy.Facades.Imprivata;

/// <summary>
/// Parser for the Imprivata OStick Authorization header:
///   Authorization: OStick ostick.ticket=&lt;JWT&gt;
/// The scheme name is case-insensitive; the "ostick.ticket=" key must be present.
/// </summary>
public static class OStickHeader
{
    public const string Scheme = "OStick";
    private const string TicketKey = "ostick.ticket=";

    /// <summary>Builds the full header value for a given ticket.</summary>
    public static string Format(string ticket) => $"{Scheme} {TicketKey}{ticket}";

    /// <summary>
    /// Extracts the raw JWT ticket from an Authorization header value.
    /// Returns null on any malformed input.
    /// </summary>
    public static string? TryExtractTicket(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue)) return null;

        var trimmed = headerValue.TrimStart();
        var space = trimmed.IndexOf(' ');
        if (space <= 0) return null;

        var scheme = trimmed[..space];
        if (!scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase)) return null;

        var rest = trimmed[(space + 1)..].TrimStart();
        // Allow "ostick.ticket=<jwt>" possibly followed by other parameters; take only the ticket value.
        if (!rest.StartsWith(TicketKey, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var value = rest[TicketKey.Length..];

        // If params are comma or semicolon separated, stop at the first delimiter.
        var end = value.IndexOfAny(new[] { ',', ';', ' ' });
        if (end >= 0) value = value[..end];

        // Strip surrounding quotes if any.
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        return string.IsNullOrEmpty(value) ? null : value;
    }
}
