using CleanArch.Tooling;

namespace CleanArch.Create.Tests;

internal sealed class FakeProcesses : IProcessRunner
{
    private readonly Func<ProcessCall, ProcessResult> _answer;

    public FakeProcesses(Func<ProcessCall, ProcessResult>? answer = null)
    {
        _answer = answer ?? (_ => new ProcessResult(0, string.Empty));
    }

    public List<ProcessCall> Calls { get; } = [];

    public IEnumerable<string> CommandLines => Calls.Select(call => string.Join(' ', call.Arguments.Prepend(call.FileName)));

    public ProcessResult Run(ProcessCall command, Action<string>? line)
    {
        Calls.Add(command);
        ProcessResult result = _answer(command);
        foreach (string output in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            line?.Invoke(output);
        }

        return result;
    }
}
