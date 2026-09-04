using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// The layout itself, read from the source tree rather than from compiled metadata.
/// <para>
/// Reflection cannot see either of these rules. It knows a type's namespace but not which file
/// declares it, and it knows nothing at all about a folder that exists and holds nothing. Both are
/// properties of the tree a reader navigates, so the tree is what these read.
/// </para>
/// </summary>
public sealed class LayoutConventionTests
{
    /// <summary>
    /// The folders a feature may hold, per layer. Closed on purpose: a word outside this list is a
    /// concept a reader has to infer from its contents, and one feature inventing a word the others
    /// do not use is how a layout stops being an index. Adding a word is an edit to
    /// CONTRIBUTING.md's Layout section and to this list, argued for in the pull request — not a
    /// mkdir.
    /// </summary>
    private static readonly Dictionary<string, string[]> _vocabulary = new(StringComparer.Ordinal)
    {
        ["Src/Application/AppTemplate.Application"] =
            ["Consumers", "Dtos", "Errors", "Extensions", "Mapping", "Policies", "Ports", "Services", "UseCases"],
        ["Src/Domain/AppTemplate.Domain"] =
            ["Entities", "Events", "Repositories", "ValueObjects"],
        ["Src/Infrastructure/AppTemplate.Infrastructure.Persistence"] =
            ["Configurations", "Mapping", "Models", "Observability", "Queries", "Repositories", "Seeding", "Tables", "Tracking"],
        ["Src/Presentation/AppTemplate.Api"] =
            ["Contracts", "Controllers", "Mapping"],

        // Every word here is the plural of a nature word the file names already carry — the rule the
        // rest of the repository follows, where a …Repository is in Repositories/ and a …Tracker in
        // Tracking/. Which is what makes a folder findable from a type name alone and back again: a
        // …Service is in Services/ and nowhere else, and Services/ holds nothing that is not one.
        ["Src/Infrastructure/AppTemplate.Infrastructure.Identity"] =
            ["Directories", "Factories", "Issuers", "Logs", "Options", "Providers", "Services", "Templates", "Verifiers"],
        ["Src/Infrastructure/AppTemplate.Infrastructure.Storage"] =
            ["Inspectors", "Inventories", "Options", "Scanners", "Stores"],

        // Empty on purpose, and checked rather than skipped. AppTemplate.Worker's features hold a
        // BackgroundService, its options and its metrics side by side, with no subfolder — so the
        // correct vocabulary today is "none", and the first subfolder anyone adds fails this test
        // instead of quietly inventing a word the other hosts do not use.
        ["Src/Presentation/AppTemplate.Worker"] = [],

        // Same reason, one layer down. These two modules have both a transverse adapter and a
        // feature-scoped one, which is what earns them Common/ and Features/ at all; the feature
        // half is one adapter and its recording double, side by side — including the reminder mail's
        // templates, which sit beside the notifier rather than earning a folder of their own for two
        // files.
        ["Src/Infrastructure/AppTemplate.Infrastructure.Email"] = [],
        ["Src/Infrastructure/AppTemplate.Infrastructure.InMemory"] = [],
    };

    /// <summary>
    /// The folders a project's <c>Common/</c> may hold, per project. Closed for the same reason as
    /// the feature vocabulary above: <c>Common/</c> is where a layout drifts fastest, because a
    /// folder with no feature to belong to accepts any name — a dozen top-level folders with vague
    /// names among them, or a one-file <c>Mapping/</c> borrowing a word that already means something
    /// else one level down.
    /// <para>
    /// Only the first level is checked. A word a reader meets on the way in has to be one of these;
    /// what a folder holds below that is the folder's own business — <c>Saving/</c> partitions
    /// itself into <c>Auditing/</c>, <c>DomainEvents/</c> and <c>Tracking/</c> and that is a
    /// detail of one subject, not a word the layout offers.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string[]> _commonVocabulary = new(StringComparer.Ordinal)
    {
        ["Src/Domain/AppTemplate.Domain"] =
            ["Abstractions", "Events", "Exceptions", "Primitives"],
        ["Src/Application/AppTemplate.Application"] =
            ["Abstractions", "Collections", "Concurrency", "Idempotency", "Localization", "Results", "Validation"],
        ["Src/Infrastructure/AppTemplate.Infrastructure.Email"] =
            ["Http", "Smtp"],
        ["Src/Infrastructure/AppTemplate.Infrastructure.InMemory"] =
            ["Email", "Time"],
        ["Src/Infrastructure/AppTemplate.Infrastructure.Identity"] =
            ["Directories", "Options"],
        ["Src/Infrastructure/AppTemplate.Infrastructure.Persistence"] =
            ["Contexts", "Idempotency", "Leases", "Options", "Saving", "Time"],
        ["Src/Infrastructure/AppTemplate.Infrastructure.Storage"] =
            ["Budgets", "Factories", "Options"],
        ["Src/Presentation/AppTemplate.Api"] =
            ["Caching", "Concurrency", "Contracts", "Controllers", "Errors", "Hosting",
             "Idempotency", "Localization", "Observability", "OpenApi", "Outbound", "Security"],
        ["Src/Presentation/AppTemplate.Worker"] =
            ["Localization", "Observability", "Outbound", "Security"],
    };

