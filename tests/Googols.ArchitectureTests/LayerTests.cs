using System.Reflection;

namespace Googols.ArchitectureTests;

/// <summary>Keeps the Clean Architecture dependency rule honest: inner layers never reference outer ones or vendors.</summary>
public class LayerTests
{
    private static readonly Assembly Domain = typeof(Googols.Domain.Common.Entity).Assembly;
    private static readonly Assembly Application = typeof(Googols.Application.DependencyInjection).Assembly;

    private static string[] Refs(Assembly a) => a.GetReferencedAssemblies().Select(r => r.Name ?? "").ToArray();

    [Fact]
    public void Domain_has_no_project_or_package_dependencies()
    {
        var refs = Refs(Domain);
        Assert.DoesNotContain(refs, r => r.StartsWith("Googols."));
        Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.EntityFrameworkCore") || r.StartsWith("Azure") || r.StartsWith("Hangfire"));
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_vendors()
    {
        var refs = Refs(Application);
        Assert.DoesNotContain("Googols.Infrastructure", refs);
        Assert.DoesNotContain("Googols.WebApi", refs);
        Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.EntityFrameworkCore") || r.StartsWith("Azure") || r.StartsWith("Hangfire") || r.StartsWith("OpenAI"));
    }
}
