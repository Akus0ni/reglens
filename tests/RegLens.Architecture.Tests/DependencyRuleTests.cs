using System.Reflection;

namespace RegLens.Architecture.Tests;

/// <summary>Clean-architecture dependency rules (D22, spec 0001 R1.4): dependencies only point inward.</summary>
public class DependencyRuleTests
{
    private static readonly string[] BaseLibraryPrefixes = ["System", "netstandard", "mscorlib", "Microsoft.Win32"];

    [Fact]
    public void DomainReferencesOnlyTheBaseLibrary()
    {
        References("RegLens.Domain").Should().AllSatisfy(name =>
            BaseLibraryPrefixes.Should().Contain(prefix => name.StartsWith(prefix, StringComparison.Ordinal),
                $"Domain must stay pure, but it references {name}"));
    }

    [Fact]
    public void ApplicationReferencesNoInfrastructureOrHost()
    {
        References("RegLens.Application").Should()
            .NotContain(["RegLens.Infrastructure", "RegLens.Ingest", "RegLens.Api", "Npgsql", "Pgvector", "UglyToad.PdfPig", "OllamaSharp"]);
    }

    [Fact]
    public void InfrastructureReferencesNoHost()
    {
        References("RegLens.Infrastructure").Should().NotContain(["RegLens.Ingest", "RegLens.Api"]);
    }

    private static List<string> References(string assemblyName) =>
        Assembly.Load(assemblyName).GetReferencedAssemblies().Select(a => a.Name!).ToList();
}
