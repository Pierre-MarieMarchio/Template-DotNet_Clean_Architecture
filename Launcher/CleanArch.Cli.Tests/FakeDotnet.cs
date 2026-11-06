using System.Globalization;
using System.Text;

namespace CleanArch.Cli.Tests;

internal sealed class FakeDotnet : IDisposable
{
    private const string _exitVariable = "CLEANARCH_FAKE_DOTNET_EXIT";

    private readonly string _previousPath = Environment.GetEnvironmentVariable("PATH") ?? "";

    public FakeDotnet()
    {
        Folder = Directory.CreateTempSubdirectory("cleanarch-fake-dotnet-").FullName;
        Log = Path.Combine(Folder, "calls.log");
        Snapshots = Path.Combine(Folder, "filters.log");
        string script =
            "#!/bin/sh\n"
            + $"printf '%s\\n' \"$*\" >> \"{Log}\"\n"
            + $"for argument in \"$@\"; do case \"$argument\" in *.slnf) {{ echo; echo \"== $argument\"; cat \"$argument\"; echo; }} >> \"{Snapshots}\" ;; esac; done\n"
            + "echo \"fake dotnet $*\"\n"
            + $"exit \"${{{_exitVariable}:-0}}\"\n";
        File.WriteAllText(Path.Combine(Folder, "dotnet"), script);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(
                Path.Combine(Folder, "dotnet"),
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        Environment.SetEnvironmentVariable("PATH", Folder + Path.PathSeparator + _previousPath);
        Environment.SetEnvironmentVariable(_exitVariable, "0");
    }

    public string Folder { get; }

    public string Log { get; }

    public string Snapshots { get; }

    public string[] Calls => File.Exists(Log) ? File.ReadAllLines(Log) : [];

    public static void ExitWith(int code) => Environment.SetEnvironmentVariable(_exitVariable, code.ToString(CultureInfo.InvariantCulture));

    public string FilterContentsOf(string fileName)
    {
        if (!File.Exists(Snapshots))
        {
            return "";
        }

        var contents = new StringBuilder();
        bool inside = false;
        foreach (string line in File.ReadAllLines(Snapshots))
        {
            if (line.StartsWith("== ", StringComparison.Ordinal))
            {
                inside = line == "== " + fileName;
                continue;
            }

            if (inside)
            {
                contents.AppendLine(line);
            }
        }

        return contents.ToString();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _previousPath);
        Environment.SetEnvironmentVariable(_exitVariable, null);
        Directory.Delete(Folder, recursive: true);
    }
}
