using System.Security.Claims;
using Merp.Application.Abstractions.Context;
using Merp.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Merp.BuildingBlocks.UnitTests.Web;

public class DevelopmentPermissionCheckerTests
{
    [Theory]
    [InlineData("admin", "platform.system.read", true)]
    [InlineData("ik", "hr.employees.read", true)]
    [InlineData("ik", "platform.system.read", false)]
    [InlineData("calisan", "hr.self.profile.read", true)]
    [InlineData("calisan", "hr.employees.read", false)]
    [InlineData("stranger", "platform.system.read", false)]
    public async Task GrantsConfiguredPatternsPerUser(string userName, string permission, bool expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Development:Permissions:admin:0"] = "*",
            ["Development:Permissions:ik:0"] = "hr.*",
            ["Development:Permissions:calisan:0"] = "hr.self.*",
        }).Build();
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("preferred_username", userName)], "test")),
            },
        };
        var tenant = Substitute.For<ITenantContext>();
        tenant.Tenant.Returns(new TenantInfo(Guid.CreateVersion7(), "demo", "Demo", "tr-TR"));
        tenant.HasTenant.Returns(true);

        var granted = await new DevelopmentPermissionChecker(http, tenant, configuration).HasPermissionAsync(permission, TestContext.Current.CancellationToken);

        granted.ShouldBe(expected);
    }

    [Fact]
    public void Wildcards_MatchOnlyWholeSegments()
    {
        DevelopmentPermissionChecker.Matches("hr.*", "hr.leave.read").ShouldBeTrue();
        DevelopmentPermissionChecker.Matches("hr.*", "hrx.leave.read").ShouldBeFalse();
        DevelopmentPermissionChecker.Matches("hr.leave.read", "hr.leave.read.all").ShouldBeFalse();
    }
}
