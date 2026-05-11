using ImprivataProxy.Configuration;
using ImprivataProxy.Facades.Admin;
using ImprivataProxy.Facades.Contracts;
using ImprivataProxy.Facades.Imprivata;
using ImprivataProxy.IdpCore.Audit;
using ImprivataProxy.IdpCore.Authentication;
using ImprivataProxy.IdpCore.Sessions;
using ImprivataProxy.IdpCore.Tokens;
using ImprivataProxy.Middleware;
using ImprivataProxy.Shared.Contracts;
using ImprivataProxy.Shared.Http;
using ImprivataProxy.Shared.Logging;
using ImprivataProxy.Sources.ActiveDirectory;
using ImprivataProxy.Sources.Contracts;
using ImprivataProxy.Sources.Local;
using ImprivataProxy.IdpCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// ===================================================================
// Configuration bindings
// ===================================================================
builder.Services.Configure<ProxyConfig>(builder.Configuration.GetSection("Proxy"));
builder.Services.Configure<DatabaseConfig>(builder.Configuration.GetSection("Database"));
builder.Services.Configure<AdConfig>(builder.Configuration.GetSection("Ad"));
builder.Services.Configure<AuthPolicyConfig>(builder.Configuration.GetSection("AuthPolicy"));
builder.Services.Configure<TicketConfig>(builder.Configuration.GetSection("Ticket"));
builder.Services.Configure<AdminConfig>(builder.Configuration.GetSection("Admin"));

// ===================================================================
// Infrastructure (cross-cutting)
// ===================================================================
var dbConnectionString = builder.Configuration["Database:ConnectionString"]
    ?? "Data Source=./data/proxy.db";
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(dbConnectionString));

builder.Services.AddSingleton<LogSanitizer>();
builder.Services.AddHttpContextAccessor();

// ===================================================================
// Sources (Local DB + Active Directory)
// ===================================================================
builder.Services.AddScoped<IUserStore, UserStore>();
// ADR-0002 §8.2: audit / sessions / ticket-blacklist each split into a
// storage repo (Sources, holds AppDbContext) and an IdpCore policy.
builder.Services.AddScoped<IAuditStore, EfAuditStore>();
builder.Services.AddScoped<IAuthSessionRepo, EfAuthSessionRepo>();
builder.Services.AddScoped<ITicketBlacklistRepo, EfTicketBlacklistRepo>();
builder.Services.AddScoped<ILockoutRepo, EfLockoutRepo>();
builder.Services.AddScoped<IClientContextProvider, HttpClientContextProvider>();

builder.Services.AddSingleton<ILdapClient, LdapClient>();
// ADR-0002 §4.1: expose LdapClient also as IRemotePasswordVerifier (future switch point).
builder.Services.AddSingleton<IRemotePasswordVerifier>(
    sp => (IRemotePasswordVerifier)sp.GetRequiredService<ILdapClient>());
builder.Services.AddScoped<AdSyncRunner>();
// ADR-0002 §4.1: expose AdSyncRunner also as IUserDirectorySync (future SCIM swap point).
builder.Services.AddScoped<IUserDirectorySync>(sp => sp.GetRequiredService<AdSyncRunner>());
var adMode = builder.Configuration.GetValue<string>("Ad:Mode") ?? "Sync";
if (string.Equals(adMode, "Sync", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<AdSyncService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<AdSyncService>());
}

// ===================================================================
// IdpCore (protocol-agnostic authentication policy)
// ===================================================================
builder.Services.AddScoped<IAuditSink, AuditLogSink>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<ILockoutPolicy, LockoutPolicy>();
builder.Services.AddScoped<IAuthSessionStore, AuthSessionStore>();
builder.Services.AddScoped<IPwdAuthenticator, PwdAuthenticator>();
builder.Services.AddSingleton<GroupAuthorizationChecker>();
var uidMode = builder.Configuration.GetValue<string>("Ad:UidMode") ?? "Badge";
if (string.Equals(uidMode, "Badge", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddScoped<IUidAuthenticator, BadgeUidAuthenticator>();
else
    builder.Services.AddScoped<IUidAuthenticator, UidAuthenticator>();
builder.Services.AddScoped<IPinAuthenticator, PinAuthenticator>();

builder.Services.AddSingleton<ISigningKeyProvider, SigningKeyProvider>();
builder.Services.AddSingleton<ITicketIssuer, JwtTicketIssuer>();
builder.Services.AddScoped<ITicketBlacklist, TicketBlacklistService>();

// ===================================================================
// Facades (pluggable external protocols) — ADR-0002 §4.3
// ===================================================================
// Adding or removing a protocol is a one-line change here. Each facade owns
// its own auth scheme + HTTP routes; Program.cs stays protocol-agnostic.
IProtocolFacade[] facades =
{
    new ImprivataFacade(),
    new AdminFacade(),
    // new SamlFacade(),   // future: browser-based SSO
    // new OidcFacade(),   // future: mobile / REST clients
};

foreach (var f in facades) f.RegisterServices(builder.Services, builder.Configuration);
builder.Services.AddAuthorization();

// ===================================================================
// Build app
// ===================================================================
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseCors(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

// Facade-owned routes
foreach (var f in facades) f.MapEndpoints(app);

// Infra-level endpoint (not protocol-specific)
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// SPA fallback: serve index.html for any unmatched route (Vue Router handles client-side routing)
app.MapFallbackToFile("index.html");

app.Run();

// Make Program discoverable to WebApplicationFactory in integration tests.
public partial class Program { }
