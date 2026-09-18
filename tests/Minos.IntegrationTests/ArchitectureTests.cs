using System.Xml.Linq;
using Minos.Application.Problems;
using Minos.Domain.Problems;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class ArchitectureTests
{
    internal static string Repository => Environment.GetEnvironmentVariable("MINOS_TEST_REPOSITORY")
        ?? FindRepository(AppContext.BaseDirectory);

    private static string FindRepository(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Minos.sln"))) return directory.FullName;
        throw new InvalidOperationException("Cannot locate Minos.sln for architecture validation.");
    }

    [Fact]
    public void Production_project_graph_has_exactly_five_projects_and_only_approved_edges()
    {
        var allowed = new Dictionary<string, string[]>
        {
            ["Minos.Domain"] = [], ["Minos.Application"] = ["Minos.Domain"],
            ["Minos.Infrastructure"] = ["Minos.Application", "Minos.Domain"],
            ["Minos.Api"] = ["Minos.Application", "Minos.Infrastructure"],
            ["Minos.JudgeWorker"] = ["Minos.Application", "Minos.Infrastructure"]
        };
        var projects = Directory.GetFiles(Path.Combine(Repository, "src"), "*.csproj", SearchOption.AllDirectories);
        Assert.Equal(allowed.Keys.Order(), projects.Select(Path.GetFileNameWithoutExtension).Order());
        var edges = new Dictionary<string, string[]>();
        foreach (var path in projects)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var xml = XDocument.Load(path);
            var references = xml.Descendants("ProjectReference").Select(element => Path.GetFileNameWithoutExtension((string)element.Attribute("Include")!)).ToArray();
            Assert.All(references, reference => Assert.Contains(reference, allowed[name]));
            if (name is "Minos.Domain" or "Minos.Application")
            {
                Assert.Empty(xml.Descendants("PackageReference"));
                Assert.Empty(xml.Descendants("FrameworkReference"));
            }
            edges[name] = references;
        }
        foreach (var name in edges.Keys) Visit(name, []);
        void Visit(string name, HashSet<string> ancestors)
        {
            Assert.True(ancestors.Add(name), $"Circular project dependency involving {name}.");
            foreach (var dependency in edges[name]) Visit(dependency, new HashSet<string>(ancestors));
        }
    }

    [Fact]
    public void Compiled_domain_and_application_have_no_forbidden_runtime_dependencies()
    {
        foreach (var assembly in new[] { typeof(ProblemDraft).Assembly, typeof(ProblemDraftUseCases).Assembly })
            Assert.All(assembly.GetReferencedAssemblies(), reference => Assert.True(
                reference.Name != "System.Diagnostics.Process" && reference.Name != "System.Net.Http" &&
                (reference.Name == "Minos.Domain" || reference.Name == "netstandard" || reference.Name!.StartsWith("System", StringComparison.Ordinal)),
                $"{assembly.GetName().Name} references forbidden assembly {reference.Name}."));
        var domainSources = SourceFiles("Minos.Domain");
        Assert.All(domainSources, path => Assert.DoesNotContain("System.Diagnostics.Process", File.ReadAllText(path), StringComparison.Ordinal));
    }

    [Fact]
    public void Hosts_use_infrastructure_only_in_composition_and_hosting_files()
    {
        var hostingFiles = new[] { "Program.cs", "AuthenticationComposition.cs", "FoundationStartupCheck.cs", "DatabaseHealthCheck.cs", "WorkerFoundationStartupCheck.cs" };
        foreach (var host in new[] { "Minos.Api", "Minos.JudgeWorker" })
            foreach (var file in SourceFiles(host).Where(path => !hostingFiles.Contains(Path.GetFileName(path))))
                Assert.DoesNotContain("Minos.Infrastructure", File.ReadAllText(file), StringComparison.Ordinal);
        foreach (var file in SourceFiles("Minos.JudgeWorker"))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("Microsoft.AspNetCore", source, StringComparison.Ordinal);
            Assert.DoesNotContain("AddMinosAccountPersistence", source, StringComparison.Ordinal);
        }
    }

    private static IEnumerable<string> SourceFiles(string project) => Directory.GetFiles(Path.Combine(Repository, "src", project), "*.cs", SearchOption.AllDirectories)
        .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"));
}
