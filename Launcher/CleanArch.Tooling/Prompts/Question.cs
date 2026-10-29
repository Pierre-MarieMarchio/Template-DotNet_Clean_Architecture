namespace CleanArch.Tooling.Prompts;

public readonly record struct Reply(string? Value, bool Asked, bool Back)
{
    public static Reply Backward => new(null, Asked: false, Back: true);

    public static Reply Answer(string? value) => new(value, Asked: true, Back: false);

    public static Reply Passed(string? value) => new(value, Asked: false, Back: false);
}

public sealed record Question(string Key, Func<IReadOnlyDictionary<string, string>, string?, Reply> Ask);
