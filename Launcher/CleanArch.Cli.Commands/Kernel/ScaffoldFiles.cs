namespace CleanArch.Cli.Commands.Kernel;

internal static class ScaffoldFiles
{
    public static void Copy(string source, string target, bool overwrite)
    {
        Directory.CreateDirectory(target);

        foreach (string file in Directory.GetFiles(source))
        {
            string copy = Path.Combine(target, Path.GetFileName(file));
            if (overwrite || !File.Exists(copy))
            {
                File.Copy(file, copy, overwrite);
            }
        }

        foreach (string folder in Directory.GetDirectories(source))
        {
            Copy(folder, Path.Combine(target, Path.GetFileName(folder)), overwrite);
        }
    }

    public static void DeleteFolder(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    public static void NormaliseLineEndings(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            byte[] content = File.ReadAllBytes(file);
            byte[] normalised = WithLfEndings(content);
            if (normalised.Length != content.Length)
            {
                File.WriteAllBytes(file, normalised);
            }
        }
    }

    public static byte[] WithLfEndings(byte[] content)
    {
        if (content.Contains((byte)0))
        {
            return content;
        }

        var normalised = new List<byte>(content.Length);
        for (int index = 0; index < content.Length; index++)
        {
            if (content[index] != (byte)'\r' || index + 1 == content.Length || content[index + 1] != (byte)'\n')
            {
                normalised.Add(content[index]);
            }
        }

        return [.. normalised];
    }
}
