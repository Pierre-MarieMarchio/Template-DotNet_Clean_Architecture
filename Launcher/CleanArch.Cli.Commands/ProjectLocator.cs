using System.Text.Json;

namespace CleanArch.Cli;

public sealed record LocatedProject(string Root, ProjectFile File);

public static class ProjectLocator
{
    public const string FileName = "cleanarch.json";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public static LocatedProject? Find(string workingDirectory)
    {
        for (DirectoryInfo? directory = new(workingDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, FileName);
            if (File.Exists(path))
            {
                return new LocatedProject(directory.FullName, Read(path));
            }
        }

        return null;
    }

    public static ProjectFile Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ProjectFile>(json, _json)
                ?? throw new ProjectFileException($"{FileName} is empty.");
        }
        catch (JsonException error)
        {
            throw new ProjectFileException($"{FileName} is not a project file: {error.Message}", error);
        }
    }

    private static ProjectFile Read(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (ProjectFileException error)
        {
            throw new ProjectFileException($"{path}: {error.Message}", error);
        }
    }
}
