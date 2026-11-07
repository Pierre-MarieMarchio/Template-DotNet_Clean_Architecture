using CleanArch.Tooling.Processes;

namespace CleanArch.Tooling.Additions;

public sealed record ItemStep(string CommandLine, Func<Action<string>?, ProcessResult> Run);

public interface IItemSteps
{
    ItemStep WebJoin(string root, string name, IReadOnlyList<string> options);

    ItemStep PresentationJoin(string root, string project);

    ItemStep PresentationScaffold(string root, string project);
}
