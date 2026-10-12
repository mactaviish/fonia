using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Fonia.Core.Sim;

namespace Fonia.Core.Tests.Architecture;

/// <summary>
/// Guards the rules in CLAUDE.md that are otherwise only conventions: the core depends on no other
/// project of the solution, and simulator-specific libraries only appear in <c>Fonia.Sim.*</c> projects.
/// Checked on both the compiled assembly and the project files, because the compiler drops
/// references that no code uses.
/// </summary>
public class SolutionLayoutTests
{
    private static readonly string[] SimulatorLibraryPatterns =
    [
        @"SimConnect",
        @"FsConnect",
        @"FSUIPC",
        @"Microsoft\.FlightSimulator",
        @"XPlane",
    ];

    [Fact]
    public void Core_assembly_references_no_other_solution_assembly()
    {
        var references = typeof(ISimAdapter).Assembly.GetReferencedAssemblies();

        references
            .Select(r => r.Name)
            .Where(name => name is not null && name.StartsWith("Fonia.", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Core_assembly_references_no_simulator_library()
    {
        var references = typeof(ISimAdapter).Assembly.GetReferencedAssemblies();

        references
            .Select(r => r.Name)
            .Where(name => name is not null && IsSimulatorLibrary(name))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Core_project_has_no_project_references()
    {
        var project = LoadProject("src/Fonia.Core/Fonia.Core.csproj");

        project.Descendants("ProjectReference").ShouldBeEmpty();
    }

    [Fact]
    public void Simulator_libraries_are_only_used_by_simulator_projects()
    {
        var offenders = new List<string>();

        foreach (var path in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.csproj", SearchOption.AllDirectories))
        {
            var projectName = Path.GetFileNameWithoutExtension(path);

            if (projectName.StartsWith("Fonia.Sim.", StringComparison.Ordinal))
            {
                continue;
            }

            var dependencies = XDocument.Load(path)
                .Descendants()
                .Where(e => e.Name.LocalName is "PackageReference" or "Reference" or "FrameworkReference")
                .Select(e => (string?)e.Attribute("Include") ?? (string?)e.Attribute("Update") ?? string.Empty);

            offenders.AddRange(dependencies
                .Where(IsSimulatorLibrary)
                .Select(dependency => $"{projectName} -> {dependency}"));
        }

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Simulator_projects_do_not_reference_each_other()
    {
        // Each Fonia.Sim.* project depends on the core only, so a simulator can be added or removed alone.
        var offenders = new List<string>();

        foreach (var path in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.csproj", SearchOption.AllDirectories))
        {
            var projectName = Path.GetFileNameWithoutExtension(path);

            if (!projectName.StartsWith("Fonia.Sim.", StringComparison.Ordinal))
            {
                continue;
            }

            var references = XDocument.Load(path)
                .Descendants("ProjectReference")
                .Select(e => Path.GetFileNameWithoutExtension(((string?)e.Attribute("Include") ?? string.Empty).Replace('\\', '/')))
                .Where(name => name.StartsWith("Fonia.Sim.", StringComparison.Ordinal));

            offenders.AddRange(references.Select(reference => $"{projectName} -> {reference}"));
        }

        offenders.ShouldBeEmpty();
    }

    private static bool IsSimulatorLibrary(string name)
    {
        return SimulatorLibraryPatterns.Any(pattern => Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase));
    }

    private static XDocument LoadProject(string relativePath)
    {
        return XDocument.Load(Path.Combine(RepoRoot(), relativePath));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Fonia.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"Fonia.sln not found above {Assembly.GetExecutingAssembly().Location}");
    }
}
