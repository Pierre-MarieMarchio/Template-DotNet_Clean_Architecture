using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class BannedPackageTests
{
    private static readonly string[] _sourceFolders = ["Src", "Tests"];

    private static readonly (string Package, string Because)[] _banned =
    [
        (
            "MediatR",
            "its licence is commercial. Use cases are sent through the source-generated Mediator, whose licence is MIT (ADR 0033)."),
        (
            "AutoMapper",
            "a mapping is written by hand, where the compiler checks it and a reader finds it. " +
            "Write the mapping as a method on the type it produces."),
    ];

    [Fact]
    public void NoProject_ReferencesABannedPackage()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;

        string[] files =
        [
            Path.Combine(root, "Directory.Packages.props"),
            .. _sourceFolders
                .Select(folder => Path.Combine(root, folder))
                .Where(Directory.Exists)
                .SelectMany(folder => Directory.EnumerateFiles(folder, "*.csproj", SearchOption.AllDirectories))
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal)),
        ];

        files.Length.ShouldBeGreaterThan(1, "No project file was read, so this rule would hold by reading nothing.");

        foreach (var (package, because) in _banned)
        {
            files
                .Where(file => References(File.ReadAllText(file), package))
                .Select(file => Path.GetRelativePath(root, file))
                .Order(StringComparer.Ordinal)
                .ShouldBeEmpty($"{package} is refused in every project: {because}");
        }
    }

    [Fact]
    public void TheBanMatcher_IsSensitive_AndTellsAPackageFromAnother()
    {
        References("<PackageReference Include=\"MediatR\" />", "MediatR").ShouldBeTrue();
        References("<PackageVersion Include=\"MediatR.Contracts\" Version=\"2.0.1\" />", "MediatR").ShouldBeTrue();
        References("<PackageReference Include=\"AutoMapper\" />", "AutoMapper").ShouldBeTrue();
        References("<PackageVersion Include=\"AutoMapper.Extensions.Microsoft.DependencyInjection\" Version=\"12.0.1\" />", "AutoMapper").ShouldBeTrue();
        References("<PackageReference Include=\"Mediator.Abstractions\" />", "MediatR").ShouldBeFalse();
        References("<PackageReference Include=\"Riok.Mapperly\" />", "AutoMapper").ShouldBeFalse();
    }

    private static bool References(string projectFile, string package) =>
        Regex.IsMatch(
            projectFile,
            $"Include=\"{Regex.Escape(package)}(\\.[^\"]*)?\"",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
}
