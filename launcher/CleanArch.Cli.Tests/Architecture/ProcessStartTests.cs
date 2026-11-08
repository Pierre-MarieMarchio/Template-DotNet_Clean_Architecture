using System.Diagnostics;
using CleanArch.Tooling.Processes;
using Mono.Cecil;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests.Architecture;

public sealed class ProcessStartTests
{
    private static readonly string[] _launcherPorts = [typeof(IProcessLauncher).FullName!, typeof(IProcessRunner).FullName!];

    [Fact]
    public void ProcessStart_IsCalledOnlyByAProcessLauncher()
    {
        string[] assemblies = [.. Directory.GetFiles(AppContext.BaseDirectory, "CleanArch.*.dll")
            .Where(path => !path.EndsWith(".Tests.dll", StringComparison.Ordinal))];
        List<Caller> callers = [.. assemblies.SelectMany(CallersIn)];

        callers.ShouldNotBeEmpty($"No call to Process.Start was found in {string.Join(", ", assemblies.Select(Path.GetFileName))}, so this rule compares nothing.");

        callers.Where(caller => !caller.IsLauncher)
            .Select(caller => caller.Type)
            .ShouldBeEmpty(
                $"Process.Start is called outside a type implementing {string.Join(" or ", _launcherPorts)}. "
                + "A process starts behind one of these ports alone, so that every test can replace it and no test starts one by surprise.");
    }

    private static List<Caller> CallersIn(string path)
    {
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(path);

        return [.. assembly.Modules
            .SelectMany(module => module.GetTypes())
            .Where(CallsProcessStart)
            .Select(Outermost)
            .DistinctBy(type => type.FullName)
            .Select(type => new Caller(type.FullName, type.Interfaces.Any(port => _launcherPorts.Contains(port.InterfaceType.FullName))))];
    }

    private static bool CallsProcessStart(TypeDefinition type) =>
        type.Methods
            .Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions)
            .Any(instruction => instruction.Operand is MethodReference { Name: nameof(Process.Start) } called
                && called.DeclaringType.FullName == typeof(Process).FullName);

    private static TypeDefinition Outermost(TypeDefinition type) => type.DeclaringType is { } declaring ? Outermost(declaring) : type;

    private sealed record Caller(string Type, bool IsLauncher);
}
