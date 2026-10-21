using System.Globalization;
using CleanArch.Create.Manifests;
using Spectre.Console;

namespace CleanArch.Create.Prompts;

public sealed class Questionnaire
{
    private readonly IAnsiConsole _console;
    private readonly bool _yes;
    private readonly List<string> _missing = [];

    public Questionnaire(IAnsiConsole console, bool interactive, bool yes)
    {
        _console = console;
        _yes = yes;
        Asks = interactive && !yes;
    }

    public bool Asks { get; }

    public string? Text(string flag, string label, string? given, string? defaultValue, Func<string, string?> problem)
    {
        if (given is not null)
        {
            return problem(given) is { } refused
                ? throw new LauncherException($"{flag} '{given}': {refused}")
                : given;
        }

        if (Asks)
        {
            var prompt = new TextPrompt<string>(Markup.Escape(label) + ":")
                .Validate(value => problem(value) is { } refused ? ValidationResult.Error(Markup.Escape(refused)) : ValidationResult.Success());

            return _console.Prompt(defaultValue is null ? prompt : prompt.DefaultValue(defaultValue));
        }

        if (_yes && defaultValue is not null)
        {
            return defaultValue;
        }

        _missing.Add(flag);
        return null;
    }

    public Dictionary<string, string> Answer(
        TemplateManifest manifest,
        IReadOnlyDictionary<string, string> given,
        IReadOnlyDictionary<string, string> preset,
        IReadOnlyDictionary<string, Condition> askedWhen)
    {
        Dictionary<string, string> chosen = Given(manifest, given);
        var answers = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (TemplateOption option in manifest.Options)
        {
            if (chosen.TryGetValue(option.Symbol, out string? value) || preset.TryGetValue(option.Symbol, out value))
            {
                answers[option.Symbol] = value;
            }
            else if (!askedWhen.TryGetValue(option.Symbol, out Condition? when) || manifest.Holds(when, answers))
            {
                Fill(manifest, option, answers);
            }
        }

        ThrowIfMissing();

        while (manifest.RefusalsFor(answers) is { Count: > 0 } refused)
        {
            string explanation = string.Join(
                Environment.NewLine,
                refused.Select(refusal => $"{refusal.Explanation} (refused by --{refusal.CliName})"));

            if (!Asks)
            {
                throw new LauncherException("These options are refused together:" + Environment.NewLine + explanation);
            }

            _console.MarkupLine("[yellow]" + Markup.Escape(explanation) + "[/]");

            var involved = refused.SelectMany(refusal => manifest.ParametersBehind(refusal.When)).ToHashSet(StringComparer.Ordinal);
            foreach (TemplateOption option in manifest.Options.Where(option => involved.Contains(option.Symbol)))
            {
                if (askedWhen.TryGetValue(option.Symbol, out Condition? when) && !manifest.Holds(when, answers))
                {
                    answers.Remove(option.Symbol);
                }
                else
                {
                    Store(answers, option.Symbol, Ask(manifest, option, answers, answers.GetValueOrDefault(option.Symbol) ?? option.DefaultValue));
                }
            }
        }

        return answers;
    }

    public void ThrowIfMissing()
    {
        if (_missing.Count > 0)
        {
            throw new LauncherException(
                "No terminal to ask in, and these answers were not given: " + string.Join(", ", _missing)
                + ". Pass each, or --yes to take the defaults.");
        }
    }

