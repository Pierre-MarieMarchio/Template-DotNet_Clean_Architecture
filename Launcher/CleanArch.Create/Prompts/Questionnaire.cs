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

    public Reply Text(string flag, string label, string? given, string? defaultValue, Func<string, string?> problem)
    {
        if (given is not null)
        {
            return problem(given) is { } refused
                ? throw new LauncherException($"{flag} '{given}': {refused}")
                : Reply.Passed(given);
        }

        if (Asks)
        {
            return Conversation.Text(label, null, defaultValue, allowEmpty: false, problem) is { } text ? Reply.Answer(text) : Reply.Backward;
        }

        if (_yes && defaultValue is not null)
        {
            return Reply.Passed(defaultValue);
        }

        _missing.Add(flag);
        return Reply.Passed(null);
    }

    public Dictionary<string, string> Answer(
        TemplateManifest manifest,
        IReadOnlyDictionary<string, string> given,
        IReadOnlyDictionary<string, string> preset,
        IReadOnlyDictionary<string, Condition> askedWhen,
        IReadOnlyList<Question>? before = null,
        IReadOnlyList<Question>? after = null)
    {
        Dictionary<string, string> answers = Given(manifest, given);
        foreach ((string symbol, string value) in preset)
        {
            answers.TryAdd(symbol, value);
        }

        var remembered = new Dictionary<string, string>(answers, StringComparer.Ordinal);
        if (Asks && Offer.Unavoidable(manifest, answers, askedWhen) is { Count: > 0 } unavoidable)
        {
            Conversation.Warn(Explanation(unavoidable));
            foreach (string symbol in unavoidable.SelectMany(refusal => manifest.ParametersBehind(refusal.When)))
            {
                answers.Remove(symbol);
            }
        }

        Interview(
            [
                .. before ?? [],
                .. manifest.Options.Where(option => !answers.ContainsKey(option.Symbol)).Select(option => OptionQuestion(manifest, option, askedWhen)),
                .. after ?? [],
            ],
            answers,
            remembered);

        ThrowIfMissing();

        while (manifest.RefusalsFor(answers) is { Count: > 0 } refused)
        {
            if (!Asks)
            {
                throw new LauncherException("These options are refused together:" + Environment.NewLine + Explanation(refused));
            }

            Conversation.Warn(Explanation(refused));

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
                    Store(answers, option.Symbol, Ask(manifest, option, answers, askedWhen, previous.GetValueOrDefault(option.Symbol) ?? option.DefaultValue).Value);
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

    private static string Explanation(IReadOnlyList<Refusal> refused) =>
        string.Join(Environment.NewLine, refused.Select(refusal => $"{refusal.Explanation} (refused by --{refusal.CliName})"));

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

    private void Interview(IReadOnlyList<Question> questions, Dictionary<string, string> answers, Dictionary<string, string> remembered)
    {
        var asked = new Stack<(int Index, int Mark)>();
        int index = 0;
        while (index < questions.Count)
        {
            Question question = questions[index];
            Conversation.CanGoBack = asked.Count > 0;
            int mark = Conversation.Mark();
            Reply reply = question.Ask(answers, remembered.GetValueOrDefault(question.Key));
            if (!reply.Back)
            {
                Store(answers, question.Key, reply.Value);
                if (reply.Asked)
                {
                    Store(remembered, question.Key, reply.Value);
                    asked.Push((index, mark));
                }

                index++;
                continue;
            }

            (int Index, int Mark) previous = asked.TryPop(out (int Index, int Mark) popped) ? popped : (index, mark);
            Conversation.Rewind(previous.Mark);
            foreach (Question undone in questions.Skip(previous.Index).Take(index - previous.Index))
            {
                answers.Remove(undone.Key);
            }

            index = previous.Index;
        }

        Conversation.CanGoBack = false;
    }

    private Question OptionQuestion(TemplateManifest manifest, TemplateOption option, IReadOnlyDictionary<string, Condition> askedWhen) =>
        new(option.Symbol, (answers, remembered) =>
        {
            if (askedWhen.TryGetValue(option.Symbol, out Condition? when) && !manifest.Holds(when, answers))
            {
                return Reply.Passed(null);
            }

            return Asks
                ? Ask(manifest, option, answers, askedWhen, remembered ?? option.DefaultValue)
                : Reply.Passed(Filled(manifest, option, answers));
        });

    private string? Filled(TemplateManifest manifest, TemplateOption option, IReadOnlyDictionary<string, string> answers)
    {
        bool required = option.RequiredWhen is { } requiredWhen && manifest.Holds(requiredWhen, answers);
        if (_yes && option.DefaultValue is not null)
        {
            return option.DefaultValue;
        }

        if (required || (!_yes && option.DefaultValue is not null))
        {
            _missing.Add("--" + option.CliName);
        }

        return null;
    }

    private Reply Ask(
        TemplateManifest manifest,
        TemplateOption option,
        IReadOnlyDictionary<string, string> answers,
        IReadOnlyDictionary<string, Condition> askedWhen,
        string? defaultValue)
    {
        Offer offer = Offer.For(manifest, option, answers, askedWhen);
        if (offer.Values.Count == 1)
        {
            string forced = offer.Values[0];
            Conversation.Answered(option.Label, Shown(option, forced), string.Join(" ", offer.Reasons.Select(refusal => refusal.Explanation)));
            return Reply.Passed(forced);
        }

        switch (option.Kind)
        {
            case OptionKind.Choice:
                string? chosen = Conversation.Choose(
                    option.Label,
                    option.Description,
                    [.. option.Choices.Where(choice => offer.Values.Contains(choice.Value))],
                    defaultValue);
                return chosen is null ? Reply.Backward : Reply.Answer(chosen);

            case OptionKind.Bool:
                bool? confirmed = Conversation.Confirm(option.Label, option.Description, Condition.IsTruthy(defaultValue));
                return confirmed is { } flag ? Reply.Answer(flag ? Condition.True : Condition.False) : Reply.Backward;

            default:
                bool required = option.RequiredWhen is { } requiredWhen && manifest.Holds(requiredWhen, answers);
                string? text = Conversation.Text(option.Label, option.Description, defaultValue, allowEmpty: !required, value => Problem(option, value, required));
                return text switch
                {
                    null => Reply.Backward,
                    { Length: 0 } => Reply.Answer(null),
                    _ => Reply.Answer(text),
                };
        }
    }
}
