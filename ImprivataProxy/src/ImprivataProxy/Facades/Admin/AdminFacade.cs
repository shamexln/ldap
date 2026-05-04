using ImprivataProxy.Facades.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ImprivataProxy.Facades.Admin;

/// <summary>
/// Management REST API adapter. Exposes CRUD-style endpoints for administrators
/// (users, cards, manual AD sync trigger) behind the Admin Basic-auth scheme.
///
/// Independent of the Imprivata protocol — sharing the same IdpCore + Sources
/// but with its own authentication boundary.
/// </summary>
public class AdminFacade : IProtocolFacade
{
    public string Name => "Admin";

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        // Additional auth scheme; ImprivataFacade already called AddAuthentication()
        // with the default scheme. Calling again without a scheme name just appends.
        services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, AdminAuthenticationHandler>(
                    AdminAuthenticationHandler.SchemeName, _ => { });

        services.AddControllers();
    }

    public void MapEndpoints(IEndpointRouteBuilder routes)
    {
        // UsersController, CardsController, SyncController — all routed by MVC conventions.
        routes.MapControllers();
    }
}