    private static Dictionary<string, string> Given(TemplateManifest manifest, IReadOnlyDictionary<string, string> given)
    {
        var chosen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach ((string name, string value) in given)
        {
            TemplateOption option = manifest.Options.FirstOrDefault(option => string.Equals(option.CliName, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new LauncherException(
                    $"--{name} is not an option of {manifest.ShortName}; its options are "
                    + string.Join(", ", manifest.Options.Select(option => "--" + option.CliName)) + ".");

            chosen[option.Symbol] = Checked(option, value);
        }

        return chosen;
    }

    private static string Checked(TemplateOption option, string value)
    {
        switch (option.Kind)
        {
            case OptionKind.Choice:
                return option.Choices.FirstOrDefault(choice => string.Equals(choice.Value, value, StringComparison.OrdinalIgnoreCase))?.Value
                    ?? throw new LauncherException(
                        $"--{option.CliName} '{value}' is not one of {string.Join(", ", option.Choices.Select(choice => choice.Value))}.");
            case OptionKind.Bool when bool.TryParse(value, out bool flag):
                return flag ? Condition.True : Condition.False;
            case OptionKind.Bool:
                throw new LauncherException($"--{option.CliName} takes true or false, not '{value}'.");
            case OptionKind.WholeNumber when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _):
                return value;
            case OptionKind.WholeNumber:
                throw new LauncherException($"--{option.CliName} takes a whole number, not '{value}'.");
            default:
                return value;
        }
    }

    private static void Store(Dictionary<string, string> answers, string symbol, string? value)
    {
        if (value is null)
        {
            answers.Remove(symbol);
        }
        else
        {
            answers[symbol] = value;
        }
    }

    private void Fill(TemplateManifest manifest, TemplateOption option, Dictionary<string, string> answers)
    {
        if (Asks)
        {
            Store(answers, option.Symbol, Ask(manifest, option, answers, option.DefaultValue));
            return;
        }

        bool required = option.RequiredWhen is { } requiredWhen && manifest.Holds(requiredWhen, answers);
        if (_yes && option.DefaultValue is not null)
        {
            answers[option.Symbol] = option.DefaultValue;
        }
        else if (required || (!_yes && option.DefaultValue is not null))
        {
            _missing.Add("--" + option.CliName);
        }
    }

    private string? Ask(TemplateManifest manifest, TemplateOption option, Dictionary<string, string> answers, string? defaultValue)
    {
        string title = $"[bold]{Markup.Escape(option.Label)}[/] [grey]{Markup.Escape(option.Description)}[/]";

        switch (option.Kind)
        {
            case OptionKind.Choice:
                var selection = new SelectionPrompt<string>()
                    .Title(title)
                    .AddChoices(option.Choices.Select(choice => choice.Value))
                    .UseConverter(value => $"{Markup.Escape(value)} [grey]{Markup.Escape(option.Choices.First(choice => choice.Value == value).Description)}[/]");
                if (defaultValue is not null && option.Choices.Any(choice => choice.Value == defaultValue))
                {
                    selection.DefaultValue(defaultValue);
                }

                string selected = _console.Prompt(selection);
                _console.MarkupLine($"[bold]{Markup.Escape(option.Label)}[/]: [green]{Markup.Escape(selected)}[/]");
                return selected;

            case OptionKind.Bool:
                return _console.Prompt(new ConfirmationPrompt(title) { DefaultValue = Condition.IsTruthy(defaultValue) })
                    ? Condition.True
                    : Condition.False;

            default:
                bool required = option.RequiredWhen is { } requiredWhen && manifest.Holds(requiredWhen, answers);
                var prompt = new TextPrompt<string>(title).Validate(value => Problem(option, value, required) is { } problem
                    ? ValidationResult.Error(Markup.Escape(problem))
                    : ValidationResult.Success());
                if (!string.IsNullOrEmpty(defaultValue))
                {
                    prompt.DefaultValue(defaultValue);
                }
                else if (!required)
                {
                    prompt.AllowEmpty();
                }

                string text = _console.Prompt(prompt);
                return text.Length == 0 ? null : text;
        }
    }

    private static string? Problem(TemplateOption option, string value, bool required)
    {
        if (value.Length == 0)
        {
            return required ? "An answer is required." : null;
        }

        return option.Kind == OptionKind.WholeNumber && !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _)
            ? "A whole number is expected."
            : null;
    }
}
