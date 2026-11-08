using CleanArch.Tooling.Manifests;

namespace CleanArch.Create.Commands.NewProject;

internal static class ExistingFiles
{
    public const string Cancel = "Cancel operation";
    public const string Remove = "Remove existing files and continue";
    public const string Ignore = "Ignore files and continue";

    private const string _repository = ".git";

    public static IReadOnlyList<OptionChoice> Choices { get; } =
    [
        new(Cancel, "leave the folder as it is"),
        new(Remove, $"delete everything in it but {_repository}, which can't be undone"),
        new(Ignore, "generate over them: the template's files replace those of the same name"),
    ];

    public static bool In(string folder) => Directory.Exists(folder) && Entries(folder).Any();

    public static string Refusal(string folder) =>
        $"'{folder}' already has files in it. Pick another folder with --output, or remove them, {_repository} excepted, with --overwrite.";

    public static IReadOnlyList<string> Forced(string? chosen) => chosen == Ignore ? ["--force"] : [];

    public static void Empty(string folder)
    {
        foreach (string entry in Entries(folder))
        {
            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }
        }
    }

    private static IEnumerable<string> Entries(string folder) =>
        Directory.EnumerateFileSystemEntries(folder).Where(entry => Path.GetFileName(entry) != _repository);
}
