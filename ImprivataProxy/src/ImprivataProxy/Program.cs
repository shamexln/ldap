using ImprivataProxy.Sources.Local;
using ImprivataProxy.Sources.Contracts;
using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.IdpCore.Audit;
using ImprivataProxy.Sources.ActiveDirectory;
using ImprivataProxy.Facades.Admin;
using ImprivataProxy.IdpCore.Authentication;
using ImprivataProxy.IdpCore.Sessions;
using ImprivataProxy.Configuration;
using ImprivataProxy.Facades.Imprivata;
using ImprivataProxy.Shared.Http;
using ImprivataProxy.Shared.Logging;
using ImprivataProxy.Middleware;
using ImprivataProxy.IdpCore.Tokens;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.Configure<ProxyConfig>(builder.Configuration.GetSection("Proxy"));
builder.Services.Configure<DatabaseConfig>(builder.Configuration.GetSection("Database"));
builder.Services.Configure<AdConfig>(builder.Configuration.GetSection("Ad"));
builder.Services.Configure<AuthPolicyConfig>(builder.Configuration.GetSection("AuthPolicy"));
builder.Services.Configure<TicketConfig>(builder.Configuration.GetSection("Ticket"));
builder.Services.Configure<AdminConfig>(builder.Configuration.GetSection("Admin"));

var dbConnectionString = builder.Configuration["Database:ConnectionString"]
    ?? "Data Source=./data/proxy.db";
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(dbConnectionString));

builder.Services.AddSingleton<LogSanitizer>();
builder.Services.AddHttpContextAccessor();

// Accounts
builder.Services.AddScoped<IUserStore, UserStore>();
// ADR-0002 §8.2 fix: audit / sessions / ticket blacklist each split into
// storage repo (Sources/Local, uses AppDbContext) and IdpCore-level policy.
builder.Services.AddScoped<IAuditStore, EfAuditStore>();
builder.Services.AddScoped<IAuthSessionRepo, EfAuthSessionRepo>();
builder.Services.AddScoped<ITicketBlacklistRepo, EfTicketBlacklistRepo>();
builder.Services.AddScoped<IClientContextProvider, HttpClientContextProvider>();
builder.Services.AddScoped<IAuditLogger, EfAuditLogger>();

// Active Directory
builder.Services.AddSingleton<ILdapClient, LdapClient>();
// ADR-0002 §4.1: expose LdapClient also as IRemotePasswordVerifier (future switch point).
builder.Services.AddSingleton<IRemotePasswordVerifier>(
    sp => (IRemotePasswordVerifier)sp.GetRequiredService<ILdapClient>());
builder.Services.AddScoped<AdSyncRunner>();
// ADR-0002 §4.1: expose AdSyncRunner also as IUserDirectorySync (future SCIM swap point).
builder.Services.AddScoped<IUserDirectorySync>(
    sp => sp.GetRequiredService<AdSyncRunner>());
builder.Services.AddSingleton<AdSyncService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AdSyncService>());

// Authentication
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IAuthSessionStore, AuthSessionStore>();
builder.Services.AddScoped<IPwdAuthenticator, PwdAuthenticator>();
builder.Services.AddScoped<IUidAuthenticator, UidAuthenticator>();
builder.Services.AddScoped<IPinAuthenticator, PinAuthenticator>();

// Tickets (JWT)
builder.Services.AddSingleton<ISigningKeyProvider, SigningKeyProvider>();
builder.Services.AddSingleton<ITicketIssuer, JwtTicketIssuer>();
builder.Services.AddScoped<ITicketBlacklist, TicketBlacklistService>();

// Authentication schemes: OStick (Imprivata clients) + Admin (management API)
builder.Services
    .AddAuthentication(OStickAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, OStickAuthenticationHandler>(
        OStickAuthenticationHandler.SchemeName, _ => { })
    .AddScheme<AuthenticationSchemeOptions, AdminAuthenticationHandler>(
        AdminAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();

builder.Services.AddControllers();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

// Imprivata protocol endpoints
app.MapPost("/sso/ProveIDWeb/v{version:int}/AuthUser", AuthUserEndpoint.PostAsync);
app.MapGet("/sso/ProveIDWeb/v{version:int}/AuthUser", AuthUserEndpoint.GetAsync)
   .RequireAuthorization();
app.MapMethods("/sso/ProveIDWeb/v{version:int}/AuthUser", new[] { "CANCEL" }, AuthUserEndpoint.CancelAsync)
   .RequireAuthorization();

// Discovery endpoints (public; clients call these before login)
app.MapGet("/sso/ProveIDWeb/v{version:int}/Servers", ServersEndpoint.GetAsync);
app.MapGet("/sso/ProveIDWeb/v{version:int}/Domains", DomainsEndpoint.GetAsync);
app.MapGet("/sso/ProveIDWeb/v{version:int}/Modalities", ModalitiesEndpoint.GetAsync);

// Deliberately-unimplemented resources: any method → 501 with Imprivata-style body.
var unimplementedResources = new[]
{
    "Password", "Enrollment", "Multi", "VdiAccess",
    "ConfigObject", "SAMLArtifact", "UserAppCreds"
};
var anyMethod = new[] { "GET", "POST", "PUT", "DELETE", "PATCH", "CANCEL" };
foreach (var res in unimplementedResources)
{
    app.MapMethods(
        $"/sso/ProveIDWeb/v{{version:int}}/{res}/{{**catchall}}",
        anyMethod,
        NotImplementedEndpoint.HandleAsync);
    app.MapMethods(
        $"/sso/ProveIDWeb/v{{version:int}}/{res}",
        anyMethod,
        NotImplementedEndpoint.HandleAsync);
}

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.Run();

// Make Program discoverable to WebApplicationFactory in integration tests.
public partial class Program { }
