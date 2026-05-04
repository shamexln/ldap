using ImprivataProxy.Facades.Imprivata;

namespace ImprivataProxy.Tests;

public class ImprivataXmlParserTests
{
    [Fact]
    public void Parse_Pwd_ExtractsAllFields()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Request>
  <ModalityAuthInput modalityID=""PWD"">
    <AuthRequest>
      <PasswordVerificationRequest>
        <UserIdentity>
          <Username>alice</Username>
          <Domain>CORP</Domain>
        </UserIdentity>
        <Password>s3cret</Password>
      </PasswordVerificationRequest>
    </AuthRequest>
  </ModalityAuthInput>
  <CreateAuthTicket>true</CreateAuthTicket>
</Request>";

        var req = ImprivataXmlParser.TryParseAuthUser(xml);

        Assert.NotNull(req);
        Assert.Equal("PWD", req.ModalityId);
        Assert.Equal("alice", req.Username);
        Assert.Equal("CORP", req.Domain);
        Assert.Equal("s3cret", req.Password);
        Assert.Null(req.UniqueId);
        Assert.Null(req.Pin);
        Assert.Null(req.ServerState);
        Assert.True(req.CreateAuthTicket);
    }

    [Fact]
    public void Parse_Uid_ExtractsUniqueId()
    {
        var xml = @"<Request>
  <ModalityAuthInput modalityID=""UID"">
    <AuthRequest><UniqueID>123456789</UniqueID></AuthRequest>
  </ModalityAuthInput>
  <CreateAuthTicket>true</CreateAuthTicket>
</Request>";

        var req = ImprivataXmlParser.TryParseAuthUser(xml);

        Assert.NotNull(req);
        Assert.Equal("UID", req.ModalityId);
        Assert.Equal("123456789", req.UniqueId);
    }

    [Fact]
    public void Parse_Pin_WithServerState()
    {
        var xml = @"<Request>
  <ServerState>a1b2c3d4</ServerState>
  <ModalityAuthInput modalityID=""PIN"">
    <AuthRequest><PIN>1234</PIN></AuthRequest>
  </ModalityAuthInput>
</Request>";

        var req = ImprivataXmlParser.TryParseAuthUser(xml);

        Assert.NotNull(req);
        Assert.Equal("PIN", req.ModalityId);
        Assert.Equal("1234", req.Pin);
        Assert.Equal("a1b2c3d4", req.ServerState);
        Assert.True(req.CreateAuthTicket);          // default when absent
    }

    [Fact]
    public void Parse_CreateAuthTicketZero_ParsesAsFalse()
    {
        var xml = @"<Request>
  <ModalityAuthInput modalityID=""PWD"">
    <AuthRequest><PasswordVerificationRequest>
      <UserIdentity><Username>a</Username><Domain>D</Domain></UserIdentity>
      <Password>p</Password>
    </PasswordVerificationRequest></AuthRequest>
  </ModalityAuthInput>
  <CreateAuthTicket>0</CreateAuthTicket>
</Request>";

        var req = ImprivataXmlParser.TryParseAuthUser(xml)!;
        Assert.False(req.CreateAuthTicket);
    }

    [Fact]
    public void Parse_Malformed_ReturnsNull()
    {
        Assert.Null(ImprivataXmlParser.TryParseAuthUser(""));
        Assert.Null(ImprivataXmlParser.TryParseAuthUser("not xml"));
        Assert.Null(ImprivataXmlParser.TryParseAuthUser("<Request></Request>"));   // no ModalityAuthInput
        Assert.Null(ImprivataXmlParser.TryParseAuthUser(
            @"<Request><ModalityAuthInput/></Request>"));                           // no modalityID attr
    }
}
