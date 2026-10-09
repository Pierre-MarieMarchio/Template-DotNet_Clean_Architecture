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
    private const string _productPrefix = "AppTemplate";

    /// <summary>
    /// The folders a feature may hold, per layer. Closed on purpose: a word outside this list is a
    /// concept a reader has to infer from its contents, and one feature inventing a word the others
    /// do not use is how a layout stops being an index. Adding a word is an edit to
    /// docs/PROJECT-LAYOUT.md's folder map and to this list, argued for in the pull request — not a
    /// mkdir.
    /// </summary>
    private static readonly Dictionary<string, string[]?> _vocabulary = new(StringComparer.Ordinal)
    {
        ["Src/Application/AppTemplate.Application"] =
            [
                "Consumers", "Dtos", "Errors", "Extensions", "Mapping",
                //#if (files)
                "Options",
                //#endif
                "Policies", "Ports", "Services", "UseCases", "Validators",
            ],

        // One vertical, holding one operation. A mechanism project earns a Features/ folder only for
        // work that is genuinely a use case rather than a mechanism — here, purging what the
        // idempotency store has let expire.
        ["Src/Application/AppTemplate.Application.Core"] = ["UseCases"],
        //#if (localAuth)

        // Four words, and no Services/ or Consumers/: this feature registers no service and
        // answers no domain event. What it offers is use cases over ports an identity module
        // satisfies.
        ["Src/Application/AppTemplate.Application.Auth"] =
            ["Errors", "Policies", "Ports", "UseCases"],
        //#endif
        ["Src/Domain/AppTemplate.Domain"] =
            ["Entities", "Events", "Repositories", "ValueObjects"],

        // Null, not empty, and the difference is the point: this project has no Features/ folder to
        // hold a word, and it may not grow one. What is here is agnostic of every feature — that is
        // the whole claim of the project — so a feature folder appearing here is the defect, and
        // this entry is what reports it. A shared *business* concept belongs in AppTemplate.Domain.
        ["Src/Domain/AppTemplate.Domain.Core"] = null,
        ["Src/Infrastructure/AppTemplate.Infrastructure.Persistence"] =
            [
                //#if (files)
                "Checkpoints",
                //#endif
                "Configurations",
                //#if (files)
                "Locks",
                //#endif
                "Mapping", "Models", "Observability", "Queries", "Repositories", "Stores", "Tracking",
            ],
        // Jobs/ holds what a feature runs on a timer: the hosted service, its options and its
        // instruments, side by side — the nature word for them, as Controllers/ is for the HTTP half.
        // Directories/ holds an adapter the host supplies because it joins two features that may
        // not name each other. Composition/ holds a feature's one entry point when it composes more
        // than use cases — a module, a readiness check, a policy — so a host can leave it out whole.
        // Options/ holds the settings a feature binds and the framework options it configures from
        // them, when they are not a job's.
        ["Src/Presentation/AppTemplate.Api"] =
            ["Composition", "Contracts", "Controllers", "Directories", "Jobs", "Mapping", "Options"],
        //#if (localAuth)

        // Every word here is the plural of a nature word the file names already carry — the rule the
        // rest of the repository follows, where a …Repository is in Repositories/ and a …Tracker in
        // Tracking/. Which is what makes a folder findable from a type name alone and back again: a
        // …Service is in Services/ and nowhere else, and Services/ holds nothing that is not one.
        ["Src/Infrastructure/AppTemplate.Infrastructure.Auth"] =
            ["Configurations", "Directories", "Factories", "Issuers", "Logs", "Models", "Options", "Providers",
             "Seeding", "Services", "Tables", "Templates", "Verifiers"],
        //#endif
        //#if (files)
        ["Src/Infrastructure/AppTemplate.Infrastructure.Storage"] =
            ["Inventories", "Options", "Scanners", "Stores", "Transfers"],
        //#endif

        // Null, not empty: this project has no Features/ and may not grow one. What is here
        // is what any host needs whatever its transport, so a feature folder appearing would
        // be a transport concern filed as a shared one.
        ["Src/Presentation/AppTemplate.Presentation.Core"] = null,

        // Same reason, one layer down. These two modules have both a transverse adapter and a
        // feature-scoped one, which is what earns them Common/ and Features/ at all; the feature
        // half is one adapter and its recording double, side by side — including the reminder mail's
        // templates, which sit beside the notifier rather than earning a folder of their own for two
        // files.
        ["Src/Infrastructure/AppTemplate.Infrastructure.Email"] = [],
        ["Src/Infrastructure/AppTemplate.Infrastructure.InMemory"] = [],

        // Null, not empty, for the reason the other Core projects carry it: what is here is a
        // mechanism no module owns, so a feature folder appearing would be one module's business
        // filed as everybody's.
        ["Src/Infrastructure/AppTemplate.Infrastructure.Core"] = null,
    };

    /// <summary>
    /// The projects a feature is added to whose <c>Features/</c> a project generated without one does
    /// not have yet. Listed only in such a project: anywhere else a missing <c>Features/</c> is the
    /// stale vocabulary entry the rule reports.
    /// </summary>
    private static readonly string[] _featuresStillToCome =
    [
        /*#if (aggregates)*/
        /*#else
        "Src/Domain/AppTemplate.Domain",
        #endif*/
        /*#if (anyFeature)*/
        /*#else
        "Src/Application/AppTemplate.Application",
        "Src/Infrastructure/AppTemplate.Infrastructure.Persistence",
        #endif*/
        /*#if (reminders)*/
        /*#else
        "Src/Infrastructure/AppTemplate.Infrastructure.Email",
        #endif*/
        /*#if (localAuth || reminders || files)*/
        /*#else
        "Src/Infrastructure/AppTemplate.Infrastructure.InMemory",
        #endif*/
    ];

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
    private static readonly Dictionary<string, string[]?> _commonVocabulary = new(StringComparer.Ordinal)
    {
        ["Src/Domain/AppTemplate.Domain.Core"] =
            ["Abstractions", "Events", "Exceptions", "Primitives"],

        /*#if (tagging)*/
        // The business half of this layer's Common/, and the word names a subject rather than a
        // nature: what a to-do item and a stored file share is that they are tagged, and that they
        // are tagged by the same rule. It could not sit one project inwards — TagSet names Tag, and
        // a Core project may name no business type — which is the test that decides between the two
        // kinds of Common/ rather than a preference about where it reads better.
        ["Src/Domain/AppTemplate.Domain"] = ["Tagging"],
        /*#else
        ["Src/Domain/AppTemplate.Domain"] = null,
        #endif*/

        // No "Abstractions": every interface here is one, so the word sorted by nothing. What the
        // layer declares for something else to implement is a Ports/, at both scopes — the same word
        // Features/<F>/Ports/ uses — and the two interfaces the layer implements itself sit with the
        // subject they are about instead.
        ["Src/Application/AppTemplate.Application.Core"] =
            ["Collections", "Concurrency", "Events", "Idempotency", "Localization", "Ownership",
             "Policies", "Ports", "Results", "UseCases", "Validation"],

        /*#if (tagging)*/
        // The business half of this layer's Common/, and it pairs with the domain's: what several
        // features share as business here is the input validation over the domain's tag rules. It
        // could not sit one project inwards either — TagValidation names Tag, and a Core project
        // may name no business type — which is again the test that decides between the two kinds
        // rather than a preference.
        ["Src/Application/AppTemplate.Application"] = ["Tagging"],
        /*#else
        ["Src/Application/AppTemplate.Application"] = null,
        #endif*/

        //#if (localAuth)
        // One feature, so nothing is shared *between* features here. A Common/ would only mean
        // this project had grown a second subject, which is the thing to argue about rather than
        // the folder.
        ["Src/Application/AppTemplate.Application.Auth"] = null,
        //#endif

        // The agnostic half of this layer: a mechanism a module needs and no module owns. A module
        // may not reference a sibling, so without this project two modules needing one mechanism
        // each keep a copy.
        ["Src/Infrastructure/AppTemplate.Infrastructure.Core"] =
            ["Caching", "Contexts", "Options", "Saving", "Templating", "Time"],

        ["Src/Infrastructure/AppTemplate.Infrastructure.Email"] =
            ["Http", "Smtp"],
        ["Src/Infrastructure/AppTemplate.Infrastructure.InMemory"] =
            ["Email", "Time"],
        //#if (localAuth)
        ["Src/Infrastructure/AppTemplate.Infrastructure.Auth"] =
            ["Contexts", "Directories", "Options"],
        //#endif
        ["Src/Infrastructure/AppTemplate.Infrastructure.Persistence"] =
            ["Contexts", "Idempotency", "Leases"],
        //#if (files)
        ["Src/Infrastructure/AppTemplate.Infrastructure.Storage"] =
            ["Budgets", "Factories", "Options"],
        //#endif
        // The HTTP mechanisms no feature owns: what a controller inherits, how a failure is
        // answered, which headers every response carries, what the host composes at start-up. What
        // a folder here may not do is name a feature — HttpCommonBoundaryTests holds that — so
        // a word on this list is a mechanism, never a subject.
        ["Src/Presentation/AppTemplate.Api"] =
            [
                "Caching", "Concurrency", "Contracts", "Controllers", "Errors", "Hosting",
                "Idempotency", "Jobs", "Localization", "Observability",
                //#if (!publicApi)
                "OpenApi",
                //#endif
                "Security",
            ],

        // The five subjects a host needs whatever its transport: recurring work, the language a
        // flow is written in, telemetry, an outbound budget, and the identity of a process with no
        // caller.
        ["Src/Presentation/AppTemplate.Presentation.Core"] =
            ["Jobs", "Localization", "Observability", "Outbound", "Security"],
    };

    [Fact]
    public void EveryCommonFolder_IsNamedFromItsProjectsVocabulary()
    {
        var offenders = new List<string>();

        foreach ((string project, string[]? allowed) in _commonVocabulary)
        {
            if (!ShouldWalk(project, "Common", allowed, offenders))
            {
                continue;
            }

            string common = Path.Combine(ProjectReferenceGraph.RepositoryRoot, project, "Common");

            offenders.AddRange(Directory
                .EnumerateDirectories(common)
                .Select(Path.GetFileName)
                .Where(folder => folder is not null && !allowed!.Contains(folder, StringComparer.Ordinal))
                .Select(folder => $"{project}: 'Common/{folder}' is not one of " +
                    $"[{string.Join(", ", allowed!)}]"));

            // The other direction, and the one a list of words rots in: a word nothing is filed
            // under any more. It cannot fail the check above — that one only reads folders — so a
            // vocabulary keeps offering a concept the tree stopped having, and the next reader
            // takes the list for the index it claims to be.
            var present = Directory
                .EnumerateDirectories(common)
                .Select(Path.GetFileName)
                .ToHashSet(StringComparer.Ordinal);

            offenders.AddRange(allowed!
                .Where(word => !present.Contains(word))
                .Select(word => $"{project}: 'Common/{word}' is in this project's vocabulary and " +
                    "no such folder exists. Drop the word, or say which of the two kinds of " +
                    "Common/ the folder it named belonged to."));

            offenders.AddRange(Directory
                .EnumerateFiles(common, "*.cs")
                .Select(file => $"{project}: 'Common/{Path.GetFileName(file)}' sits loose at the " +
                    "root of Common, which names no responsibility at all"));
        }

        offenders.Order(StringComparer.Ordinal).ShouldBeEmpty(
            "Common/ is the half of a project that knows no feature, and a word invented there is " +
            "read by everyone. Adding one is an edit to this list and to docs/PROJECT-LAYOUT.md's " +
            "folder map, argued for in the pull request.");
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
        var offenders = new List<string>();

        foreach ((string project, string[]? allowed) in _vocabulary)
        {
            if (_featuresStillToCome.Contains(project, StringComparer.Ordinal)
                && !Directory.Exists(Path.Combine(ProjectReferenceGraph.RepositoryRoot, project, "Features")))
            {
                continue;
            }

            if (!ShouldWalk(project, "Features", allowed, offenders))
            {
                continue;
            }

            string features = Path.Combine(ProjectReferenceGraph.RepositoryRoot, project, "Features");

            offenders.AddRange(Directory
                .EnumerateDirectories(features)
                .SelectMany(feature => Directory.EnumerateDirectories(feature))
                .Where(folder => !allowed!.Contains(Path.GetFileName(folder), StringComparer.Ordinal))
                .Select(folder => $"{project}: '{Path.GetRelativePath(features, folder)}' is not one of " +
                    $"[{string.Join(", ", allowed!)}]"));

            //#if (todoLists && notes)
            // The other direction: a word no feature is filed under any more. Not every feature has
            // every word — one having no Consumers/ is normal — so the claim is that *some* feature
            // does. A word none does is a concept the vocabulary still offers and the tree stopped
            // having, which is how a list of words quietly stops being an index. Checked only where
            // every example is present: a project generated with fewer starts with words no feature
            // uses yet, and the scaffolded features still need them.
            var used = Directory
                .EnumerateDirectories(features)
                .SelectMany(feature => Directory.EnumerateDirectories(feature))
                .Select(Path.GetFileName)
                .ToHashSet(StringComparer.Ordinal);

            offenders.AddRange(allowed!
                .Where(word => !used.Contains(word))
                .Select(word => $"{project}: '{word}' is in this layer's vocabulary and no feature " +
                    "is filed under it. Drop the word, or file something under it."));
            //#endif

            // The same second pass its sibling above makes over Common/'s loose files, and it has to
            // be conditional where that one does not: a project whose list is empty says by saying so
            // that its features hold their files side by side, which is the documented shape for the
            // two smallest infrastructure modules. Running the pass there would
            // fail twenty-one correct files. Where a word does exist, a file lying beside the folders
            // rather than in one is filed under nothing.
            if (allowed!.Length == 0)
            {
                continue;
            }

            offenders.AddRange(Directory
                .EnumerateDirectories(features)
                .SelectMany(feature => Directory.EnumerateFiles(feature, "*.cs"))
                .Select(file => $"{project}: '{Path.GetRelativePath(features, file)}' sits loose at " +
                    $"the root of its feature, under none of [{string.Join(", ", allowed)}]"));
        }

        offenders.Order(StringComparer.Ordinal).ShouldBeEmpty(
            "A folder outside its layer's vocabulary makes the reader infer a concept from the files " +
            "inside it, and lets one feature be organised unlike every other. A file loose beside " +
            "those folders is the same defect from the other side: the vocabulary stops describing " +
            "the feature the moment part of it is filed under no word at all.");
    }

    /// <summary>
    /// The two vocabularies above, against every project that actually exists on disk.
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
    public void EveryProjectOnDisk_HasAVocabularyOfItsOwn()
    {
        var onDisk = ProjectReferenceGraph.SourceProjects.Values
            .Select(project => Path.GetDirectoryName(project.RelativePath)!.Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

        onDisk.Count.ShouldBeGreaterThanOrEqualTo(
            10,
            "Far fewer projects were found under Src than this template holds, so the project walk " +
            "is not reading the tree it is meant to describe and every project in it would read as " +
            "listed.");

        var unlisted = onDisk
            .Where(project => !_vocabulary.ContainsKey(project) || !_commonVocabulary.ContainsKey(project))
            .Order(StringComparer.Ordinal)
            .ToList();

        unlisted.ShouldBeEmpty(
            "A project exists that one of the two vocabularies does not name, so the rule that reads " +
            "it walks past that project and passes. Give it an entry in each — empty when its " +
            "features hold their files side by side, as Email and InMemory do — and write the words " +
            "into docs/PROJECT-LAYOUT.md's folder map.");
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
        int checkedProjects = 0;
        var offenders = new List<string>();

        foreach (var project in ProjectReferenceGraph.SourceProjects.Values)
        {
            string root = ProjectReferenceGraph.RootOf(project);
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
                    $"{project.Name}: '{loose[0]}' sits at its root, which is none of 'Program.cs', " +
                    $"'{ModuleFileName(project.Name)}' or " +
                    $"'{QualifiedModuleFileName(project.Name)}'.");
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
            10,
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
        int examined = 0;
        var offenders = new List<string>();

        foreach (var project in ProjectReferenceGraph.SourceProjects.Values)
        {
            string root = ProjectReferenceGraph.RootOf(project);

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
            167,
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
            101,
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
            167,
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

    /// <summary>
    /// Whether the walk goes on into <paramref name="folderName"/>, recording a complaint first when
    /// the tree and the vocabulary disagree about that folder existing at all.
    /// <para>
    /// A <see langword="null"/> vocabulary is a project stating it has no such folder, and the claim
    /// is checked rather than taken. The two ways either list rots are a folder that appeared where
    /// the list says there is none, and a folder that vanished from under a list of words — the same
    /// defect from both sides, the vocabulary no longer describing the tree, and neither is visible
    /// in a count of how many projects the walk managed to read.
    /// </para>
    /// </summary>
    private static bool ShouldWalk(
        string project,
        string folderName,
        string[]? allowed,
        List<string> offenders)
    {
        bool exists = Directory.Exists(
            Path.Combine(ProjectReferenceGraph.RepositoryRoot, project, folderName));

        if (allowed is null)
        {
            if (exists)
            {
                offenders.Add(
                    $"{project}: '{folderName}/' exists, and this project is listed as having none. " +
                    "Decide which of the two it is first: something that knows no feature belongs in " +
                    "this layer's Core project, and something several features share as business " +
                    "belongs here. Then give it the words it holds, and write them into " +
                    "docs/PROJECT-LAYOUT.md's folder map.");
            }

            return false;
        }

        if (!exists)
        {
            offenders.Add(
                $"{project}: '{folderName}/' does not exist, and this project is listed with " +
                $"[{string.Join(", ", allowed)}]. Set its entry to null if it genuinely has none.");
        }

        return exists;
    }

    /// <summary>
    /// The DI module class a project's name asks for: the last segment of the name, plus
    /// <c>Module</c>. <c>AppTemplate.Infrastructure.Persistence</c> composes itself in
    /// <c>PersistenceModule</c>, which is how <c>AddPersistenceModule</c> is findable from the call.
    /// </summary>
    private static string ModuleFileName(string projectName) =>
        $"{projectName[(projectName.LastIndexOf('.') + 1)..]}Module.cs";

    /// <summary>
    /// The same class named from the whole project, product prefix stripped and dots removed:
    /// <c>AppTemplate.Application.Core</c> composes itself in <c>ApplicationCoreModule</c>.
    /// <para>
    /// A second accepted form rather than a replacement, because the rule's purpose is that
    /// <c>AddApplicationCore</c> be findable from its call site, and the last segment alone would
    /// demand a <c>CoreModule</c> — a name three projects would share and none of them would be
    /// found under.
    /// </para>
    /// </summary>
    private static string QualifiedModuleFileName(string projectName) =>
        $"{projectName.Replace($"{_productPrefix}.", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)}Module.cs";

    private static bool IsCompositionFile(string projectName, string fileName) =>
        fileName is "Program.cs"
        || string.Equals(fileName, ModuleFileName(projectName), StringComparison.Ordinal)
        || string.Equals(fileName, QualifiedModuleFileName(projectName), StringComparison.Ordinal);

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
