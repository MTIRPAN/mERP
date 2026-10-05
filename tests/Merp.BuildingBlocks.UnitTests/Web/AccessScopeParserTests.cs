using Merp.Application.Abstractions.Security;
using Merp.Web.Security;

namespace Merp.BuildingBlocks.UnitTests.Web;

public sealed class AccessScopeParserTests
{
    [Fact]
    public void Parse_ReadsTypesWithAndWithoutIds()
    {
        var company = Guid.CreateVersion7();

        var scopes = AccessScopeParser.Parse(["Tenant", $" Company:{company} ", ""]);

        scopes.ShouldBe([new AccessScope(AccessScopeTypes.Tenant), new AccessScope(AccessScopeTypes.Company, company)]);
    }

    [Fact]
    public void Parse_SkipsScopesWithAnInvalidId() =>
        AccessScopeParser.Parse(["Company:acme"]).ShouldBeEmpty();
}
