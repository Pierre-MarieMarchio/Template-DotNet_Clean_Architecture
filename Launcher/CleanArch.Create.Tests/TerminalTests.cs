using System.Diagnostics;
using System.Text;
using Shouldly;
using Xunit;

namespace CleanArch.Create.Tests;

public sealed class TerminalTests
{
    private const string _westernEuropeanCodePage = "850";

    [Fact]
    public void ARedirectedOutput_IsUtf8_AndKeepsEachMessageOnOneLine()
    {
        const string name = "Café.Shop.WithANameLongEnoughToPassEightyColumns";

        string output = Launched(["new", "-n", name, "--yes"]);

        output.ShouldContain($"--name '{name}': use letters, digits and dots, with each part starting with a letter, like Acme.Shop.");
    }

    private static string Launched(string[] arguments)
    {
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (OperatingSystem.IsWindows())
        {
            foreach (string prefix in (string[])["/d", "/c", "chcp", _westernEuropeanCodePage, ">nul", "&&", "dotnet"])
            {
                start.ArgumentList.Add(prefix);
            }
        }

        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "CleanArch.Create.dll"));
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        process.StandardInput.Close();
        using var bytes = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(bytes);
        process.WaitForExit();

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes.ToArray());
    }
}
