using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// The mapping is only real once <c>AppDbContext.OnModelCreating</c> names it. A configuration class
/// that nobody applies compiles, formats, passes every architecture rule, and leaves the table it
/// describes unmapped until the first query against it.
/// </summary>
/// <remarks>
/// <b>Why the source and not the model.</b> Asking a built model whether an entity is mapped answers
/// a different question: <c>AppDbContext</c> exposes a <see cref="DbSet{TEntity}"/> for most of its
/// records, and a <c>DbSet</c> is enough to bring an entity into the model with conventions alone.
/// So a record that has its <c>DbSet</c> but never had its configuration applied is present, mapped
/// to a defaulted table in the default schema, and indistinguishable from a configured one to
/// anything that reads the model. The claim worth checking is the one about the call, so this reads
/// the call.
/// </remarks>
public sealed class PersistenceModelTests
{
    /// <summary>Matches <c>builder.ApplyConfiguration(new SomethingConfiguration());</c>.</summary>
    private static readonly Regex _applyConfiguration = new(
        @"ApplyConfiguration\(\s*new\s+([A-Za-z0-9_]+)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

    /// <summary>
    /// The other direction, which the compiler already covers for a deleted class but not for one
    /// that stopped being a configuration — a class named <c>…Configuration</c> that no longer
    /// implements the interface still compiles inside <c>ApplyConfiguration</c> only while it does.
    /// Cheap to assert, and it keeps the two lists honest in both directions.
    /// </summary>
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

        // Measured against the disk rather than against a number written here. A literal would be
        // right only until someone removes a feature, and this rule already appears in the removal
        // guide as a floor to lower by hand — which is the defect it exists to prevent, in the file
        // that prevents it. The two routes are independent: reflection over the built assembly, and
        // the files the convention names.
        int onDisk = ConfigurationFilesOnDisk();

        names.Count.ShouldBe(
            onDisk,
            $"The assembly declares {names.Count} IEntityTypeConfiguration implementations and the "
            + $"persistence project holds {onDisk} files named for one. They disagree, so either "
            + "this discovery has stopped matching the convention or a file is named as a "
            + "configuration without being one, and the rule below would be guarding the wrong set.");

        return names;
    }

    /// <summary>
    /// The second route to the same answer: the files the naming convention puts a configuration
    /// in. Migrations are excluded because EF writes them and they carry no configuration class.
    /// </summary>
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

        var names = _applyConfiguration
            .Matches(File.ReadAllText(_contextPath))
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        // Deliberately a floor of one rather than of fifteen. This guards the parser, not the
        // count: a pattern that has stopped matching finds nothing, while a configuration somebody
        // forgot to apply finds one fewer. Holding this to the real total would make the second
        // case fail here, reporting a number instead of naming the configuration that went
        // unapplied — a floor over the wrong set, which is the failure this project has already
        // paid for once.
        names.ShouldNotBeEmpty(
            $"No ApplyConfiguration call was parsed out of '{_contextPath}'. The calls are written "
            + "as 'builder.ApplyConfiguration(new XConfiguration());'; if that shape changed, this "
            + "pattern has to change with it, because every assertion below would otherwise report "
            + "every configuration as orphaned.");

        return names;
    }
}
