using ImprivataProxy.Shared.Logging;

namespace ImprivataProxy.Tests;

public class LogSanitizerTests
{
    private readonly LogSanitizer _sanitizer = new();

    [Fact]
    public void SanitizeXml_Password_IsRedacted()
    {
        var xml = @"<Request><Password>secret123</Password></Request>";
        var result = _sanitizer.SanitizeXml(xml);
        Assert.Contains("***", result);
        Assert.DoesNotContain("secret123", result);
    }

    [Fact]
    public void SanitizeXml_AuthTicket_IsRedacted()
    {
        var xml = @"<Response><AuthTicket>abc123def456</AuthTicket></Response>";
        var result = _sanitizer.SanitizeXml(xml);
        Assert.Contains("***", result);
        Assert.DoesNotContain("abc123def456", result);
    }

    [Fact]
    public void SanitizeXml_UniqueID_IsRedacted()
    {
        var xml = @"<Request><UniqueID>cardid123</UniqueID></Request>";
        var result = _sanitizer.SanitizeXml(xml);
        Assert.Contains("***", result);
        Assert.DoesNotContain("cardid123", result);
    }

    [Fact]
    public void SanitizeXml_NonSensitiveData_IsPreserved()
    {
        var xml = @"<Response><Username>testuser</Username><Domain>corp.com</Domain></Response>";
        var result = _sanitizer.SanitizeXml(xml);
        Assert.Contains("testuser", result);
        Assert.Contains("corp.com", result);
    }

    [Fact]
    public void SanitizeXml_InvalidXml_ReturnsPlaceholder()
    {
        var result = _sanitizer.SanitizeXml("not xml content");
        Assert.Equal("[non-XML content]", result);
    }

    [Fact]
    public void SanitizeXml_NestedPassword_IsRedacted()
    {
        var xml = @"<Request>
            <ModalityAuthInput>
                <AuthRequest>
                    <PasswordVerificationRequest>
                        <Password>mypassword</Password>
                    </PasswordVerificationRequest>
                </AuthRequest>
            </ModalityAuthInput>
        </Request>";

        var result = _sanitizer.SanitizeXml(xml);
        Assert.DoesNotContain("mypassword", result);
    }

    [Fact]
    public void SanitizeHeaders_Authorization_IsRedacted()
    {
        var headers = new List<KeyValuePair<string, IEnumerable<string>>>
        {
            new("Content-Type", new[] { "text/xml" }),
            new("Authorization", new[] { "OStick ostick.ticket=secret" }),
            new("isx-product", new[] { "test-product" })
        };

        var result = _sanitizer.SanitizeHeaders(headers);
        Assert.Contains("Content-Type: text/xml", result);
        Assert.Contains("Authorization: ***", result);
        Assert.DoesNotContain("secret", result);
        Assert.Contains("isx-product: test-product", result);
    }
}
