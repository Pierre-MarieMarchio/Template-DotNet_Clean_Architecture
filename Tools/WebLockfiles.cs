#:property UseAppHost=false
#:property ManagePackageVersionsCentrally=false

using System.ComponentModel;
using System.Diagnostics;

if (args is ["--help"] or ["-h"])
{
    Console.WriteLine(WebLockfiles.Help);
    return 0;
}

bool check = args.Contains("--check");
string[] roots = [.. args.Where(argument => argument != "--check")];
if (roots.Length > 1)
{
    Console.Error.WriteLine(WebLockfiles.Help);
    return 2;
}

try
{
    WebLockfiles.Run(Path.GetFullPath(roots.Length > 0 ? roots[0] : "."), check);
    return 0;
}
catch (InvalidOperationException error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

internal static class WebLockfiles
{
    internal const string Help =
        """
        Usage: dotnet run Tools/WebLockfiles.cs [<root>]
               dotnet run Tools/WebLockfiles.cs --check [<root>]
               dotnet run Tools/WebLockfiles.cs -- --help

        Writes the lockfile of each generated web shape: for every app under Src/Web/apps holding a
        package.json, a copy of the workspace holding that app alone, locked by the pinned pnpm, into
        .template.config/variants/web-<App>/Src/Web/pnpm-lock.yaml. An app holding a scaffold.json
        gets none, since the scaffold writes its lockfile where it creates the app.
        --check writes nothing and fails when one of them differs from what it would write; verify
        runs it in the template's repository.
        """;

    private static readonly string[] _outputs = ["node_modules", "dist", "coverage", ".angular"];

    public static void Run(string root, bool check)
    {
        string web = Path.Combine(root, "Src", "Web");
        string variants = Path.Combine(root, ".template.config", "variants");
        string templateJson = File.ReadAllText(Path.Combine(root, ".template.config", "template.json"));
        string[] apps = Apps(web);
        List<string> problems = [];
        string scratch = Directory.CreateTempSubdirectory("web-lockfiles-").FullName;

        try
        {
            foreach (string app in apps)
            {
                problems.AddRange(Lock(root, web, variants, templateJson, Path.Combine(scratch, app), app, check));
            }

            problems.AddRange(Orphans(root, variants, apps, check));
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                string.Join(Environment.NewLine, problems) + Environment.NewLine
                + (check ? "Run `dotnet run Tools/WebLockfiles.cs` and commit what it writes." : "Fix the above and run it again."));
        }

        Console.WriteLine($"{apps.Length} generated web shape(s) locked: {string.Join(", ", apps)}.");
    }

    private static string[] Apps(string web)
    {
        string folder = Path.Combine(web, "apps");
        string[] apps = Directory.Exists(folder)
            ? [.. Directory.GetDirectories(folder)
                .Where(app => File.Exists(Path.Combine(app, "package.json")))
                .Select(app => Path.GetFileName(app))
                .Order(StringComparer.Ordinal)]
            : [];

        return apps.Length > 0
            ? apps
            : throw new InvalidOperationException("Src/Web/apps holds no app with a package.json, so there is no generated web shape to lock.");
    }

    private static IEnumerable<string> Lock(string root, string web, string variants, string templateJson, string copy, string app, bool check)
    {
        CopyTree(web, copy, folder =>
            _outputs.Contains(Path.GetFileName(folder), StringComparer.Ordinal)
            || (Path.GetDirectoryName(folder) == Path.Combine(web, "apps") && Path.GetFileName(folder) != app));
        Pnpm(copy, "install", "--lockfile-only", "--no-frozen-lockfile");

        string locked = Lines(File.ReadAllText(Path.Combine(copy, "pnpm-lock.yaml")));
        string variant = Path.Combine(variants, "web-" + app, "Src", "Web", "pnpm-lock.yaml");
        string shown = Shown(root, variant);
        bool current = File.Exists(variant) && Lines(File.ReadAllText(variant)) == locked;

        if (!current && check)
        {
            yield return $"{shown} is {(File.Exists(variant) ? "stale" : "missing")}.";
        }
        else if (!current)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(variant)!);
            File.WriteAllText(variant, locked);
            Console.WriteLine($"Wrote {shown}.");
        }

        string source = $"\"source\": \"./.template.config/variants/web-{app}/\"";
        if (!templateJson.Contains(source, StringComparison.Ordinal))
        {
            yield return $".template.config/template.json does not generate {shown}: add {{ {source}, \"target\": \"./\", "
                + "\"condition\": \"(<the symbol choosing this app>)\" } to its sources.";
        }
    }

    private static IEnumerable<string> Orphans(string root, string variants, string[] apps, bool check)
    {
        foreach (string orphan in Directory.GetDirectories(variants, "web-*")
            .Where(variant => !apps.Contains(Path.GetFileName(variant)["web-".Length..], StringComparer.Ordinal)))
        {
            if (check)
            {
                yield return $"{Shown(root, orphan)} locks an app Src/Web/apps no longer holds.";
            }
            else
            {
                Directory.Delete(orphan, recursive: true);
                Console.WriteLine($"Removed {Shown(root, orphan)}.");
            }
        }
    }

    private static void Pnpm(string directory, params string[] arguments)
    {
        string pnpm = OperatingSystem.IsWindows() ? "pnpm.cmd" : "pnpm";
        Console.WriteLine($"> {pnpm} {string.Join(' ', arguments)}");
        var start = new ProcessStartInfo(pnpm) { WorkingDirectory = directory, UseShellExecute = false };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Start(start);
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"'{pnpm} {string.Join(' ', arguments)}' exited with {process.ExitCode} in {directory}.");
        }
    }

    private static Process Start(ProcessStartInfo start)
    {
        try
        {
            return Process.Start(start)!;
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException($"Couldn't start {start.FileName}. Is pnpm installed (corepack enable)?");
        }
    }

    private static void CopyTree(string source, string target, Func<string, bool> skip)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (string folder in Directory.GetDirectories(source).Where(folder => !skip(folder)))
        {
            CopyTree(folder, Path.Combine(target, Path.GetFileName(folder)), skip);
        }
    }

    private static string Shown(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
