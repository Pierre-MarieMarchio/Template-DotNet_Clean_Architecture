using CleanArch.Create.Manifests;
using Spectre.Console;

namespace CleanArch.Create.Prompts;

public sealed record Closing(string? Readme, IReadOnlyList<string> Commands, string? Section);

public abstract class Conversation
{
    public const string Yes = "Yes";
    public const string No = "No";

    public abstract bool EchoesTheTemplateEngine { get; }

    public static Conversation For(IAnsiConsole console, bool asks) =>
        asks && console.Profile.Capabilities.Ansi && console.Profile.Capabilities.ColorSystem != ColorSystem.NoColors
            ? new GuidedConversation(console)
            : new PlainConversation(console);

    public abstract void Begin(string title, string? version);

    public abstract string Choose(string label, string description, IReadOnlyList<OptionChoice> choices, string? defaultValue);

    public abstract IReadOnlyList<string> ChooseMany(string label, IReadOnlyList<OptionChoice> choices, IReadOnlyCollection<string> selected);

    public abstract bool Confirm(string label, string description, bool defaultValue);

    public abstract string Text(string label, string? description, string? defaultValue, bool allowEmpty, Func<string, string?> problem);

    public abstract void Answered(string label, string value, string reason);

    public abstract void Warn(string text);

    public abstract void Note(string text);

    public abstract bool Review(string subject, IReadOnlyList<KeyValuePair<string, string>> rows);

    public abstract void Perform(string running, string done, Action work);

    public abstract T Spin<T>(string running, Func<T> work);

    public abstract void Reported(string text, bool succeeded, string? why, IReadOnlyList<string> rerun);

    public abstract void Equivalents(IReadOnlyList<string> commandLines);

    public abstract void Close(Closing? closing);

    public abstract void Fail(string message);
}
