using CleanArch.Create.Manifests;
using Spectre.Console;

namespace CleanArch.Create.Prompts;

public sealed class PlainConversation : Conversation
{
    private readonly IAnsiConsole _console;

    public PlainConversation(IAnsiConsole console)
    {
        _console = console;
    }

    public override bool EchoesTheTemplateEngine => true;

    public override void Begin(string title, string? version)
    {
    }

    public override string Choose(string label, string description, IReadOnlyList<OptionChoice> choices, string? defaultValue)
    {
        var selection = new SelectionPrompt<string>()
            .Title(Title(label, description))
            .AddChoices(choices.Select(choice => choice.Value))
            .UseConverter(value => $"{Markup.Escape(value)} [grey]{Markup.Escape(choices.First(choice => choice.Value == value).Description)}[/]");
        if (defaultValue is not null && choices.Any(choice => choice.Value == defaultValue))
        {
            selection.DefaultValue(defaultValue);
        }

        string selected = _console.Prompt(selection);
        _console.MarkupLine($"[bold]{Markup.Escape(label)}[/]: [green]{Markup.Escape(selected)}[/]");
        return selected;
    }

    public override IReadOnlyList<string> ChooseMany(string label, IReadOnlyList<OptionChoice> choices, IReadOnlyCollection<string> selected)
    {
        var prompt = new MultiSelectionPrompt<string>()
            .Title($"[bold]{Markup.Escape(label)}[/]")
            .NotRequired()
            .AddChoices(choices.Select(choice => choice.Value))
            .UseConverter(value => $"{Markup.Escape(value)} [grey]{Markup.Escape(choices.First(choice => choice.Value == value).Description)}[/]");
        foreach (string value in selected)
        {
            prompt.Select(value);
        }

        List<string> chosen = _console.Prompt(prompt);
        _console.MarkupLine($"[bold]{Markup.Escape(label)}[/]: [green]{Markup.Escape(string.Join(", ", chosen))}[/]");
        return chosen;
    }

    public override bool Confirm(string label, string description, bool defaultValue) =>
        _console.Prompt(new ConfirmationPrompt(Title(label, description)) { DefaultValue = defaultValue });

    public override string Text(string label, string? description, string? defaultValue, bool allowEmpty, Func<string, string?> problem)
    {
        var prompt = new TextPrompt<string>(description is null ? Markup.Escape(label) + ":" : Title(label, description))
            .Validate(value => problem(value) is { } refused ? ValidationResult.Error(Markup.Escape(refused)) : ValidationResult.Success());
        if (!string.IsNullOrEmpty(defaultValue))
        {
            prompt.DefaultValue(defaultValue);
        }
        else if (allowEmpty)
        {
            prompt.AllowEmpty();
        }

        return _console.Prompt(prompt);
    }

    public override void Answered(string label, string value, string reason) =>
        _console.MarkupLine($"[bold]{Markup.Escape(label)}[/]: [green]{Markup.Escape(value)}[/] [grey]{Markup.Escape(reason)}[/]");

    public override void Warn(string text) => _console.MarkupLine("[yellow]" + Markup.Escape(text) + "[/]");

    public override void Note(string text) => _console.MarkupLine("[grey]" + Markup.Escape(text) + "[/]");

    public override bool Review(string subject, IReadOnlyList<KeyValuePair<string, string>> rows) => true;

    public override void Perform(string running, string done, Action work) => work();

    public override T Spin<T>(string running, Func<T> work) => work();

    public override void Reported(string text, bool succeeded, string? why, IReadOnlyList<string> rerun)
    {
        string colour = succeeded ? "green" : "yellow";
        _console.MarkupLine($"[{colour}]{Markup.Escape(text)}.[/]" + (why is null ? string.Empty : " " + Markup.Escape(why)));
        foreach (string command in rerun)
        {
            _console.Verbatim("  " + command);
        }
    }

    public override void Equivalents(IReadOnlyList<string> commandLines)
    {
        _console.MarkupLine("[bold]Done.[/] The same, without questions:");
        foreach (string line in commandLines)
        {
            _console.Verbatim("  " + line);
        }
    }

    public override void Close(Closing? closing)
    {
        if (closing is null)
        {
            return;
        }

        _console.MarkupLine(closing.Readme is null ? "[bold]Next[/]" : "[bold]By hand[/] " + Markup.Escape(closing.Readme) + ":");
        foreach (string command in closing.Commands)
        {
            _console.Verbatim(command);
        }

        if (closing.Section is not null)
        {
            _console.Verbatim(closing.Section);
        }
    }

    public override void Fail(string message) => _console.MarkupLine("[red]" + Markup.Escape(message) + "[/]");

    private static string Title(string label, string description) =>
        $"[bold]{Markup.Escape(label)}[/] [grey]{Markup.Escape(description)}[/]";
}
