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

        List<Range> open = Open(manifest, option.Symbol, known, askedWhen);
        var offered = new List<string>();
        var reasons = new List<Refusal>();

        foreach (string candidate in candidates)
        {
            var asked = new Range(option, option.AllowsMany ? [.. Selections(option).Where(selection => TemplateOption.Values(selection).Contains(candidate))] : [candidate]);
            if (Blocked(manifest, open, asked, known, askedWhen) is { } blocked)
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
        Blocked(manifest, Open(manifest, null, known, askedWhen), null, known, askedWhen) ?? [];

    private static List<Refusal>? Blocked(
        TemplateManifest manifest,
        List<Range> open,
        Range? asked,
        IReadOnlyDictionary<string, string> known,
        IReadOnlyDictionary<string, Condition> askedWhen)
    {
        var outcomes = new List<IReadOnlyList<Refusal>>();
        foreach (Dictionary<string, string> completed in Completions(asked is null ? open : [.. open, asked], known))
        {
            IReadOnlyList<Refusal> refused = manifest.RefusalsFor(Unasked(manifest, open, completed, askedWhen));
            if (refused.Count == 0)
            {
                return null;
            }

            outcomes.Add(refused);
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

    private static List<string> Selections(TemplateOption option)
    {
        string[] values = [.. option.Choices.Select(choice => choice.Value)];
        return [.. Enumerable.Range(1, (1 << values.Length) - 1)
            .Select(members => option.Joined(values.Where((_, index) => (members & (1 << index)) != 0)))];
    }

    private static List<Range> Open(
        TemplateManifest manifest,
        string? asked,
        IReadOnlyDictionary<string, string> known,
        IReadOnlyDictionary<string, Condition> askedWhen)
    {
        var read = manifest.Refusals.Select(refusal => refusal.When)
            .Concat(askedWhen.Values)
            .SelectMany(manifest.ParametersBehind)
            .ToHashSet(StringComparer.Ordinal);

        return [.. manifest.Options
            .Where(other => other.Symbol != asked && read.Contains(other.Symbol) && !known.ContainsKey(other.Symbol))
            .Select(other => new Range(other, Answers(other)))];
    }

    private static List<string> Answers(TemplateOption option)
    {
        if (option.AllowsMany)
        {
            return Selections(option);
        }

        return Candidates(option) is { Count: > 0 } candidates ? candidates : [option.DefaultValue ?? string.Empty];
    }

    private static IEnumerable<Dictionary<string, string>> Completions(List<Range> ranges, IReadOnlyDictionary<string, string> known)
    {
        IEnumerable<Dictionary<string, string>> completions = [new Dictionary<string, string>(known, StringComparer.Ordinal)];

        foreach (Range range in ranges)
        {
            completions = completions.SelectMany(partial => range.Values.Select(value =>
                new Dictionary<string, string>(partial, StringComparer.Ordinal) { [range.Option.Symbol] = value }));
        }

        return completions;
    }

    private static Dictionary<string, string> Unasked(
        TemplateManifest manifest,
        List<Range> open,
        Dictionary<string, string> answers,
        IReadOnlyDictionary<string, Condition> askedWhen)
    {
        foreach (Range range in open)
        {
            if (askedWhen.TryGetValue(range.Option.Symbol, out Condition? when) && !manifest.Holds(when, answers))
            {
                answers.Remove(range.Option.Symbol);
            }
        }

        return answers;
    }

    private sealed record Range(TemplateOption Option, IReadOnlyList<string> Values);
}
