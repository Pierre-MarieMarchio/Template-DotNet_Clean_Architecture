using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Composition;
using AppTemplate.Architecture.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// The manifests in <c>deploy/kubernetes/</c> against the options both hosts validate at start-up.
/// <para>
/// <b>Nothing compiles a manifest, so nothing but this rule holds one to the validators.</b> A
/// ConfigMap that omits a variable a validator requires — <c>Storage__BucketName</c>, say, which
/// <c>StorageOptionsValidator</c> demands, the tracked <c>appsettings.json</c> leaves blank, and
/// both hosts validate through <c>AddStorageModule</c> with <c>ValidateOnStart</c> — yields a pod
/// that never finishes starting, which is the exact failure <c>configmap-api.yaml</c>'s own header
/// promises the file prevents.
/// </para>
/// <para>
/// This is a rule rather than a script because the question is not "does the YAML parse" but "does
/// this configuration satisfy the validators", and the validators are code. It composes each host
/// exactly as <c>ContainerCompositionTests</c> does and runs the same
/// <see cref="IStartupValidator"/> a real host runs, over configuration read from the files an
/// operator actually applies.
/// </para>
/// </summary>
/// <remarks>
/// <b>What it models, and what it does not.</b> A Deployment takes the whole ConfigMap through
/// <c>envFrom</c> and individual Secret keys through <c>secretKeyRef</c>, so both are read and the
/// double underscore is translated to a colon the way the configuration binder does. It does not
/// model a cluster: an operator who overrides a value with a patch, a Kustomize overlay or a Helm
/// value is outside this, and so is anything about whether the endpoints named resolve. What it
/// pins is the one property the files can be held to on their own — that the shipped set is
/// complete enough to start.
/// </remarks>
public sealed class DeploymentConfigurationTests
{
    private const string _manifestDirectory = "deploy/kubernetes";

