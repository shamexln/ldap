using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace ImprivataProxy.Tests.Architecture;

/// <summary>
/// ADR-0002 §8 anti-pattern enforcement via ArchUnitNET.
/// These tests fail fast if a future change re-introduces a layering violation.
///
/// Layer namespaces:
///   Facades.*   — external protocol adapters (Imprivata XML, Admin REST)
///   IdpCore.*   — protocol-agnostic authentication policy
///   Sources.*   — identity-source adapters (Local DB, Active Directory)
///     Sources.Contracts        — abstractions (allowed from IdpCore)
///     Sources.Local            — concrete impls + AppDbContext
///     Sources.Local.Entities   — EF-backed data records (shared domain types)
///     Sources.ActiveDirectory  — LDAP client + sync
///   Shared.Contracts — layer-neutral contracts (IAuditSink, IClientContextProvider, PwdOrPin)
///   Shared.*     — cross-cutting infrastructure (Logging, Xml, Http)
///   Configuration — IOptions POCOs (any layer may consume)
/// </summary>
public class LayeringTests
{
    private static readonly ArchUnitNET.Domain.Architecture Arch =
        new ArchLoader()
            .LoadAssemblies(typeof(Program).Assembly)
            .Build();

    // Regex helpers — anchored on full type name ("namespace.TypeName").
    // `(\..*)?$` lets each layer include its sub-namespaces.
    private const string FacadesTypes = @"^ImprivataProxy\.Facades(\..*)?$";
    private const string IdpCoreTypes = @"^ImprivataProxy\.IdpCore(\..*)?$";
    private const string SourcesTypes = @"^ImprivataProxy\.Sources(\..*)?$";

    // ========================================================================
    // §8.1 Facades must not reach into Sources concrete types.
    //   Allowed: Sources.Contracts (interfaces), Sources.Local.Entities (data types).
    //   Forbidden: AppDbContext, UserStore, Ef*Repo, LdapClient, etc.
    // ========================================================================

    [Fact]
    public void Facades_Should_Not_Depend_On_AppDbContext()
    {
        Types().That().HaveFullNameMatching(FacadesTypes)
               .Should().NotDependOnAny(
                   Types().That().HaveName("AppDbContext"))
               .Because("ADR-0002 §8.1: Facades go through IUserStore / IAuditStore / etc., never DbContext directly.")
               .Check(Arch);
    }

    [Fact]
    public void Facades_Should_Not_Depend_On_Concrete_Repos_Or_Stores()
    {
        // All concrete persistence classes in Sources.Local start with "Ef"
        // (EfAuditStore, EfAuthSessionRepo, EfTicketBlacklistRepo, EfLockoutRepo)
        // or are specifically "UserStore".
        Types().That().HaveFullNameMatching(FacadesTypes)
               .Should().NotDependOnAny(
                   Types().That().HaveNameStartingWith("Ef")
                                 .Or()
                                 .HaveName("UserStore"))
               .Because("ADR-0002 §8.1: Facades must not bind to Sources.Local concrete repos/stores.")
               .Check(Arch);
    }

    [Fact]
    public void Facades_Should_Not_Depend_On_LdapClient()
    {
        Types().That().HaveFullNameMatching(FacadesTypes)
               .Should().NotDependOnAny(
                   Types().That().HaveName("LdapClient"))
               .Because("ADR-0002 §8.1: Facades must not know about the AD-specific LdapClient.")
               .Check(Arch);
    }

    // ========================================================================
    // §8.2 IdpCore must not depend on HTTP / XML / concrete Source classes.
    //   Allowed: IUserStore, IAuditStore, ILockoutRepo, IClientContextProvider (abstractions).
    //   Forbidden: AppDbContext, IHttpContextAccessor, System.Xml.Linq.*.
    // ========================================================================

    [Fact]
    public void IdpCore_Should_Not_Depend_On_AppDbContext()
    {
        Types().That().HaveFullNameMatching(IdpCoreTypes)
               .Should().NotDependOnAny(
                   Types().That().HaveName("AppDbContext"))
               .Because("ADR-0002 §8.2: IdpCore persistence goes through IAuditStore / ILockoutRepo / etc.")
               .Check(Arch);
    }

    [Fact]
    public void IdpCore_Should_Not_Depend_On_HttpContextAccessor()
    {
        Types().That().HaveFullNameMatching(IdpCoreTypes)
               .Should().NotDependOnAny(
                   Types().That().HaveFullNameMatching(@"^Microsoft\.AspNetCore\.Http\.IHttpContextAccessor$"))
               .Because("ADR-0002 §8.2: IdpCore must be HTTP-ignorant; use IClientContextProvider (Shared.Contracts).")
               .Check(Arch);
    }

    [Fact]
    public void IdpCore_Should_Not_Depend_On_System_Xml_Linq()
    {
        // XML handling is a facade concern (Imprivata XML protocol). IdpCore must stay protocol-agnostic.
        Types().That().HaveFullNameMatching(IdpCoreTypes)
               .Should().NotDependOnAny(
                   Types().That().HaveFullNameMatching(@"^System\.Xml\.Linq\..*"))
               .Because("ADR-0002 §8.2: XML parsing / building lives in Facades, not IdpCore.")
               .Check(Arch);
    }

    // ========================================================================
    // §8.3 Sources must not depend on IdpCore or Facades.
    //   Allowed: Shared.Contracts (layer-neutral contracts like PwdOrPin, IAuditSink).
    // ========================================================================

    [Fact]
    public void Sources_Should_Not_Depend_On_IdpCore()
    {
        Types().That().HaveFullNameMatching(SourcesTypes)
               .Should().NotDependOnAny(
                   Types().That().HaveFullNameMatching(IdpCoreTypes))
               .Because("ADR-0002 §8.3: Sources is the bottom layer; shared vocabulary belongs in Shared.Contracts.")
               .Check(Arch);
    }

    [Fact]
    public void Sources_Should_Not_Depend_On_Facades()
    {
        Types().That().HaveFullNameMatching(SourcesTypes)
               .Should().NotDependOnAny(
                   Types().That().HaveFullNameMatching(FacadesTypes))
               .Because("ADR-0002 §8.3: Sources must not know about external protocols.")
               .Check(Arch);
    }

    // ========================================================================
    // §8.4 Configuration must not leak IConfiguration into IdpCore or Sources.
    //   Only Program.cs + Facade.RegisterServices consumes IConfiguration directly.
    // ========================================================================

    [Fact]
    public void IdpCore_Should_Not_Depend_On_IConfiguration()
    {
        Types().That().HaveFullNameMatching(IdpCoreTypes)
               .Should().NotDependOnAny(
                   Types().That().HaveFullNameMatching(@"^Microsoft\.Extensions\.Configuration\.IConfiguration$"))
               .Because("ADR-0002 §8.4: IdpCore consumes typed options (IOptions<T>), not raw IConfiguration.")
               .Check(Arch);
    }

    [Fact]
    public void Sources_Should_Not_Depend_On_IConfiguration()
    {
        Types().That().HaveFullNameMatching(SourcesTypes)
               .Should().NotDependOnAny(
                   Types().That().HaveFullNameMatching(@"^Microsoft\.Extensions\.Configuration\.IConfiguration$"))
               .Because("ADR-0002 §8.4: Sources consumes typed options (IOptions<T>), not raw IConfiguration.")
               .Check(Arch);
    }
}
