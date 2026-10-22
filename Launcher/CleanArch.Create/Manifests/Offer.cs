namespace CleanArch.Create.Manifests;

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

        List<TemplateOption> open = Open(manifest, option, known, askedWhen);
        var offered = new List<string>();
        var reasons = new List<Refusal>();

        foreach (string candidate in candidates)
        {
            List<IReadOnlyList<Refusal>> outcomes = [.. Completions(open, Answered(known, option.Symbol, candidate))
                .Select(answers => manifest.RefusalsFor(Unasked(manifest, open, answers, askedWhen)))];

            if (outcomes.Any(refused => refused.Count == 0))
            {
                offered.Add(candidate);
                continue;
            }

            List<Refusal> always = [.. outcomes.Skip(1).Aggregate(outcomes[0].AsEnumerable(), (common, refused) => common.Intersect(refused))];
            reasons.AddRange(always.Count > 0 ? always : outcomes.SelectMany(refused => refused));
        }

        return offered.Count == 0
            ? new Offer(candidates, [])
            : new Offer(offered, [.. reasons.Distinct()]);
    }

    private static List<string> Candidates(TemplateOption option) => option.Kind switch
    {
        OptionKind.Choice => [.. option.Choices.Select(choice => choice.Value)],
        OptionKind.Bool => [Condition.True, Condition.False],
        _ => [],
    };

    private static List<TemplateOption> Open(
        TemplateManifest manifest,
        TemplateOption option,
        IReadOnlyDictionary<string, string> known,
        IReadOnlyDictionary<string, Condition> askedWhen)
    {
        var read = manifest.Refusals.Select(refusal => refusal.When)
            .Concat(askedWhen.Values)
            .SelectMany(manifest.ParametersBehind)
            .ToHashSet(StringComparer.Ordinal);

        return [.. manifest.Options.Where(other =>
            other.Symbol != option.Symbol && read.Contains(other.Symbol) && !known.ContainsKey(other.Symbol))];
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
