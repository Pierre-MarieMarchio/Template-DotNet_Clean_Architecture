using System.Text.RegularExpressions;

namespace AppTemplate.Architecture.Tests.Fixtures;

/// <summary>
/// The populations this suite checks, read off the source tree instead of written down.
/// <para>
/// A rule that says "every use case" needs a number to compare against, and a number typed into a
/// floor is only correct on the day it is typed: it cannot be wrong, it can only go stale, and a
/// stale floor lets the rule pass while guarding a fraction of what it names. Raising it would only
/// reset the clock. So the number is measured instead — here, from the folders and the declarations
/// a reviewer reads — and the rules assert that the reflection-based discovery finds the <em>same
/// population</em>. Two independent paths to one truth: when either stops matching, they diverge,
/// and the rule names what only one of them saw.
/// </para>
/// <para>
/// Every match below is on a <em>declaration</em>, never on a file or type name. A candidate filter
/// written over names is how <c>InspectDepositedFilesUseCase</c> and <c>IssueFileDownloadUseCase</c>
/// disappear from a walk meant to skip interfaces: they are classes whose names legitimately begin
/// with an I, and nothing says so out loud.
/// </para>
/// </summary>
internal static class SourceDeclarations
{
    /// <summary>
    /// A non-abstract class declaration whose name ends in <c>UseCase</c>. <c>abstract</c> is
    /// deliberately absent from the modifiers this accepts, so that an abstract base is invisible
    /// here exactly as it is to the reflection discovery, which excludes it too.
    /// </summary>
    private static readonly Regex _useCaseClassDeclaration = new(
        @"^[\t ]*(?:public|internal)?[\t ]*(?:sealed[\t ]+)?(?:partial[\t ]+)?class[\t ]+([A-Za-z0-9_]+UseCase)\b",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _publicInterfaceDeclaration = new(
        @"^[\t ]*public[\t ]+(?:partial[\t ]+)?interface[\t ]+([A-Za-z0-9_]+)",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static readonly string _applicationRoot = Path.Combine(
        ProjectReferenceGraph.RepositoryRoot, "Src", "Application", "AppTemplate.Application");

    private static readonly string _domainRoot = Path.Combine(
        ProjectReferenceGraph.RepositoryRoot, "Src", "Domain", "AppTemplate.Domain");

    private static readonly string _featuresRoot = Path.Combine(_applicationRoot, "Features");

    /// <summary>The verticals the application layer has, by folder name.</summary>
    internal static IReadOnlyList<string> Verticals { get; } = SubfolderNamesOf(_featuresRoot);

    /// <summary>
    /// Every use-case class the application layer declares, by the full name it compiles to, so a
    /// comparison against reflection is a comparison of identities rather than of counts.
    /// </summary>
    internal static IReadOnlyList<string> UseCaseFullNames { get; }

    /// <summary>
    /// Every folder under a vertical's <c>UseCases</c> that declares a use case, as the namespace
    /// the layout maps it to. This is the operation index a reader navigates by.
    /// </summary>
    internal static IReadOnlyList<string> UseCaseOperationNamespaces { get; }

    /// <summary>
    /// Every public interface declared where the port convention puts one: under a vertical's
    /// <c>Ports</c>, or anywhere under <c>Common</c>. This is the raw population — the markers
    /// <see cref="ApplicationPorts.NotPorts"/> names are in here too, because they are declared
    /// there, and a rule comparing the two sides has to add them back rather than re-derive the
    /// exclusion and drift from it.
    /// </summary>
    internal static IReadOnlyList<string> PortFullNames { get; }

    /// <summary>Every public interface declared in a <c>Repositories</c> folder in the Domain.</summary>
    internal static IReadOnlyList<string> DomainRepositoryFullNames { get; }

    /// <summary>
    /// What is wrong with the walk itself, rather than with what it found. A comparison between two
    /// empty populations holds, so every rule reading this asserts here first: a wrong root, a
    /// renamed folder or a declaration pattern that has stopped matching must be told apart from
    /// agreement.
    /// </summary>
    internal static IReadOnlyList<string> WalkComplaints { get; }

    static SourceDeclarations()
    {
        // The whole project, not only the UseCases folders: a use case declared somewhere else is
        // still one reflection will find, and the rule about where it belongs is a different rule
        // whose failure this one must not pre-empt by quietly not seeing the type at all.
        var useCases = DeclarationsUnder(
            Directory.Exists(_applicationRoot) ? [_applicationRoot] : [],
            _useCaseClassDeclaration,
            _applicationRoot,
            "AppTemplate.Application");

        UseCaseFullNames = [.. useCases.Order(StringComparer.Ordinal)];

        UseCaseOperationNamespaces =
            [.. useCases
                .Select(fullName => fullName[..fullName.LastIndexOf('.')])
                .Where(@namespace => @namespace.Contains(".UseCases.", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)];

        PortFullNames =
            [.. DeclarationsUnder(
                    Verticals
                        .Select(vertical => Path.Combine(_featuresRoot, vertical, "Ports"))
                        .Append(Path.Combine(_applicationRoot, "Common"))
                        .Where(Directory.Exists),
                    _publicInterfaceDeclaration,
                    _applicationRoot,
                    "AppTemplate.Application")
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)];

        DomainRepositoryFullNames =
            [.. DeclarationsUnder(
                    Directory.Exists(_domainRoot)
                        ? Directory.EnumerateDirectories(_domainRoot, "Repositories", SearchOption.AllDirectories)
                        : [],
                    _publicInterfaceDeclaration,
                    _domainRoot,
                    "AppTemplate.Domain")
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)];

        WalkComplaints = ReadWalkComplaints();
    }

    /// <summary>
    /// The two-sided difference between what the source tree declares and what a discovery found,
    /// stated from both sides: a name only the tree has means the discovery has stopped matching it,
    /// a name only the discovery has means the tree has moved. Empty when the two agree.
    /// </summary>
    /// <param name="inTheSourceTree">Full names read from the tree by one of the walks above.</param>
    /// <param name="discovered">Full names the rule's own discovery produced, arity stripped.</param>
    /// <param name="discoveredBy">How the discovery reads, for the failure message.</param>
    internal static List<string> Divergence(
        IEnumerable<string> inTheSourceTree,
        IEnumerable<string> discovered,
        string discoveredBy)
    {
        var declared = inTheSourceTree.ToHashSet(StringComparer.Ordinal);
        var found = discovered.ToHashSet(StringComparer.Ordinal);

        return
        [
            .. declared.Except(found, StringComparer.Ordinal)
                .Select(name => $"the source tree declares '{name}', which was not {discoveredBy}")
                .Concat(found.Except(declared, StringComparer.Ordinal)
                    .Select(name => $"'{name}' was {discoveredBy}, but the source tree declares no such type there"))
                .Order(StringComparer.Ordinal)
        ];
    }

    /// <summary>
    /// A compiled type's name without its arity suffix, so <c>IUseCase`1</c> compares equal to the
    /// <c>IUseCase</c> a reader sees in the file. Generic overloads of one name are one declaration.
    /// </summary>
    internal static string WithoutArity(Type type)
    {
        string fullName = type.FullName ?? type.Name;
        int arity = fullName.IndexOf('`');

        return arity < 0 ? fullName : fullName[..arity];
    }

    private static List<string> ReadWalkComplaints()
    {
        var complaints = new List<string>();

        if (Verticals.Count == 0)
        {
            complaints.Add(
                $"No vertical folder was found under '{_featuresRoot}', so every population read " +
                "here is empty and every comparison against one holds by comparing nothing.");
        }

        complaints.AddRange(Verticals
            .Where(vertical => Directory.Exists(Path.Combine(_featuresRoot, vertical, "UseCases")))
            .Where(vertical => !UseCaseFullNames.Any(fullName => fullName.StartsWith(
                $"AppTemplate.Application.Features.{vertical}.UseCases.", StringComparison.Ordinal)))
            .Select(vertical =>
                $"'{vertical}' has a UseCases folder in which no use-case class declaration was " +
                "found, so the declaration pattern has stopped matching what is written there."));

        if (PortFullNames.Count == 0)
        {
            complaints.Add(
                $"No port interface was found under '{_featuresRoot}' or '{_applicationRoot}\\Common'.");
        }

        if (DomainRepositoryFullNames.Count == 0)
        {
            complaints.Add($"No repository contract was found in a Repositories folder under '{_domainRoot}'.");
        }

        return complaints;
    }

    /// <summary>
    /// Every capture of <paramref name="declaration"/> in the source files under
    /// <paramref name="roots"/>, as the full name the folder and the declaration together imply.
    /// </summary>
    private static List<string> DeclarationsUnder(
        IEnumerable<string> roots,
        Regex declaration,
        string projectRoot,
        string rootNamespace)
    {
        var found = new List<string>();

        foreach (string root in roots)
        {
            foreach (string file in SourceFilesUnder(root))
            {
                string @namespace = NamespaceOf(Path.GetDirectoryName(file)!, projectRoot, rootNamespace);

                found.AddRange(declaration
                    .Matches(File.ReadAllText(file))
                    .Select(match => $"{@namespace}.{match.Groups[1].Value}"));
            }
        }

        return found;
    }

    /// <summary>
    /// The namespace a folder maps to, which is what makes a folder path comparable with a compiled
    /// type's full name. Namespaces follow folders in this repository, and a rule elsewhere asserts
    /// that they do — so a divergence here is either a moved folder or that rule already broken.
    /// </summary>
    private static string NamespaceOf(string folder, string projectRoot, string rootNamespace)
    {
        string relative = Path.GetRelativePath(projectRoot, folder);

        return string.Equals(relative, ".", StringComparison.Ordinal)
            ? rootNamespace
            : $"{rootNamespace}.{relative.Replace(Path.DirectorySeparatorChar, '.').Replace('/', '.')}";
    }

    private static IEnumerable<string> SourceFilesUnder(string root) =>
        Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static List<string> SubfolderNamesOf(string root) =>
        Directory.Exists(root)
            ? [.. Directory
                .EnumerateDirectories(root)
                .Select(folder => new DirectoryInfo(folder).Name)
                .Order(StringComparer.Ordinal)]
            : [];
}
