using CleanArch.Tooling.Processes;

namespace CleanArch.Tooling.Additions;

public sealed record ItemStep(Func<bool> IsDone, Func<Action<string>?, ProcessResult> Run);

public interface IItemSteps
{
    ItemStep WebJoin(string root, string name, WebJoinOptions options);

    ItemStep WebScaffold(string root, string name);

    ItemStep PresentationJoin(string root, string project);

    ItemStep PresentationScaffold(string root, string project);
}
