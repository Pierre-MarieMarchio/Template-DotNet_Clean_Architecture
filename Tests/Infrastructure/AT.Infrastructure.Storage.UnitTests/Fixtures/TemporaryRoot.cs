using AppTemplate.Infrastructure.Storage.Features.Files.Configuration.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Fixtures;

internal sealed class TemporaryRoot : IDisposable
{
    internal TemporaryRoot()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"apptemplate-files-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    internal string Path { get; }

    internal IOptions<FilesStorageFileSystemOptions> Options =>
        Microsoft.Extensions.Options.Options.Create(new FilesStorageFileSystemOptions { RootPath = Path });

    internal IReadOnlyList<string> AllFiles() =>
        [.. Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories)
            .Select(file => System.IO.Path.GetRelativePath(Path, file).Replace(System.IO.Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)];

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