    [Fact]
    public void EveryCommonFolder_IsNamedFromItsProjectsVocabulary()
    {
        var checkedProjects = 0;
        var offenders = new List<string>();

        foreach ((string project, string[] allowed) in _commonVocabulary)
        {
            string common = Path.Combine(ProjectReferenceGraph.RepositoryRoot, project, "Common");

            if (!Directory.Exists(common))
            {
                continue;
            }

            checkedProjects++;

            offenders.AddRange(Directory
                .EnumerateDirectories(common)
                .Select(Path.GetFileName)
                .Where(folder => folder is not null && !allowed.Contains(folder, StringComparer.Ordinal))
                .Select(folder => $"{project}: 'Common/{folder}' is not one of " +
                    $"[{string.Join(", ", allowed)}]"));

            offenders.AddRange(Directory
                .EnumerateFiles(common, "*.cs")
                .Select(file => $"{project}: 'Common/{Path.GetFileName(file)}' sits loose at the " +
                    "root of Common, which names no responsibility at all"));
        }

        checkedProjects.ShouldBe(
            _commonVocabulary.Count,
            "A project's Common folder was not found, so its vocabulary was never checked.");

        offenders.Order(StringComparer.Ordinal).ShouldBeEmpty(
            "Common/ is the half of a project that knows no feature, and a word invented there is " +
            "read by everyone. Adding one is an edit to this list and to CONTRIBUTING.md's Layout " +
            "section, argued for in the pull request.");
    }

