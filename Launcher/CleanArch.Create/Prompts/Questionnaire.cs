using System.Globalization;
using CleanArch.Create.Manifests;
using Spectre.Console;

namespace CleanArch.Create.Prompts;

public sealed class Questionnaire
{
    private readonly bool _yes;
    private readonly List<string> _missing = [];

    public Questionnaire(IAnsiConsole console, bool interactive, bool yes)
    {
        _yes = yes;
        Asks = interactive && !yes;
        Conversation = Conversation.For(console, Asks);
    }

    public bool Asks { get; }

    public Conversation Conversation { get; }

    public static List<KeyValuePair<string, string>> Recap(TemplateManifest manifest, IReadOnlyDictionary<string, string> answers) =>
        [.. manifest.Options
            .Where(option => answers.ContainsKey(option.Symbol))
            .Select(option => KeyValuePair.Create(option.Label, Shown(option, answers[option.Symbol])))];

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
            return Conversation.Text(label, null, defaultValue, allowEmpty: false, problem);
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
        IReadOnlyDictionary<string, Condition> askedWhen,
        IReadOnlyDictionary<string, string>? defaults = null)
    {
        Dictionary<string, string> answers = Given(manifest, given);
        foreach ((string symbol, string value) in preset)
        {
            answers.TryAdd(symbol, value);
        }

        foreach (TemplateOption option in manifest.Options.Where(option => !answers.ContainsKey(option.Symbol)).ToList())
        {
            if (!askedWhen.TryGetValue(option.Symbol, out Condition? when) || manifest.Holds(when, answers))
            {
                Fill(manifest, option, answers, askedWhen, defaults?.GetValueOrDefault(option.Symbol) ?? option.DefaultValue);
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

            Conversation.Warn(explanation);

            var involved = refused.SelectMany(refusal => manifest.ParametersBehind(refusal.When)).ToHashSet(StringComparer.Ordinal);
            var previous = new Dictionary<string, string>(answers, StringComparer.Ordinal);
            foreach (string symbol in involved)
            {
                answers.Remove(symbol);
            }

            foreach (TemplateOption option in manifest.Options.Where(option => involved.Contains(option.Symbol)))
            {
                if (!askedWhen.TryGetValue(option.Symbol, out Condition? when) || manifest.Holds(when, answers))
                {
                    Store(answers, option.Symbol, Ask(manifest, option, answers, askedWhen, previous.GetValueOrDefault(option.Symbol) ?? option.DefaultValue));
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

    private void Fill(
        TemplateManifest manifest,
        TemplateOption option,
        Dictionary<string, string> answers,
        IReadOnlyDictionary<string, Condition> askedWhen,
        string? defaultValue)
    {
        if (Asks)
        {
            Store(answers, option.Symbol, Ask(manifest, option, answers, askedWhen, defaultValue));
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

    private string? Ask(
        TemplateManifest manifest,
        TemplateOption option,
        Dictionary<string, string> answers,
        IReadOnlyDictionary<string, Condition> askedWhen,
        string? defaultValue)
    {
        Offer offer = Offer.For(manifest, option, answers, askedWhen);
        if (offer.Values.Count == 1)
        {
            string forced = offer.Values[0];
            Conversation.Answered(option.Label, Shown(option, forced), string.Join(" ", offer.Reasons.Select(refusal => refusal.Explanation)));
            return forced;
        }

        switch (option.Kind)
        {
            case OptionKind.Choice:
                return Conversation.Choose(
                    option.Label,
                    option.Description,
                    [.. option.Choices.Where(choice => offer.Values.Contains(choice.Value))],
                    defaultValue);

            case OptionKind.Bool:
                return Conversation.Confirm(option.Label, option.Description, Condition.IsTruthy(defaultValue)) ? Condition.True : Condition.False;

            default:
                bool required = option.RequiredWhen is { } requiredWhen && manifest.Holds(requiredWhen, answers);
                string text = Conversation.Text(option.Label, option.Description, defaultValue, allowEmpty: !required, value => Problem(option, value, required));
                return text.Length == 0 ? null : text;
        }
    }

    private static string Shown(TemplateOption option, string value)
    {
        if (option.Kind != OptionKind.Bool)
        {
            return value;
        }

        return Condition.IsTruthy(value) ? Conversation.Yes : Conversation.No;
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
