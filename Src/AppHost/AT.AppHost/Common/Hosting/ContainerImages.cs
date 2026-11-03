using System.Text.RegularExpressions;

namespace AppTemplate.AppHost.Common.Hosting;

internal static partial class ContainerImages
{
    private const string _digestSeparator = "@sha256:";

    private static readonly string[] _composeFiles = ["docker-compose.yml", "docker-compose.database.yml"];

    internal static IReadOnlyDictionary<string, string> FromCompose(string root)
    {
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] files =
        [
            .. _composeFiles.Select(file => Path.Combine(root, file)).Where(File.Exists),
            .. File.ReadAllLines(Path.Combine(root, _composeFiles[0]))
                .Select(line => Include().Match(line))
                .Where(match => match.Success)
                .Select(match => Path.Combine(root, match.Groups["path"].Value))
                .Where(File.Exists),
        ];

        foreach (string file in files)
        {
            bool inServices = false;
            string? service = null;

            foreach (string line in File.ReadAllLines(file))
            {
                if (TopLevelKey().Match(line) is { Success: true } key)
                {
                    inServices = key.Groups["key"].Value == "services";
                    service = null;
                }
                else if (inServices && Service().Match(line) is { Success: true } name)
                {
                    service = name.Groups["name"].Value;
                }
                else if (service is not null && Image().Match(line) is { Success: true } image)
                {
                    images[service] = image.Groups["reference"].Value;
                }
            }
        }

        return images;
    }

    internal static IResourceBuilder<T> WithComposeImage<T>(
        this IResourceBuilder<T> container,
        IReadOnlyDictionary<string, string> images)
        where T : ContainerResource
    {
        string reference = images.TryGetValue(container.Resource.Name, out string? image)
            ? image
            : throw new InvalidOperationException(
                $"No Compose file gives the service {container.Resource.Name} an image, and the app host runs "
                + "the image Compose pins.");
        int digestAt = reference.IndexOf(_digestSeparator, StringComparison.Ordinal);
        int tagAt = digestAt < 0 ? -1 : reference.LastIndexOf(':', digestAt);

        if (tagAt < 0 || digestAt == reference.Length - _digestSeparator.Length)
        {
            throw new InvalidOperationException(
                $"Compose gives {container.Resource.Name} the image '{reference}', not name:tag@sha256:digest.");
        }

        string name = reference[..tagAt];
        int registryEnd = name.IndexOf('/', StringComparison.Ordinal);

        if (registryEnd > 0 && name[..registryEnd].Contains('.', StringComparison.Ordinal))
        {
            container = container.WithImageRegistry(name[..registryEnd]);
            name = name[(registryEnd + 1)..];
        }

        return container.WithImage(name, reference[(tagAt + 1)..]);
    }

    [GeneratedRegex(@"^(?<key>[a-z][a-z0-9_-]*):", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex TopLevelKey();

    [GeneratedRegex(@"^  (?<name>[a-z0-9][a-z0-9_-]*):\s*$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Service();

    [GeneratedRegex(@"^    image:\s*(?<reference>\S+)\s*$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Image();

    [GeneratedRegex(@"^\s+-\s+(?:path:\s*)?(?<path>\S+compose\.ya?ml)\s*$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Include();
}
