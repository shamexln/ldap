using ImprivataProxy.Facades.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ImprivataProxy.Facades.Imprivata;

/// <summary>
/// Imprivata ProveID Web API v{version} adapter. Exposes the XML-over-HTTP protocol
/// that Imprivata clients (EWS etc.) speak, authenticated via the OStick JWT scheme.
/// </summary>
public class ImprivataFacade : IProtocolFacade
{
    public string Name => "Imprivata";

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        // OStick is the *default* authentication scheme for this application —
        // Imprivata clients are the primary HTTP surface. Other facades add their
        // own schemes on top via AddAuthentication() without a default name.
        services.AddAuthentication(OStickAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, OStickAuthenticationHandler>(
                    OStickAuthenticationHandler.SchemeName, _ => { });
    }

    public void MapEndpoints(IEndpointRouteBuilder routes)
    {
        const string root = "/sso/ProveIDWeb/v{version:int}";

        // AuthUser — the core authentication endpoint (PWD / UID / PIN scenarios)
        routes.MapPost($"{root}/AuthUser",    AuthUserEndpoint.PostAsync);
        routes.MapGet ($"{root}/AuthUser",    AuthUserEndpoint.GetAsync)
              .RequireAuthorization();
        routes.MapMethods($"{root}/AuthUser", new[] { "CANCEL" }, AuthUserEndpoint.CancelAsync)
              .RequireAuthorization();

        // Discovery (pre-login): no auth required
        routes.MapGet($"{root}/Servers",    ServersEndpoint.GetAsync);
        routes.MapGet($"{root}/Domains",    DomainsEndpoint.GetAsync);
        routes.MapGet($"{root}/Modalities", ModalitiesEndpoint.GetAsync);

        // Resources we deliberately don't implement → 501 with Imprivata-style XML body.
        var unimplementedResources = new[]
        {
            "Password", "Enrollment", "Multi", "VdiAccess",
            "ConfigObject", "SAMLArtifact", "UserAppCreds"
        };
        var anyMethod = new[] { "GET", "POST", "PUT", "DELETE", "PATCH", "CANCEL" };
        foreach (var res in unimplementedResources)
        {
            routes.MapMethods($"{root}/{res}/{{**catchall}}", anyMethod, NotImplementedEndpoint.HandleAsync);
            routes.MapMethods($"{root}/{res}",                anyMethod, NotImplementedEndpoint.HandleAsync);
        }
    }
}
