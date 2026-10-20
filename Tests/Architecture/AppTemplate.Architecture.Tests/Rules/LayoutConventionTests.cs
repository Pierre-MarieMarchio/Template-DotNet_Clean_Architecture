using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class LayoutConventionTests
{
    private const string _productPrefix = "AppTemplate";

    private static readonly Dictionary<string, string[]?> _vocabulary = new(StringComparer.Ordinal)
    {
        ["Src/Application/AppTemplate.Application"] =
            [
                //#if (files)
                "Configuration/Options",
                //#endif
                "Helpers",
                "Model/Dtos", "Model/Errors", "Model/Mapping", "Model/Policies",
                "Ports", "Services/Consumers", "UseCases",
            ],
        ["Src/Application/AppTemplate.Application.Core"] = ["UseCases"],
        //#if (localAuth)
        ["Src/Application/AppTemplate.Application.Auth"] =
            ["Model/Errors", "Model/Policies", "Ports", "UseCases"],
        //#endif
        ["Src/Domain/AppTemplate.Domain"] =
            ["Model/Entities", "Model/Events", "Model/ValueObjects", "Repositories"],
        ["Src/Domain/AppTemplate.Domain.Core"] = null,
        ["Src/Infrastructure/AppTemplate.Infrastructure.Persistence"] =
            [
                "Access/Queries", "Access/Repositories", "Access/Stores",
                //#if (files)
                "Adapters/Checkpoints", "Adapters/Locks",
                //#endif
                "Adapters/Observability",
                "Rows/Configurations", "Rows/Mapping", "Rows/Models", "Rows/Tracking",
            ],
        ["Src/Presentation/AppTemplate.Api"] =
            [
                "Composition",
                //#if (externalAuth)
                "Configuration/Options",
                //#endif
                "Contracts/Mapping", "Contracts/Requests", "Contracts/Responses",
                "Controllers", "Jobs",
            ],
        //#if (localAuth)
        ["Src/Infrastructure/AppTemplate.Infrastructure.Auth"] =
            ["Access/Queries", "Access/Tables",
             "Adapters/Directories", "Adapters/Providers", "Adapters/Services",
             "Configuration/Options", "Configuration/Seeding",
             "Resources/Templates",
             "Rows/Configurations", "Rows/Models"],
        //#endif
        //#if (files)
        ["Src/Infrastructure/AppTemplate.Infrastructure.Storage"] =
            ["Access/Stores",
             "Adapters/Inventories", "Adapters/Scanners", "Adapters/Transfers",
             "Configuration/Options"],
        //#endif

        ["Src/Presentation/AppTemplate.Presentation.Core"] = null,
        ["Src/Infrastructure/AppTemplate.Infrastructure.Email"] = [],
        ["Src/Infrastructure/AppTemplate.Infrastructure.InMemory"] = [],
        ["Src/Infrastructure/AppTemplate.Infrastructure.Core"] = null,
    };

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

    private static readonly Dictionary<string, string[]?> _commonVocabulary = new(StringComparer.Ordinal)
    {
        ["Src/Domain/AppTemplate.Domain.Core"] =
            ["Abstractions", "Events", "Exceptions", "Primitives"],

        /*#if (tagging)*/
        ["Src/Domain/AppTemplate.Domain"] = ["Tagging"],
        /*#else
        ["Src/Domain/AppTemplate.Domain"] = null,
        #endif*/

        ["Src/Application/AppTemplate.Application.Core"] =
            ["Collections", "Concurrency", "Events", "Idempotency", "Localization", "Ownership",
             "Policies", "Ports", "Results", "UseCases", "Validation"],

        /*#if (tagging)*/
        ["Src/Application/AppTemplate.Application"] = ["Tagging"],
        /*#else
        ["Src/Application/AppTemplate.Application"] = null,
        #endif*/

        //#if (localAuth)
        ["Src/Application/AppTemplate.Application.Auth"] = null,
        //#endif

        ["Src/Infrastructure/AppTemplate.Infrastructure.Core"] =
            ["Caching", "Contexts", "Options", "Saving", "Templating", "Time"],

        ["Src/Infrastructure/AppTemplate.Infrastructure.Email"] =
            ["Http", "Smtp"],
        ["Src/Infrastructure/AppTemplate.Infrastructure.InMemory"] =
            ["Email", "Time"],
        //#if (localAuth)
        ["Src/Infrastructure/AppTemplate.Infrastructure.Auth"] =
            ["Contexts", "Options"],
        //#endif
        ["Src/Infrastructure/AppTemplate.Infrastructure.Persistence"] =
            ["Contexts", "Idempotency", "Leases"],
        //#if (files)
        ["Src/Infrastructure/AppTemplate.Infrastructure.Storage"] =
            ["Budgets", "Factories", "Options"],
        //#endif
        ["Src/Presentation/AppTemplate.Api"] =
            [
                "Caching", "Concurrency", "Contracts", "Controllers", "Errors", "Hosting",
                "Idempotency", "Jobs", "Localization", "Observability",
                //#if (!publicApi)
                "OpenApi",
                //#endif
                "Security",
            ],
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
            var levelTwo = allowed!.Select(ConceptOf).ToHashSet(StringComparer.Ordinal);
            var natures = allowed!
                .Where(word => word.Contains('/', StringComparison.Ordinal))
                .GroupBy(ConceptOf, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

            var folders = Directory
                .EnumerateDirectories(features)
                .SelectMany(feature => Directory.EnumerateDirectories(feature))
                .ToList();

            offenders.AddRange(folders
                .Where(folder => !levelTwo.Contains(Path.GetFileName(folder)))
                .Select(folder => $"{project}: '{Path.GetRelativePath(features, folder)}' is not one of " +
                    $"[{string.Join(", ", levelTwo.Order(StringComparer.Ordinal))}]"));

            var natureFolders = folders
                .Where(folder => natures.ContainsKey(Path.GetFileName(folder)))
                .SelectMany(concept => Directory.EnumerateDirectories(concept))
                .ToList();

            offenders.AddRange(natureFolders
                .Where(folder => !allowed!.Contains(WordOf(folder), StringComparer.Ordinal))
                .Select(folder => $"{project}: '{Path.GetRelativePath(features, folder)}' is not one of " +
                    $"[{string.Join(", ", natures[ConceptOf(WordOf(folder))])}]"));

            //#if (todoLists && notes)
            var used = folders
                .Select(Path.GetFileName)
                .Concat(natureFolders.Select(WordOf))
                .ToHashSet(StringComparer.Ordinal);

            offenders.AddRange(allowed!
                .Where(word => !used.Contains(word))
                .Select(word => $"{project}: '{word}' is in this layer's vocabulary and no feature " +
                    "is filed under it. Drop the word, or file something under it."));
            //#endif

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

    private static readonly string[] _projectRootFolders =
        ["Common", "Features", "Migrations", "Properties"];

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

    private static readonly Regex _fileScopedNamespace = new(
        @"^namespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*;",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

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

                string folder = Path.GetRelativePath(root, Path.GetDirectoryName(file)!);
                string expected = folder is "."
                    ? project.Name
                    : $"{project.Name}.{folder.Replace(Path.DirectorySeparatorChar, '.')}";

                string relative = Path.GetRelativePath(ProjectReferenceGraph.RepositoryRoot, file);
                var declaration = _fileScopedNamespace.Match(File.ReadAllText(file));

                if (!declaration.Success)
                {
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

        examined.ShouldBeGreaterThanOrEqualTo(
            167,
            "Far fewer source files were found than this template holds, so the walk is not reading " +
            "the tree it is meant to describe and every file in it would read as well named.");

        offenders.Order(StringComparer.Ordinal).ShouldBeEmpty(
            $"Namespaces follow folders. No exceptions. ({examined} files read.) A namespace that " +
            "names a folder the file is not in is a wrong direction given to every reader who " +
            "follows the using directive, and nothing else in the build will say so.");
    }

    [Fact]
    public void NoFolderInTheSourceTree_IsEmpty()
    {
        string source = Path.Combine(ProjectReferenceGraph.RepositoryRoot, "Src");

        var walked = Directory
            .EnumerateDirectories(source, "*", SearchOption.AllDirectories)
            .Where(folder => !IsBuildOutput(folder))
            .ToList();

        walked.Count.ShouldBeGreaterThanOrEqualTo(
            90,
            "Far fewer folders were found than this template holds, so the walk is not reading the " +
            "tree it is meant to describe and every folder in it would read as non-empty.");

        var empty = walked
            .Where(folder => !Directory.EnumerateFileSystemEntries(folder).Any())
            .Select(folder => Path.GetRelativePath(ProjectReferenceGraph.RepositoryRoot, folder))
            .Order(StringComparer.Ordinal)
            .ToList();

        empty.ShouldBeEmpty("A folder exists only when it has contents.");
    }

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

    private static string ModuleFileName(string projectName) =>
        $"{projectName[(projectName.LastIndexOf('.') + 1)..]}Module.cs";

    private static string QualifiedModuleFileName(string projectName) =>
        $"{projectName.Replace($"{_productPrefix}.", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)}Module.cs";

    private static string ConceptOf(string word) =>
        word.Split('/')[0];

    private static string WordOf(string natureFolder) =>
        $"{Path.GetFileName(Path.GetDirectoryName(natureFolder))}/{Path.GetFileName(natureFolder)}";

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

    private static bool IsGenerated(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}
