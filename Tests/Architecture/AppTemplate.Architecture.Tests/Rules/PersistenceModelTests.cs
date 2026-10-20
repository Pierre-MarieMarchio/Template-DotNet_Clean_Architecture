using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed partial class PersistenceModelTests
{
    [GeneratedRegex(
        @"ApplyConfiguration\(\s*new\s+([A-Za-z0-9_]+)\s*\(",
        RegexOptions.CultureInvariant)]
    private static partial Regex ApplyConfiguration();

    private static readonly string _contextPath = Path.Combine(
        ProjectReferenceGraph.RepositoryRoot,
        "Src",
        "Infrastructure",
        "AppTemplate.Infrastructure.Persistence",
        "Common",
        "Contexts",
        "AppDbContext.cs");

    [Fact]
    public void EveryEntityTypeConfiguration_IsAppliedByTheContext()
    {
        var declared = DeclaredConfigurationNames();
        var applied = AppliedConfigurationNames();

        var orphans = declared.Except(applied, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        orphans.ShouldBeEmpty(
            "Every IEntityTypeConfiguration in AppTemplate.Infrastructure.Persistence has to be named "
            + $"in AppDbContext.OnModelCreating, and these are not: {string.Join(", ", orphans)}. "
            + "A configuration nobody applies is inert: the build stays green, the architecture rules "
            + "stay green, and 'dotnet ef migrations add' produces an empty migration, because the "
            + "model and the snapshot omit the entity in exactly the same way. The failure arrives at "
            + "the first query. Add builder.ApplyConfiguration(new <Name>()) to OnModelCreating.");
    }

    [Fact]
    public void EveryConfigurationTheContextApplies_IsDeclaredInTheModule()
    {
        var declared = DeclaredConfigurationNames();
        var applied = AppliedConfigurationNames();

        var unknown = applied.Except(declared, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        unknown.ShouldBeEmpty(
            "AppDbContext.OnModelCreating applies these, and no IEntityTypeConfiguration in "
            + $"AppTemplate.Infrastructure.Persistence declares them: {string.Join(", ", unknown)}.");
    }

    private static HashSet<string> DeclaredConfigurationNames()
    {
        var names = ArchitectureAssemblies.Persistence
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.GetInterfaces().Any(contract =>
                contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>)))
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        int onDisk = ConfigurationFilesOnDisk();

        names.Count.ShouldBe(
            onDisk,
            $"The assembly declares {names.Count} IEntityTypeConfiguration implementations and the "
            + $"persistence project holds {onDisk} files named for one. They disagree, so either "
            + "this discovery has stopped matching the convention or a file is named as a "
            + "configuration without being one, and the rule below would be guarding the wrong set.");

        return names;
    }

    private static int ConfigurationFilesOnDisk()
    {
        string module = Path.Combine(
            ProjectReferenceGraph.RepositoryRoot,
            "Src",
            "Infrastructure",
            "AppTemplate.Infrastructure.Persistence");

        Directory.Exists(module).ShouldBeTrue(
            $"'{module}' was not found, so this rule cannot count the files it compares against.");

        var files = Directory
            .EnumerateFiles(module, "*Configuration.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        files.ShouldNotBeEmpty(
            $"No file named '*Configuration.cs' was found under '{module}'. This walk is the floor "
            + "for the comparison above; finding nothing would let it pass over an empty set.");

        return files.Count;
    }

    private static HashSet<string> AppliedConfigurationNames()
    {
        File.Exists(_contextPath).ShouldBeTrue(
            $"'{_contextPath}' was not found, so this rule cannot read the calls it exists to check. "
            + "The context moved and this path did not follow it.");

        var names = ApplyConfiguration()
            .Matches(File.ReadAllText(_contextPath))
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        names.ShouldNotBeEmpty(
            $"No ApplyConfiguration call was parsed out of '{_contextPath}'. The calls are written "
            + "as 'builder.ApplyConfiguration(new XConfiguration());'; if that shape changed, this "
            + "pattern has to change with it, because every assertion below would otherwise report "
            + "every configuration as orphaned.");

        return names;
    }
}
