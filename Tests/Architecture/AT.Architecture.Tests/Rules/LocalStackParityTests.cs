using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AppTemplate.AppHost;
using AppTemplate.Architecture.Tests.Fixtures;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class LocalStackParityTests
{
    private static readonly Dictionary<string, string> _composeOnlyServices = new(StringComparer.Ordinal)
    {
        //#if (sonar)
        ["sonarqube"] = "started only under the sonar profile, for the sonar task: a tool, not a part of the application",
        ["sonar-scanner"] = "the one-shot analysis the sonar task drives under the sonar profile",
        //#endif
        //#if (templateMaintenance)
        ["presentation-presentation-app-portal"] = "in this checkout, the source of a host the defaults leave out",
        ["presentation-presentation-app-mcp"] = "in this checkout, the source of a host the defaults leave out",
        ["worker"] = "in this checkout, the source of a host the defaults leave out",
        ["web-placeholder-site"] = "in this checkout, the source of a web app the defaults leave out",
        ["web-framework-site"] = "in this checkout, the source of a web app the defaults leave out",
        //#endif
    };

    private static readonly Dictionary<string, string> _containerOnlySettings = new(StringComparer.Ordinal)
    {
        ["DataProtection__KeysDirectory"] = "a path in the container's volume; a host the app host runs in Development keeps its key ring in the user profile",
    };

    private static readonly Regex _topLevelKey = new(
        @"^(?<key>[a-z][a-z0-9_-]*):",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _service = new(
        @"^  (?<name>[a-z0-9][a-z0-9_-]*):\s*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _image = new(
        @"^    image:\s*(?<ref>\S+)\s*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _include = new(
        @"^\s+-\s+(?:path:\s*)?(?<path>\S+compose\.ya?ml)\s*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _setting = new(
        @"^\s+(?<key>[A-Za-z][A-Za-z0-9]*(?:__[A-Za-z0-9]+)+):",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _publishedPort = new(
        @"^\s+-\s+""127\.0\.0\.1:\$\{[A-Z0-9_]+:-(?<port>\d+)\}:\d+""\s*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private const string _sdkName = "Aspire.AppHost.Sdk";

    [Fact]
    public void EveryComposeService_IsAnAppHostResourceOfTheSameName_AndEveryResourceAComposeService()
    {
        var services = ComposeServices().Keys.Where(name => !_composeOnlyServices.ContainsKey(name)).ToHashSet(StringComparer.Ordinal);
        var resources = AppHostResources().Select(resource => resource.Name).ToHashSet(StringComparer.Ordinal);

        services.ShouldNotBeEmpty("docker-compose.yml runs at least the mail catcher; a parse that finds no service is broken.");

        services.Except(resources).Order(StringComparer.Ordinal).ShouldBeEmpty(
            "docker-compose.yml runs these services and the app host does not: add each to Src/AppHost under the same "
            + "name, or list it in _composeOnlyServices with the reason the app host leaves it out.");
        resources.Except(services).Order(StringComparer.Ordinal).ShouldBeEmpty(
            "The app host starts these resources and docker-compose.yml does not: add each service to the Compose "
            + "files, which stay the reference of the deployment, or remove the resource.");
    }

    [Fact]
    public void EveryContainerTheAppHostPulls_RunsTheImageItsComposeServicePins()
    {
        var images = ComposeServices()
            .Where(service => service.Value is not null)
            .ToDictionary(service => service.Key, service => service.Value!, StringComparer.Ordinal);

        AppHostResources()
            .OfType<ContainerResource>()
            .Where(container => images.ContainsKey(container.Name))
            .Select(container => (container.Name, Compose: images[container.Name], AppHost: ImageOf(container)))
            .Where(pair => pair.Compose != pair.AppHost)
            .Select(pair => $"{pair.Name}: Compose runs {pair.Compose}, the app host {pair.AppHost}")
            .ShouldBeEmpty(
                "The app host reads each container's image from the Compose files, so it runs exactly the image its "
                + "service pins, tag and digest alike; one that differs means it no longer reads them.");
    }

    [Fact]
    public void EveryWebAppTheAppHostStarts_ListensOnThePortItsComposeServicePublishes()
    {
        var published = new Dictionary<string, int?>(StringComparer.Ordinal);

        foreach ((string service, string line) in ServiceLines())
        {
            if (_publishedPort.Match(line) is { Success: true } port)
            {
                published.TryAdd(service, int.Parse(port.Groups["port"].Value, CultureInfo.InvariantCulture));
            }
        }

        AppHostResources()
            .OfType<ContainerResource>()
            .Where(container => container.Name.StartsWith("web-", StringComparison.Ordinal))
            .Select(container => (
                container.Name,
                Compose: published.GetValueOrDefault(container.Name),
                AppHost: container.Annotations.OfType<EndpointAnnotation>().Single(endpoint => endpoint.Name == "http").Port))
            .Where(app => app.Compose != app.AppHost)
            .Select(app => $"{app.Name}: Compose publishes {app.Compose?.ToString(CultureInfo.InvariantCulture) ?? "no port"}, "
                + $"the app host {app.AppHost?.ToString(CultureInfo.InvariantCulture) ?? "a port of its own"}")
            .ShouldBeEmpty(
                "The app host binds each web app on the port cleanarch.json records for it, the one its compose.yml publishes, "
                + "so the app answers at the same address under either stack; record the port with web-join.");
    }

    [Fact]
    public void EverySettingComposeGivesAHost_TheAppHostGivesToo()
    {
        string appHost = string.Concat(Directory
            .EnumerateFiles(AppHostDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText));

        var settings = ComposeFiles()
            .SelectMany(File.ReadAllLines)
            .Select(line => _setting.Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["key"].Value)
            .ToHashSet(StringComparer.Ordinal);

        settings.ShouldNotBeEmpty("docker-compose.yml gives every host its Email section; a parse that finds no setting is broken.");

        settings
            .Where(key => !_containerOnlySettings.ContainsKey(key) && !appHost.Contains($"\"{key}\"", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "Compose gives a host these settings and the app host does not: give each the same value in Src/AppHost, "
                + "a secret from a parameter, or list it in _containerOnlySettings with its reason.");
    }

    [Fact]
    public void TheAppHostSdk_IsPinnedInGlobalJson_AtTheVersionOfEveryAspirePackage()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        string sdk = XDocument.Load(Directory.EnumerateFiles(AppHostDirectory, "*.csproj").Single()).Root!.Attribute("Sdk")!.Value;
        string? pinned = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "global.json")))?["msbuild-sdks"]?[_sdkName]?.GetValue<string>();

        sdk.ShouldBe(_sdkName, "The app host names its SDK without a version, which global.json pins under msbuild-sdks, where Dependabot raises it.");
        pinned.ShouldNotBeNull($"global.json pins no {_sdkName} under msbuild-sdks.");

        XDocument.Load(Path.Combine(root, "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Where(package => package.Attribute("Include")!.Value.StartsWith("Aspire.", StringComparison.Ordinal))
            .Where(package => package.Attribute("Version")!.Value != pinned)
            .Select(package => $"{package.Attribute("Include")!.Value} {package.Attribute("Version")!.Value}")
            .ShouldBeEmpty(
                $"global.json pins {_sdkName} {pinned}, and the CLI the app host runs is the SDK's own: one Aspire "
                + "version for the SDK and every package, which Dependabot's aspire group raises together.");
    }

    private static string AppHostDirectory =>
        ProjectReferenceGraph.RootOf(ProjectReferenceGraph.SourceProjects.Values
            .Single(project => project.Name.EndsWith(".AppHost", StringComparison.Ordinal)));

    private static List<IResource> AppHostResources()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args = [],
            ProjectDirectory = AppHostDirectory,
            DisableDashboard = true,
        });

        builder.AddLocalStack();

        return [.. builder.Resources.Where(resource => resource is ProjectResource or ContainerResource)];
    }

    private static string ImageOf(ContainerResource container)
    {
        var image = container.Annotations.OfType<ContainerImageAnnotation>().Last();
        string registry = image.Registry is null or "docker.io" ? string.Empty : $"{image.Registry}/";

        return image.SHA256 is null ? $"{registry}{image.Image}:{image.Tag}" : $"{registry}{image.Image}@sha256:{image.SHA256}";
    }

    private static Dictionary<string, string?> ComposeServices()
    {
        var services = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach ((string service, string line) in ServiceLines())
        {
            services.TryAdd(service, null);

            if (_image.Match(line) is { Success: true } image)
            {
                services[service] = image.Groups["ref"].Value;
            }
        }

        string database = Path.Combine(ProjectReferenceGraph.RepositoryRoot, "docker-compose.database.yml");

        if (services.TryGetValue("db", out string? extended) && extended is null && File.Exists(database))
        {
            services["db"] = File.ReadAllLines(database)
                .Select(line => _image.Match(line))
                .Where(match => match.Success)
                .Select(match => match.Groups["ref"].Value)
                .FirstOrDefault();
        }

        return services;
    }

    private static IEnumerable<(string Service, string Line)> ServiceLines()
    {
        foreach (string file in ServiceFiles())
        {
            bool inServices = false;
            string? current = null;

            foreach (string line in File.ReadAllLines(file))
            {
                if (_topLevelKey.Match(line) is { Success: true } key)
                {
                    inServices = key.Groups["key"].Value == "services";
                    current = null;
                }
                else if (inServices && _service.Match(line) is { Success: true } service)
                {
                    current = service.Groups["name"].Value;
                    yield return (current, line);
                }
                else if (current is not null)
                {
                    yield return (current, line);
                }
            }
        }
    }

    private static IEnumerable<string> ComposeFiles() =>
        ServiceFiles().Append(Path.Combine(ProjectReferenceGraph.RepositoryRoot, "docker-compose.database.yml")).Where(File.Exists);

    private static List<string> ServiceFiles()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        string compose = Path.Combine(root, "docker-compose.yml");

        return
        [
            compose,
            .. File.ReadAllLines(compose)
                .Select(line => _include.Match(line))
                .Where(match => match.Success)
                .Select(match => Path.Combine(root, match.Groups["path"].Value))
                .Where(File.Exists),
        ];
    }
}