    /// <summary>
    /// A top-level public type declaration. Anchored at column zero because a nested type is indented
    /// and stays with its parent, and because <c>internal</c> companions — an options validator beside
    /// the options it validates — are deliberately not the subject of the rule.
    /// </summary>
    private static readonly Regex _publicTypeDeclaration = new(
        @"^public\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+|ref\s+)*(?:class|record|interface|enum|struct)\s+([A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void EveryFeatureFolder_IsNamedFromItsLayersVocabulary()
    {
        var checkedLayers = 0;
        var offenders = new List<string>();

        foreach ((string project, string[] allowed) in _vocabulary)
        {
            string features = Path.Combine(ProjectReferenceGraph.RepositoryRoot, project, "Features");

            if (!Directory.Exists(features))
            {
                continue;
            }

            checkedLayers++;

            offenders.AddRange(Directory
                .EnumerateDirectories(features)
                .SelectMany(feature => Directory.EnumerateDirectories(feature))
                .Where(folder => !allowed.Contains(Path.GetFileName(folder), StringComparer.Ordinal))
                .Select(folder => $"{project}: '{Path.GetRelativePath(features, folder)}' is not one of " +
                    $"[{string.Join(", ", allowed)}]"));

            // The same second pass its sibling above makes over Common/'s loose files, and it has to
            // be conditional where that one does not: a project whose list is empty says by saying so
            // that its features hold their files side by side, which is the documented shape for the
            // worker and for the two smallest infrastructure modules. Running the pass there would
            // fail twenty-one correct files. Where a word does exist, a file lying beside the folders
            // rather than in one is filed under nothing.
            if (allowed.Length == 0)
            {
                continue;
            }

            offenders.AddRange(Directory
                .EnumerateDirectories(features)
                .SelectMany(feature => Directory.EnumerateFiles(feature, "*.cs"))
                .Select(file => $"{project}: '{Path.GetRelativePath(features, file)}' sits loose at " +
                    $"the root of its feature, under none of [{string.Join(", ", allowed)}]"));
        }

        checkedLayers.ShouldBe(
            _vocabulary.Count,
            "A layer's Features folder was not found, so its vocabulary was never checked.");

        offenders.Order(StringComparer.Ordinal).ShouldBeEmpty(
            "A folder outside its layer's vocabulary makes the reader infer a concept from the files " +
            "inside it, and lets one feature be organised unlike every other. A file loose beside " +
            "those folders is the same defect from the other side: the vocabulary stops describing " +
            "the feature the moment part of it is filed under no word at all.");
    }

    /// <summary>
    /// The two vocabularies above, against the infrastructure modules that actually exist on disk.
    /// </summary>
    /// <remarks>
    /// Both dictionaries are maintained by hand, and the guards on the two rules above cannot catch
    /// what went wrong here: <c>checkedLayers.ShouldBe(_vocabulary.Count)</c> fails on a project
    /// <em>listed without</em> the folder it names, and never on a project <em>on disk and not
    /// listed</em>. So the identity and storage modules sat outside both lists while their layout
    /// drifted — forty files in ten root folders in one of them — and the rule whose whole business
    /// is that a layout is an index reported success without reading either.
    /// <para>
    /// This is the same hole <c>ModuleDependencyTests</c> closed for
    /// <c>ArchitectureAssemblies</c>, and it is closed the same way: by asking the disk, which is
    /// where <c>ProjectReferenceGraph</c> was already looking.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryInfrastructureModuleOnDisk_HasAVocabularyOfItsOwn()
    {
        var onDisk = ProjectReferenceGraph.InfrastructureModules
            .Select(project => Path.GetDirectoryName(project.RelativePath)!.Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

        onDisk.Count.ShouldBeGreaterThanOrEqualTo(
            5,
            "Far fewer infrastructure modules were found under Src than this template holds, so the " +
            "project walk is not reading the tree it is meant to describe and every module in it " +
            "would read as listed.");

        var unlisted = onDisk
            .Where(project => !_vocabulary.ContainsKey(project) || !_commonVocabulary.ContainsKey(project))
            .Order(StringComparer.Ordinal)
            .ToList();

        unlisted.ShouldBeEmpty(
            "An infrastructure module exists that neither vocabulary names, so both rules above walk " +
            "past it and pass. Give it an entry in each — empty when its features hold their files " +
            "side by side, as Email and InMemory do — and write the words into CONTRIBUTING.md's " +
            "Layout section.");
    }

    /// <summary>
    /// What a project root may hold besides its <c>.csproj</c> and its one composition file.
    /// <para>
    /// <c>Properties/</c> is here because the SDK and the IDE own it — it holds
    /// <c>launchSettings.json</c> and no code — and <c>Migrations/</c> because EF Core writes there.
    /// The other two are the shape every project has.
    /// </para>
    /// </summary>
    private static readonly string[] _projectRootFolders =
        ["Common", "Features", "Migrations", "Properties"];

    /// <summary>
    /// A project root holds its <c>.csproj</c>, at most one <c>.cs</c> — the DI module class, or a
    /// host's <c>Program.cs</c> — and folders.
    /// <para>
    /// A root is the one place in the tree where a file has no folder to be filed under, so it is
    /// the one place a file can be dropped without answering the question the layout asks of every
    /// other file. Which is why it is also where files land when nobody decides: the identity
    /// module carried loose adapters at its root for as long as nothing read the root.
    /// </para>
    /// </summary>
    [Fact]
    public void NoProjectRoot_HoldsAnythingButItsModule()
    {
        var checkedProjects = 0;
        var offenders = new List<string>();

        foreach (var project in ProjectReferenceGraph.SourceProjects.Values)
        {
            string root = RootOf(project);
            checkedProjects++;

            var loose = Directory
                .EnumerateFiles(root, "*.cs")
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal)
                .ToList();

            if (loose.Count > 1)
            {
                offenders.Add(
                    $"{project.Name}: {loose.Count} .cs files sit at its root " +
                    $"({string.Join(", ", loose)}); only its composition file may.");
            }
            else if (loose.Count == 1 && !IsCompositionFile(project.Name, loose[0]!))
            {
                offenders.Add(
                    $"{project.Name}: '{loose[0]}' sits at its root, which is neither 'Program.cs' " +
                    $"nor '{ModuleFileName(project.Name)}'.");
            }

            offenders.AddRange(Directory
                .EnumerateDirectories(root)
                .Select(Path.GetFileName)
                .Where(folder => folder is not "bin" and not "obj")
                .Where(folder => !_projectRootFolders.Contains(folder, StringComparer.Ordinal))
                .Select(folder => $"{project.Name}: '{folder}/' sits at its root, which is not one " +
                    $"of [{string.Join(", ", _projectRootFolders)}]"));
        }

        checkedProjects.ShouldBeGreaterThanOrEqualTo(
            9,
            "Fewer projects were found under Src than this template ships, so this rule read no " +
            "root at all and every root in it would report as clean.");

        offenders.Order(StringComparer.Ordinal).ShouldBeEmpty(
            "A folder even for a single file. The root is where a file goes when nobody decided " +
            "where it goes, so it holds the one file that composes the project and nothing else.");
    }

    /// <summary>
    /// A file-scoped namespace declaration. Anchored at column zero, and the only form the compiler
    /// accepts here — <c>csharp_style_namespace_declarations = file_scoped</c> is an error in
    /// .editorconfig — so a file this does not match declares no namespace at all.
    /// </summary>
    private static readonly Regex _fileScopedNamespace = new(
        @"^namespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*;",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Namespaces follow folders, with no exceptions: the namespace of a file is its project's name
    /// followed by the folders on the way to it.
    /// <para>
    /// This is what makes the two directions of navigation agree. A folder in the vocabulary tells a
    /// reader what nature of thing is in it, and the rules above hold that; but a using directive
    /// naming a folder that does not hold the file sends the next reader to the wrong place, and no
    /// compiler complains, because a namespace is a name and not a location.
    /// </para>
    /// </summary>
    [Fact]
    public void EverySourceFile_DeclaresTheNamespaceOfItsFolder()
    {
        var examined = 0;
        var offenders = new List<string>();

        foreach (var project in ProjectReferenceGraph.SourceProjects.Values)
        {
            string root = RootOf(project);

            foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (IsBuildOutput(file) || IsGenerated(file))
                {
                    continue;
                }

                examined++;

                // No project sets RootNamespace, so it is the project name, which is also the name
                // of the folder the project file sits in.
                string folder = Path.GetRelativePath(root, Path.GetDirectoryName(file)!);
                string expected = folder is "."
                    ? project.Name
                    : $"{project.Name}.{folder.Replace(Path.DirectorySeparatorChar, '.')}";

                string relative = Path.GetRelativePath(ProjectReferenceGraph.RepositoryRoot, file);
                var declaration = _fileScopedNamespace.Match(File.ReadAllText(file));

                if (!declaration.Success)
                {
                    // A host's Program.cs is top-level statements, which the compiler puts in the
                    // global namespace. There is nothing to compare, and the only way to give it a
                    // namespace is to give up the top-level form.
                    if (Path.GetFileName(file) is "Program.cs" && folder is ".")
                    {
                        continue;
                    }

                    offenders.Add($"{relative} declares no namespace; its folder asks for '{expected}'.");
                }
                else if (!string.Equals(declaration.Groups[1].Value, expected, StringComparison.Ordinal))
                {
                    offenders.Add(
                        $"{relative} declares '{declaration.Groups[1].Value}'; its folder asks for " +
                        $"'{expected}'.");
                }
            }
        }

        // Without this, a filter that matched nothing reports no offender and reads as a pass — and
        // the count is printed with the offenders below for the same reason: a rule that says how
        // many files it read is one whose silence can be checked.
        examined.ShouldBeGreaterThanOrEqualTo(
            600,
            "Far fewer source files were found than this template holds, so the walk is not reading " +
            "the tree it is meant to describe and every file in it would read as well named.");

        offenders.Order(StringComparer.Ordinal).ShouldBeEmpty(
            $"Namespaces follow folders. No exceptions. ({examined} files read.) A namespace that " +
            "names a folder the file is not in is a wrong direction given to every reader who " +
            "follows the using directive, and nothing else in the build will say so.");
    }

    /// <summary>
    /// A folder holding nothing is structure carrying no information: it suggests a concept the
    /// feature does not actually have, and a reader opening it learns only that they were misled.
    /// </summary>
    [Fact]
    public void NoFolderInTheSourceTree_IsEmpty()
    {
        string source = Path.Combine(ProjectReferenceGraph.RepositoryRoot, "Src");

        var walked = Directory
            .EnumerateDirectories(source, "*", SearchOption.AllDirectories)
            .Where(folder => !IsBuildOutput(folder))
            .ToList();

        // This rule asserts an emptiness, so a walk that found no folders at all passes it. Its two
        // siblings above establish their own candidate sets and would fail on a wrong root, but a
        // rule that leans on a neighbour to be non-vacuous is one that stops being a guarantee the
        // day the neighbour moves.
        walked.Count.ShouldBeGreaterThanOrEqualTo(
            200,
            "Far fewer folders were found than this template holds, so the walk is not reading the " +
            "tree it is meant to describe and every folder in it would read as non-empty.");

        var empty = walked
            .Where(folder => !Directory.EnumerateFileSystemEntries(folder).Any())
            .Select(folder => Path.GetRelativePath(ProjectReferenceGraph.RepositoryRoot, folder))
            .Order(StringComparer.Ordinal)
            .ToList();

        empty.ShouldBeEmpty("A folder exists only when it has contents.");
    }

    /// <summary>
    /// One top-level public type per file, and the file is named for it — which is what makes a type
    /// findable by its name alone, without a search across a tree of similar folders.
    /// <para>
    /// Generic overloads of one name (<c>IUseCase</c>, <c>IUseCase&lt;T&gt;</c>,
    /// <c>IUseCase&lt;T,R&gt;</c>) count once: they are one concept, and the arity is not part of what
    /// a reader looks for.
    /// </para>
    /// </summary>
    [Fact]
    public void EverySourceFile_DeclaresOnePublicType_NamedForTheFile()
    {
        string source = Path.Combine(ProjectReferenceGraph.RepositoryRoot, "Src");

        var files = Directory
            .EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Where(file => !IsGenerated(file))
            .ToList();

        files.Count.ShouldBeGreaterThanOrEqualTo(
            300,
            "Far fewer source files were found than this template holds, so the walk is not reading " +
            "the tree it is meant to describe.");

        var offenders = new List<string>();

        foreach (string file in files)
        {
            var declared = PublicTypesIn(file);

            if (declared.Count == 0)
            {
                continue;
            }

            string relative = Path.GetRelativePath(ProjectReferenceGraph.RepositoryRoot, file);
            string expected = Path.GetFileNameWithoutExtension(file);

            if (declared.Count > 1)
            {
                offenders.Add(
                    $"{relative} declares {declared.Count} public types " +
                    $"({string.Join(", ", declared.Order(StringComparer.Ordinal))}).");
            }
            else if (!declared.Contains(expected))
            {
                offenders.Add($"{relative} declares '{declared.First()}'.");
            }
        }

        offenders.Order(StringComparer.Ordinal).ShouldBeEmpty(
            "A file holds one public type and is named for it. A second one hides behind a name that " +
            "does not mention it, and a mismatched name makes the type unfindable by the only thing a " +
            "reader knows about it.");
    }

    /// <summary>
    /// Proves the file walk above can fail, by running its predicate over a file written to break
    /// the rule. Without this, a walk that silently matched nothing would report no offenders and
    /// read as a pass.
    /// </summary>
    [Fact]
    public void TheOneTypePerFileRule_IsSensitive_AndSeesASecondType()
    {
        string probe = Path.Combine(Path.GetTempPath(), $"{nameof(LayoutConventionTests)}.probe.cs");

        File.WriteAllText(
            probe,
            """
            namespace Probe;

            public sealed class First
            {
                public sealed class Nested;
            }

            public sealed record Second(string Value);
            """);

        try
        {
            var declared = PublicTypesIn(probe);

            declared.ShouldBe(
                ["First", "Second"],
                ignoreOrder: true,
                "The predicate must see both top-level types and neither the nested one nor anything " +
                "else. Seeing one would make the real rule vacuous; seeing three would make it noisy.");
        }
        finally
        {
            File.Delete(probe);
        }
    }

    /// <summary>The directory holding a project file, which is the project's root.</summary>
    private static string RootOf(ProjectNode project) =>
        Path.GetDirectoryName(Path.Combine(ProjectReferenceGraph.RepositoryRoot, project.RelativePath))!;

    /// <summary>
    /// The DI module class a project's name asks for: the last segment of the name, plus
    /// <c>Module</c>. <c>AppTemplate.Infrastructure.Persistence</c> composes itself in
    /// <c>PersistenceModule</c>, which is how <c>AddPersistenceModule</c> is findable from the call.
    /// </summary>
    private static string ModuleFileName(string projectName) =>
        $"{projectName[(projectName.LastIndexOf('.') + 1)..]}Module.cs";

    private static bool IsCompositionFile(string projectName, string fileName) =>
        fileName is "Program.cs"
        || string.Equals(fileName, ModuleFileName(projectName), StringComparison.Ordinal);

    private static HashSet<string> PublicTypesIn(string file) =>
        [.. _publicTypeDeclaration
            .Matches(File.ReadAllText(file))
            .Select(match => match.Groups[1].Value)];

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    /// <summary>
    /// EF Core writes migrations, and their shape is the tool's to decide, not this template's.
    /// </summary>
    private static bool IsGenerated(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}
