namespace CleanArch.Tooling.Manifests;

public sealed record Offer(IReadOnlyList<string> Values, IReadOnlyList<Refusal> Reasons)
{
    public static Offer For(
        TemplateManifest manifest,
        TemplateOption option,
        IReadOnlyDictionary<string, string> known,
        IReadOnlyDictionary<string, Condition> askedWhen)
    {
        List<string> candidates = Candidates(option);
        if (candidates.Count == 0 || manifest.Refusals.Count == 0)
        {
            return new Offer(candidates, []);
        }

        List<TemplateOption> open = Open(manifest, option.Symbol, known, askedWhen);
        var offered = new List<string>();
        var reasons = new List<Refusal>();

        foreach (string candidate in candidates)
        {
            if (Blocked(manifest, open, Answered(known, option.Symbol, candidate), askedWhen) is { } blocked)
            {
                reasons.AddRange(blocked);
            }
            else
            {
                offered.Add(candidate);
            }
        }

        return offered.Count == 0
            ? new Offer(candidates, [])
            : new Offer(offered, [.. reasons.Distinct()]);
    }

    public static IReadOnlyList<Refusal> Unavoidable(
        TemplateManifest manifest,
        IReadOnlyDictionary<string, string> known,
        IReadOnlyDictionary<string, Condition> askedWhen) =>
        Blocked(manifest, Open(manifest, null, known, askedWhen), new Dictionary<string, string>(known, StringComparer.Ordinal), askedWhen) ?? [];

    private static List<Refusal>? Blocked(
        TemplateManifest manifest,
        List<TemplateOption> open,
        Dictionary<string, string> answers,
        IReadOnlyDictionary<string, Condition> askedWhen)
    {
        List<IReadOnlyList<Refusal>> outcomes = [.. Completions(open, answers)
            .Select(completed => manifest.RefusalsFor(Unasked(manifest, open, completed, askedWhen)))];

        if (outcomes.Any(refused => refused.Count == 0))
        {
            return null;
        }

        List<Refusal> always = [.. outcomes.Skip(1).Aggregate(outcomes[0].AsEnumerable(), (common, refused) => common.Intersect(refused))];
        return always.Count > 0 ? always : [.. outcomes.SelectMany(refused => refused).Distinct()];
    }

    private static List<string> Candidates(TemplateOption option) => option.Kind switch
    {
        OptionKind.Choice => [.. option.Choices.Select(choice => choice.Value)],
        OptionKind.Bool => [Condition.True, Condition.False],
        _ => [],
    };

    private static List<TemplateOption> Open(
        TemplateManifest manifest,
        string? asked,
        IReadOnlyDictionary<string, string> known,
        IReadOnlyDictionary<string, Condition> askedWhen)
    {
        var read = manifest.Refusals.Select(refusal => refusal.When)
            .Concat(askedWhen.Values)
            .SelectMany(manifest.ParametersBehind)
            .ToHashSet(StringComparer.Ordinal);

        return [.. manifest.Options.Where(other =>
            other.Symbol != asked && read.Contains(other.Symbol) && !known.ContainsKey(other.Symbol))];
    }

    private static Dictionary<string, string> Answered(IReadOnlyDictionary<string, string> known, string symbol, string value) =>
        new(known, StringComparer.Ordinal) { [symbol] = value };

    private static IEnumerable<Dictionary<string, string>> Completions(List<TemplateOption> open, Dictionary<string, string> answers)
    {
        IEnumerable<Dictionary<string, string>> completions = [answers];

        foreach (TemplateOption option in open)
        {
            IReadOnlyList<string> values = Candidates(option) is { Count: > 0 } candidates ? candidates : [option.DefaultValue ?? string.Empty];
            completions = completions.SelectMany(partial => values.Select(value => Answered(partial, option.Symbol, value)));
        }

        return completions;
    }

    private static Dictionary<string, string> Unasked(
        TemplateManifest manifest,
        List<TemplateOption> open,
        Dictionary<string, string> answers,
        IReadOnlyDictionary<string, Condition> askedWhen)
    {
        foreach (TemplateOption option in open)
        {
            if (askedWhen.TryGetValue(option.Symbol, out Condition? when) && !manifest.Holds(when, answers))
            {
                answers.Remove(option.Symbol);
            }
        }

        return answers;
    }
}