    /// <summary>A flat scalar entry under <c>data:</c> or <c>stringData:</c>, which is all these files use.</summary>
    private static readonly Regex _scalarEntry = new(
        @"^  ([A-Za-z0-9_.-]+):[ ]*(?:""(.*)""|(.*))$",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _resourceName = new(
        @"^metadata:\s*\n(?:\s+.*\n)*?\s+name:\s*(\S+)",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _configMapReference = new(
        @"configMapRef:\s*\n\s*name:\s*(\S+)",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _secretReference = new(
        @"-\s*name:\s*(\S+)\s*\n\s*valueFrom:\s*\n\s*secretKeyRef:\s*\n\s*name:\s*(\S+)\s*\n\s*key:\s*(\S+)",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _literalEnvironmentEntry = new(
        @"-\s*name:\s*([A-Za-z0-9_]+)\s*\n\s*value:\s*""?([^""\n]*)""?",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void TheApiManifests_SatisfyEveryOptionTheApiValidatesAtStartUp() =>
        ShouldStart("api-deployment.yaml", HostComposition.ComposeApi);

    [Fact]
    public void TheWorkerManifests_SatisfyEveryOptionTheWorkerValidatesAtStartUp() =>
        ShouldStart("worker-deployment.yaml", HostComposition.ComposeWorker);

    /// <summary>
    /// Every Secret key a Deployment reaches for has to exist in the example Secret.
    /// </summary>
    /// <remarks>
    /// A <c>secretKeyRef</c> naming a key nobody declared does not fail at apply time; the container
    /// simply never starts, with an event rather than a log line. The example file is the shape an
    /// operator copies, so a key missing from it is a key they will not know to create.
    /// </remarks>
    [Fact]
    public void EverySecretKeyAManifestReferences_IsDeclaredInTheExampleSecret()
    {
        var declared = SecretValues();

        declared.Count.ShouldBeGreaterThanOrEqualTo(
            5,
            "Far fewer keys were parsed out of secret.example.yaml than it declares, so this rule is " +
            "comparing against an almost-empty set and could not fail.");

        var references = ManifestFiles()
            .SelectMany(file => _secretReference.Matches(Read(file))
                .Select(match => (File: Path.GetFileName(file), Key: match.Groups[3].Value)))
            .ToList();

        references.Count.ShouldBeGreaterThanOrEqualTo(
            8,
            "Far fewer secretKeyRef entries were found than these manifests carry, so the reference " +
            "pattern has stopped matching the shape they are written in.");

        references
            .Where(reference => !declared.ContainsKey(reference.Key))
            .Select(reference => $"{reference.File} references secret key '{reference.Key}'")
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "A manifest reaches for a Secret key the example Secret does not declare. The pod " +
                "will not start and the operator has no way to know which key to create.");
    }

    /// <summary>
    /// Proves the composition above can refuse: the same host, with one required key removed from
    /// what the manifests supply, must fail to start.
    /// </summary>
    /// <remarks>
    /// Without this the two rules above pass on a configuration that was parsed as nothing — every
    /// validator would read its own default, and a manifest set that supplies no value at all would
    /// look exactly like a complete one for any section whose defaults happen to be valid.
    /// </remarks>
    [Fact]
    public void TheCheck_Refuses_WhenTheManifestsLeaveOutARequiredKey()
    {
        var supplied = ConfigurationFor("api-deployment.yaml");

        supplied.ShouldContainKey(
            "Storage:BucketName",
            "this is the key the manifests were missing, so it is the one worth removing here.");

        supplied.Remove("Storage:BucketName");

        var exception = Should.Throw<OptionsValidationException>(
            () => Validate(HostComposition.ComposeApi, supplied));

        exception.Message.ShouldContain("Storage:BucketName");
    }

    private static void ShouldStart(
        string deploymentFile,
        Func<IConfiguration, ServiceCollection> compose)
    {
        var supplied = ConfigurationFor(deploymentFile);

        supplied.Count.ShouldBeGreaterThanOrEqualTo(
            20,
            $"Far fewer configuration keys were read out of '{deploymentFile}' and the ConfigMap it " +
            "names than they hold, so this rule would pass on defaults rather than on what the " +
            "manifests supply.");

        // No assertion on the exception: there is none to catch. A validator that refuses throws
        // OptionsValidationException naming its own key, which is a better failure message than
        // anything this test could write around it.
        Validate(compose, supplied);
    }

    private static void Validate(
        Func<IConfiguration, ServiceCollection> compose,
        Dictionary<string, string?> supplied)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(supplied).Build();

        using var provider = compose(configuration)
            .BuildServiceProvider(HostComposition.StrictValidation);

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    /// <summary>
    /// What one host's container actually receives: every key of the ConfigMap it names, every
    /// Secret key it references, and every literal <c>value:</c> in its own env block — with the
    /// double underscore translated the way the configuration binder translates it.
    /// </summary>
    private static Dictionary<string, string?> ConfigurationFor(string deploymentFile)
    {
        string deployment = Read(Path.Combine(Root, deploymentFile));
        var configuration = new Dictionary<string, string?>(StringComparer.Ordinal);

        var configMap = _configMapReference.Match(deployment);

        configMap.Success.ShouldBeTrue(
            $"'{deploymentFile}' names no ConfigMap through envFrom, so either the manifest stopped " +
            "reading one or this rule stopped recognising how it does.");

        foreach ((string key, string? value) in EntriesOf(ConfigMapNamed(configMap.Groups[1].Value)))
        {
            configuration[Bind(key)] = value;
        }

        var secrets = SecretValues();

        foreach (Match reference in _secretReference.Matches(deployment))
        {
            string key = reference.Groups[3].Value;

            // Absence is the other rule's subject; here the reference simply contributes nothing,
            // so that this rule fails on the missing option rather than on a dictionary lookup.
            if (secrets.TryGetValue(key, out string? value))
            {
                configuration[Bind(reference.Groups[1].Value)] = value;
            }
        }

        foreach (Match literal in _literalEnvironmentEntry.Matches(deployment))
        {
            configuration[Bind(literal.Groups[1].Value)] = literal.Groups[2].Value;
        }

        return configuration;
    }

    /// <summary>The ConfigMap document declaring <paramref name="name"/>, whichever file holds it.</summary>
    private static string ConfigMapNamed(string name)
    {
        var documents = ManifestFiles()
            .SelectMany(file => Documents(Read(file)))
            .Where(document => document.Contains("kind: ConfigMap", StringComparison.Ordinal))
            .Where(document => NameOf(document) == name)
            .ToList();

        documents.Count.ShouldBe(
            1,
            $"exactly one ConfigMap named '{name}' has to exist under {_manifestDirectory}; " +
            $"{documents.Count} were found.");

        return documents[0];
    }

    private static Dictionary<string, string?> SecretValues()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        var documents = ManifestFiles()
            .SelectMany(file => Documents(Read(file)))
            .Where(document => document.Contains("kind: Secret", StringComparison.Ordinal));

        foreach (string document in documents)
        {
            foreach ((string key, string? value) in EntriesOf(document))
            {
                values[key] = value;
            }
        }

        return values;
    }

    /// <summary>
    /// The scalar entries under this document's <c>data:</c> or <c>stringData:</c> block. Read from
    /// the block onwards, so the labels and annotations above it — which are indented the same way —
    /// are not mistaken for configuration.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, string?>> EntriesOf(string document)
    {
        int start = document.IndexOf("\nstringData:\n", StringComparison.Ordinal);

        if (start < 0)
        {
            start = document.IndexOf("\ndata:\n", StringComparison.Ordinal);
        }

        if (start < 0)
        {
            return [];
        }

        return _scalarEntry
            .Matches(document[start..])
            .Select(match => new KeyValuePair<string, string?>(
                match.Groups[1].Value,
                match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value.Trim()));
    }

    private static string? NameOf(string document) =>
        _resourceName.Match(document) is { Success: true } match ? match.Groups[1].Value : null;

    /// <summary>One YAML file's documents, split on the separator at column zero.</summary>
    private static string[] Documents(string file) => file.Split("\n---");

    private static string Bind(string environmentVariable) =>
        environmentVariable.Replace("__", ":", StringComparison.Ordinal);

    private static IEnumerable<string> ManifestFiles() =>
        Directory.EnumerateFiles(Root, "*.yaml", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal);

    private static string Read(string path) => File.ReadAllText(path);

    private static string Root { get; } =
        Path.Combine(ProjectReferenceGraph.RepositoryRoot, "deploy", "kubernetes");
}
