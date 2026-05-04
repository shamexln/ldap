using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ImprivataProxy.Facades.Contracts;

/// <summary>
/// ADR-0002 §4.3: pluggable external-protocol adapter.
///
/// Each protocol the proxy exposes (Imprivata ProveID XML, Admin REST, future SAML/OIDC/SCIM …)
/// lives in its own <see cref="IProtocolFacade"/> implementation and self-registers both its
/// DI services and its HTTP routes. <c>Program.cs</c> iterates a list of facades; adding or
/// removing one is a single-line edit in the facades array — no protocol knowledge leaks into
/// the composition root.
/// </summary>
public interface IProtocolFacade
{
    /// <summary>Short identifier used in logs / diagnostics (e.g. "Imprivata", "Admin").</summary>
    string Name { get; }

    /// <summary>
    /// Registers DI services this facade needs (auth schemes, controllers, options, etc.).
    /// Runs during <see cref="WebApplicationBuilder.Build"/> phase.
    /// </summary>
    void RegisterServices(IServiceCollection services, IConfiguration config);

    /// <summary>
    /// Maps HTTP endpoints this facade serves. Runs after <c>app.Use*</c> pipeline setup,
    /// before <c>app.Run()</c>.
    /// </summary>
    void MapEndpoints(IEndpointRouteBuilder routes);
}
