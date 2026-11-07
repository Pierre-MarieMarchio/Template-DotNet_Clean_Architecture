using CleanArch.Tooling.Processes;

namespace CleanArch.Tooling.Additions;

public sealed record ItemStep(string CommandLine, Func<bool> IsDone, Func<Action<string>?, ProcessResult> Run);

public interface IItemSteps
{
    ItemStep WebJoin(string root, string name, IReadOnlyList<string> options);

    ItemStep WebScaffold(string root, string name);

    ItemStep PresentationJoin(string root, string project);

    ItemStep PresentationScaffold(string root, string project);
}
