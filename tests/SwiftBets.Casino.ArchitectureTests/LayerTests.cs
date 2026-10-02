using System.Reflection;
using NetArchTest.Rules;

namespace SwiftBets.Casino.ArchitectureTests;

public sealed class LayerTests
{
    private static readonly Assembly Domain = typeof(SwiftBets.Casino.Domain.ProviderSignature).Assembly;
    private static readonly Assembly Application = typeof(SwiftBets.Casino.Application.ApplicationRegistration).Assembly;

    [Fact]
    public void Domain_depends_on_nothing_else_in_the_solution() =>
        Types.InAssembly(Domain).ShouldNot().HaveDependencyOnAny("SwiftBets.Casino.Application", "SwiftBets.Casino.Infrastructure", "SwiftBets.BuildingBlocks", "SwiftBets.Contracts", "Microsoft.AspNetCore")
            .GetResult().IsSuccessful.ShouldBeTrue();

    [Fact]
    public void Application_does_not_depend_on_infrastructure() =>
        Types.InAssembly(Application).ShouldNot().HaveDependencyOnAny("SwiftBets.Casino.Infrastructure", "Grpc", "Dapper", "Microsoft.Data.SqlClient", "System.Net.Http")
            .GetResult().IsSuccessful.ShouldBeTrue();
}
