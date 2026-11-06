using System.Text.Json;
using System.Text.Json.Nodes;
using CleanArch.Tooling;

namespace CleanArch.Cli.Joins;

internal static class PresentationJoin
{
    private const string _scaffoldDescriptor = "scaffold.json";

    private static readonly JsonDocumentOptions _descriptorOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static int Run(LocatedProject project, string projectName, IProcessLauncher processes, TextWriter output, TextWriter error) =>
        JoinClosing.Refused(error, () => Join(project, projectName, new JoinProcesses(processes, output)));

    private static void Join(LocatedProject project, string projectName, JoinProcesses processes)
    {
        string name = PartOf(project, projectName);
        string relative = "Src/Presentation/" + ProjectNames.FolderOf(project.File.Name, name);
        bool recorded = project.File.Presentation?.Any(presentation => presentation.Name == name) == true;
        string? framework = recorded ? null : Framework(project.Root, relative);

        if (name == JobMoves.Worker)
        {
            WorkerJoin.Run(processes, project, relative);
        }
        else
        {
            ItemJoin.Run(project.Root, new JoinedItem(relative, "cleanarch-presentation", "presentation-scaffold", projectName, Required: true), processes.Output);
        }

        JoinClosing.Indexed(project, processes.Output);
        ProjectRecord record = ProjectRecord.Read(project.Root, processes.Output);
        if (framework is not null)
        {
            record.RecordPresentation(new PresentationProject(name, framework));
        }

        JoinClosing.RecordClient(project, record);
    }

    private static string PartOf(LocatedProject project, string projectName)
    {
        string prefix = project.File.Name + ".";
        return projectName.StartsWith(prefix, StringComparison.Ordinal) && projectName.Length > prefix.Length
            ? projectName[prefix.Length..]
            : throw new JoinRefusedException($"'{projectName}' is not a presentation project of {project.File.Name}: its name is {prefix}<Name>.");
    }

    private static string Framework(string root, string relative)
    {
        string descriptor = Path.Combine(root, relative, _scaffoldDescriptor);
        if (!File.Exists(descriptor))
        {
            throw new JoinRefusedException(
                $"{relative} holds no {_scaffoldDescriptor} to read its framework from, so no file was changed: name the folder cleanarch-presentation wrote, before presentation-scaffold creates it.");
        }

        return JsonNode.Parse(File.ReadAllText(descriptor).TrimStart('\uFEFF'), documentOptions: _descriptorOptions)?["framework"]?.GetValue<string>() is { Length: > 0 } framework
            ? framework
            : throw new JoinRefusedException($"'framework' is missing from the JSON of {relative}.");
    }
}
