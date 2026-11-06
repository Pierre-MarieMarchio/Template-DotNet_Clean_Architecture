using System.Globalization;
using Microsoft.CodeAnalysis;

namespace CleanArch.Cli.Generators.Shared;

internal static class FieldCode
{
    internal const string AnonymisedText = "[deleted account]";

    internal const int IndexNameMaxLength = 63;

    internal static List<string> Conflicts(
        IReadOnlyList<Field> fields,
        string features,
        string type,
        IEnumerable<string> generatedTypes,
        string deletion,
        bool owner,
        bool list)
    {
        var problems = new List<string>();

        foreach (string index in list ? fields.Where(FieldCode.IsSortable).Select(field => IndexName(features, field, owner)) : [])
        {
            if (index.Length > IndexNameMaxLength)
            {
                problems.Add(
                    $"--fields: the sort index {index} would be {index.Length} characters long, and PostgreSQL cuts a name at "
                    + $"{IndexNameMaxLength}, where two indexes could then share one. Shorten the field or the feature name.");
            }
        }
        var types = new HashSet<string>(generatedTypes, StringComparer.Ordinal) { type, $"{type}Id" };

        foreach (var field in fields.Where(field => types.Contains(field.Name)))
        {
            problems.Add(
                $"--fields: '{field.Name}' is the name of a type or a member the feature {type} generates, and a property "
                + "cannot share it. Choose another name.");
        }

        if (deletion == "anonymise")
        {
            if (!fields.Any(field => FieldCode.Anonymised(field) is not null))
            {
                problems.Add(
                    "--deletion anonymise rewrites the fields that can say who the owner was, its strings, texts and nullable "
                    + "fields, and these fields have none: there is nothing to anonymise. Use --deletion delete, or add such a field.");
            }

            foreach (var field in fields.Where(field => field is { Kind: FieldKind.String, Nullable: false } && field.Length < AnonymisedText.Length))
            {
                problems.Add(
                    $"--fields: {field.Name} is a required string({field.Length}), and --deletion anonymise writes "
                    + $"\"{AnonymisedText}\", {AnonymisedText.Length} characters, in its place. Give it string({AnonymisedText.Length}) "
                    + "or more, or make it nullable, which anonymises to null.");
            }
        }

        if (fields.All(field => !FieldCode.HasRule(field)))
        {
            problems.Add(
                "--fields: no field has a rule to check (a bool, an unbounded int or long, a nullable DateOnly or Guid accept "
                + "every value), so the validator the feature's writes run would be empty. Bound a number, or add a string, a text, "
                + "a decimal or a required date or Guid.");
        }

        return problems;
    }

    internal static string IndexName(string features, Field field, bool owner) =>
        owner ? $"IX_{features}_OwnerId_{field.Name}_Id" : $"IX_{features}_{field.Name}_Id";

    internal static string Label(Field field) => CSharpNames.Separated(field.Name, ' ');

    internal static string Parameter(Field field) => CSharpNames.Lower(field.Name);

    internal static string Type(Field field) => field.ClrType + (field.Nullable ? "?" : string.Empty);

    internal static bool IsReference(Field field) => field.Kind is FieldKind.String or FieldKind.Text;

    internal static bool HasRule(Field field) => field.Kind switch
    {
        FieldKind.String or FieldKind.Text or FieldKind.Decimal => true,
        FieldKind.Int or FieldKind.Long => field.Bounds.Any(),
        FieldKind.DateOnly or FieldKind.DateTimeOffset or FieldKind.Guid => !field.Nullable,
        _ => false,
    };

    internal static bool HasRequire(Field field) => HasRule(field) || field.Kind == FieldKind.DateTimeOffset;

    internal static bool IsSortable(Field field) =>
        !field.Nullable && field.Kind is FieldKind.String or FieldKind.Int or FieldKind.Long or FieldKind.DateOnly or FieldKind.DateTimeOffset;

    internal static bool IsKeyset(Field field) => IsSortable(field) && field.Kind is FieldKind.String or FieldKind.DateTimeOffset;

    internal static bool InSummary(Field field) => field.Kind != FieldKind.Text;

    internal static bool IsRequiredOnTheWire(Field field) => !field.Nullable && !IsReference(field);

    internal static string? Anonymised(Field field) => field switch
    {
        { Nullable: true } => "null",
        { Kind: FieldKind.String } => "AnonymisedText",
        { Kind: FieldKind.Text } => "string.Empty",
        _ => null,
    };

    internal static bool HasConstants(Field field) => IsReference(field) || field.Kind == FieldKind.Decimal || field.Bounds.Any();

    internal static string Grouped(int value) =>
        value >= 1_000
            ? value.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', '_')
            : value.ToString(CultureInfo.InvariantCulture);

    internal static string Literal(Field field, decimal value) =>
        value.ToString(CultureInfo.InvariantCulture) + (field.Kind == FieldKind.Decimal ? "m" : string.Empty);

    internal static string Joined(IEnumerable<string> lines, string separator = "\n") => string.Join(separator, lines);

    internal static string List(IEnumerable<string> items, string indent, string last = "") =>
        string.Join(",\n", items.Select(item => indent + item)) + last;

    internal static string Constants(IEnumerable<Field> fields, bool ceilings) =>
        Joined(
            [
                .. fields.SelectMany(ConstantsOf),
                .. ceilings
                    ? fields.Where(field => field.Kind == FieldKind.Decimal)
                        .Select(field => $"    private const decimal {Ceiling(field)} = 1e{field.Precision - field.Scale}m;")
                    : [],
            ],
            "\n\n");

    private static IEnumerable<string> ConstantsOf(Field field)
    {
        if (IsReference(field))
        {
            yield return $"    public const int {field.Name}MaxLength = {Grouped(field.Length)};";
        }

        if (field.Kind == FieldKind.Decimal)
        {
            yield return $"    public const int {field.Name}Precision = {field.Precision};";
            yield return $"    public const int {field.Name}Scale = {field.Scale};";
        }

        foreach (var bound in field.Bounds)
        {
            yield return $"    public const {field.ClrType} {field.Name}{bound.Suffix} = {Literal(field, bound.Value)};";
        }
    }

    private static string Ceiling(Field field) => $"_{Parameter(field)}Ceiling";

    private static string Subject(Field field) => $"%An% %L% {Label(field)}";

    internal static string Required(Field field) => $"{Subject(field)} is required.";

    private static string RequiredMayBeEmpty(Field field) => $"{Subject(field)} is required; it may be empty.";

    private static string Blank(Field field) => $"{Subject(field)} cannot be blank; leave it out instead.";

    private static string TooLong(Field field, string holder) =>
        $"{Subject(field)} cannot exceed {{{holder}{field.Name}MaxLength}} characters.";

    private static string OutOfBound(Field field, FieldBound bound) =>
        $"{Subject(field)} must be {bound.Wording} {bound.Value.ToString(CultureInfo.InvariantCulture)}.";

    private static string Digits(Field field)
    {
        int before = field.Precision - field.Scale;

        if (field.Scale == 0)
        {
            return $"{Subject(field)} must be a whole number of at most {Count(before, "digit")}.";
        }

        return before == 0
            ? $"{Subject(field)} must lie between -1 and 1, with at most {Count(field.Scale, "decimal place")}."
            : $"{Subject(field)} must have at most {Count(before, "digit")} before the decimal point and {field.Scale} after it.";
    }

    private static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? string.Empty : "s")}";

    internal static string Require(Field field)
    {
        string name = $"Require{field.Name}";
        string type = Type(field);

        switch (field.Kind)
        {
            case FieldKind.String when !field.Nullable:
                return $$"""
                        private static {{type}} {{name}}({{type}} value)
                        {
                            string trimmed = value?.Trim() ?? string.Empty;

                            if (trimmed.Length == 0)
                            {
                                throw new DomainException("{{Required(field)}}");
                            }

                            return trimmed.Length <= {{field.Name}}MaxLength
                                ? trimmed
                                : throw new DomainException($"{{TooLong(field, string.Empty)}}");
                        }
                    """;

            case FieldKind.String:
                return $$"""
                        private static {{type}} {{name}}({{type}} value)
                        {
                            if (value is null)
                            {
                                return null;
                            }

                            string trimmed = value.Trim();

                            if (trimmed.Length == 0)
                            {
                                throw new DomainException("{{Blank(field)}}");
                            }

                            return trimmed.Length <= {{field.Name}}MaxLength
                                ? trimmed
                                : throw new DomainException($"{{TooLong(field, string.Empty)}}");
                        }
                    """;

            case FieldKind.Text when !field.Nullable:
                return $$"""
                        private static {{type}} {{name}}({{type}} value)
                        {
                            if (value is null)
                            {
                                throw new DomainException("{{RequiredMayBeEmpty(field)}}");
                            }

                            return value.Length <= {{field.Name}}MaxLength
                                ? value
                                : throw new DomainException($"{{TooLong(field, string.Empty)}}");
                        }
                    """;

            case FieldKind.Text:
                return $$"""
                        private static {{type}} {{name}}({{type}} value) =>
                            value is null || value.Length <= {{field.Name}}MaxLength
                                ? value
                                : throw new DomainException($"{{TooLong(field, string.Empty)}}");
                    """;

            case FieldKind.DateTimeOffset when field.Nullable:
                return $"""
                        private static {type} {name}({type} value) => value?.ToUniversalTime();
                    """;
        }

        string present = field.Nullable ? "present" : "value";
        var lines = new List<string> { $"    private static {type} {name}({type} value)", "    {" };

        if (field.Nullable)
        {
            lines.AddRange(["        if (value is not { } present)", "        {", "            return null;", "        }", string.Empty]);
        }

        void Check(string condition, string message) =>
            lines.AddRange([$"        if ({condition})", "        {", $"            throw new DomainException(\"{message}\");", "        }", string.Empty]);

        if (field.Kind == FieldKind.Decimal)
        {
            Check($"decimal.Round({present}, {field.Name}Scale) != {present} || Math.Abs({present}) >= {Ceiling(field)}", Digits(field));
        }

        foreach (var bound in field.Bounds)
        {
            string comparison = bound.Operator switch
            {
                ">=" => "<",
                ">" => "<=",
                "<=" => ">",
                _ => ">=",
            };

            Check($"{present} {comparison} {field.Name}{bound.Suffix}", OutOfBound(field, bound));
        }

        if (field.Kind is FieldKind.DateOnly or FieldKind.DateTimeOffset or FieldKind.Guid && !field.Nullable)
        {
            Check($"{present} == default", Required(field));
        }

        lines.Add(field.Kind == FieldKind.DateTimeOffset ? $"        return {present}.ToUniversalTime();" : $"        return {present};");
        lines.Add("    }");

        return Joined(lines);
    }

    internal static string Rules(IEnumerable<Field> fields, string subject, string holder) =>
        Joined(fields.Where(HasRule).Select(field => RulesOf(field, subject, holder)), "\n\n");

    private static string RulesOf(Field field, string subject, string holder)
    {
        string limits = $"{holder}.";
        var chain = new List<string>();

        switch (field.Kind)
        {
            case FieldKind.String when !field.Nullable:
                chain.Add($".NotEmpty().WithMessage(\"{Required(field)}\")");
                chain.Add($".Must(value => value.Trim().Length <= {limits}{field.Name}MaxLength)");
                chain.Add($".WithMessage($\"{TooLong(field, limits)}\")");
                break;

            case FieldKind.String:
                chain.Add($".Must(value => value!.Trim().Length > 0).WithMessage(\"{Blank(field)}\")");
                chain.Add($".Must(value => value!.Trim().Length <= {limits}{field.Name}MaxLength)");
                chain.Add($".WithMessage($\"{TooLong(field, limits)}\")");
                chain.Add($".When({subject} => {subject}.{field.Name} is not null)");
                break;

            case FieldKind.Text:
                if (!field.Nullable)
                {
                    chain.Add($".NotNull().WithMessage(\"{RequiredMayBeEmpty(field)}\")");
                }

                chain.Add($".MaximumLength({limits}{field.Name}MaxLength)");
                chain.Add($".WithMessage($\"{TooLong(field, limits)}\")");
                break;

            case FieldKind.DateOnly or FieldKind.DateTimeOffset or FieldKind.Guid:
                chain.Add($".NotEmpty().WithMessage(\"{Required(field)}\")");
                break;
        }

        if (field.Kind == FieldKind.Decimal)
        {
            chain.Add($".PrecisionScale({limits}{field.Name}Precision, {limits}{field.Name}Scale, ignoreTrailingZeros: true)");
            chain.Add($".WithMessage(\"{Digits(field)}\")");
        }

        foreach (var bound in field.Bounds)
        {
            string rule = bound.Operator switch
            {
                ">=" => "GreaterThanOrEqualTo",
                ">" => "GreaterThan",
                "<=" => "LessThanOrEqualTo",
                _ => "LessThan",
            };

            chain.Add($".{rule}({limits}{field.Name}{bound.Suffix}).WithMessage(\"{OutOfBound(field, bound)}\")");
        }

        int rules = chain.Count(link => !link.StartsWith(".WithMessage", StringComparison.Ordinal) && !link.StartsWith(".When", StringComparison.Ordinal));

        if (rules > 1)
        {
            chain.Insert(0, ".Cascade(CascadeMode.Stop)");
        }

        return Joined([$"        RuleFor({subject} => {subject}.{field.Name})", .. chain.Select(link => "            " + link)]) + ";";
    }

    internal static string WireDocumentation(Field field)
    {
        string presence = field.Nullable ? "Optional" : "Required";
        string text = field.Kind switch
        {
            FieldKind.String when field.Nullable => $"Optional; when sent, not blank and at most {field.Length} characters once trimmed",
            FieldKind.String => $"Required, at most {field.Length} characters once trimmed",
            FieldKind.Text when field.Nullable => $"Optional, at most {field.Length} characters",
            FieldKind.Text => $"Required but may be empty, at most {field.Length} characters",
            FieldKind.DateOnly => $"{presence}, as yyyy-MM-dd",
            FieldKind.DateTimeOffset => $"{presence}, with its offset; kept as the instant, in UTC",
            FieldKind.Guid when !field.Nullable => "Required, and not the empty Guid",
            FieldKind.Decimal => $"{presence}, with at most {field.Precision - field.Scale} digits before the decimal point and {field.Scale} after it",
            _ => presence,
        };

        string bounds = string.Concat(field.Bounds.Select(bound => $", {bound.Wording} {bound.Value.ToString(CultureInfo.InvariantCulture)}"));

        return $"/// <param name=\"{field.Name}\">{text}{bounds}.</param>";
    }

    internal static string WireParameter(Field field) =>
        (IsRequiredOnTheWire(field) ? "[property: JsonRequired] " : string.Empty) + $"{Type(field)} {field.Name}";

    internal static string Store(Field field, string source)
    {
        string read = $"{source}.{field.Name}";

        if (field.Kind != FieldKind.DateTimeOffset)
        {
            return read;
        }

        return read + (field.Nullable ? "?.ToUniversalTime()" : ".ToUniversalTime()");
    }

    internal static string RecordProperty(Field field) =>
        field is { Nullable: false } && IsReference(field)
            ? $"    public string {field.Name} {{ get; set; }} = string.Empty;"
            : $"    public {Type(field)} {field.Name} {{ get; set; }}";

    internal static string Configuration(Field field, string holder)
    {
        string property = $"        builder.Property(record => record.{field.Name})";
        var links = new List<string>();

        if (IsReference(field))
        {
            links.Add($".HasMaxLength({holder}.{field.Name}MaxLength)");
        }

        if (field.Kind == FieldKind.Decimal)
        {
            links.Add($".HasPrecision({holder}.{field.Name}Precision, {holder}.{field.Name}Scale)");
        }

        if (!field.Nullable)
        {
            links.Add(".IsRequired()");
        }

        return links.Count switch
        {
            0 => property + ";",
            1 when !IsReference(field) && field.Kind != FieldKind.Decimal => property + links[0] + ";",
            _ => Joined([property, .. links.Select(link => "            " + link)]) + ";",
        };
    }

    internal static string SortIndex(string features, Field field, bool owner) =>
        Joined(
        [
            owner
                ? $"        builder.HasIndex(record => new {{ record.OwnerId, record.{field.Name}, record.Id }})"
                : $"        builder.HasIndex(record => new {{ record.{field.Name}, record.Id }})",
            $"            .HasDatabaseName(\"{FieldCode.IndexName(features, field, owner)}\");",
        ]);

    internal static (decimal Low, decimal High) Range(Field field)
    {
        decimal step = field.Kind == FieldKind.Decimal ? FieldGrammar.Step(field.Scale) : 1m;
        decimal floor = field.Kind switch
        {
            FieldKind.Int => int.MinValue,
            FieldKind.Long => long.MinValue,
            _ => -FieldGrammar.Ceiling(field) + step,
        };
        decimal top = field.Kind switch
        {
            FieldKind.Int => int.MaxValue,
            FieldKind.Long => long.MaxValue,
            _ => FieldGrammar.Ceiling(field) - step,
        };

        return (
            field.Lower is { } lower ? Math.Max(floor, Reached(lower, step)) : floor,
            field.Upper is { } upper ? Math.Min(top, Reached(upper, -step)) : top);
    }

    private static decimal Reached(FieldBound bound, decimal step) => bound.IsInclusive ? bound.Value : bound.Value + step;

    internal static string Sample(Field field, int which)
    {
        bool first = which == 1;

        return field.Kind switch
        {
            FieldKind.String => $"\"{Fit(first ? "Alpha" : "Bravo", field.Length)}\"",
            FieldKind.Text => $"\"{Fit(first ? "Some text" : "Other text", field.Length)}\"",
            FieldKind.Bool => first ? "true" : "false",
            FieldKind.DateOnly => first ? "new DateOnly(2026, 3, 4)" : "new DateOnly(2026, 4, 5)",
            FieldKind.DateTimeOffset => first
                ? "new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero)"
                : "new DateTimeOffset(2026, 4, 5, 6, 7, 8, TimeSpan.Zero)",
            FieldKind.Guid => first
                ? "new Guid(\"0199a3c4-2222-7000-8000-000000000002\")"
                : "new Guid(\"0199a3c4-3333-7000-8000-000000000003\")",
            _ => Literal(field, NumberSample(field, first)),
        };
    }

    private static string Fit(string text, int length) => length >= text.Length ? text : text[..length];

    private static decimal NumberSample(Field field, bool first)
    {
        var (low, high) = Range(field);
        decimal[] candidates = field.Kind == FieldKind.Decimal && field.Scale > 0
            ? DecimalCandidates(first, low, high)
            : WholeCandidates(first, low, high);
        int scale = field.Kind == FieldKind.Decimal ? field.Scale : 0;

        return candidates.First(candidate => candidate >= low && candidate <= high && decimal.Round(candidate, scale) == candidate);
    }

    private static decimal[] DecimalCandidates(bool first, decimal low, decimal high) => first ? [1.5m, 1m, 0m, low, high] : [2.25m, 2m, 1m, 0m, high, low];

    private static decimal[] WholeCandidates(bool first, decimal low, decimal high) => first ? [1m, 0m, low, high] : [2m, 1m, 0m, high, low];

    internal static List<(string Case, string Value)> Invalid(Field field, string? holder) =>
        [
            .. KindCases(field),
            .. IsReference(field) ? [("OverTheLimit", $"new string('a', {Constant(field, holder, "MaxLength", field.Length)} + 1)")] : Array.Empty<(string, string)>(),
            .. field.Kind == FieldKind.Decimal ? [("TooLarge", $"1e{field.Precision - field.Scale}m")] : Array.Empty<(string, string)>(),
            .. field is { Kind: FieldKind.String, Nullable: false } ? [("Missing", "null!")] : Array.Empty<(string, string)>(),
            .. field.Bounds.Select(bound => BoundCase(field, holder, bound)),
        ];

    private static IEnumerable<(string Case, string Value)> KindCases(Field field) => field switch
    {
        { Kind: FieldKind.String } => [("Blank", "\"   \"")],
        { Kind: FieldKind.Text, Nullable: false } => [("Missing", "null!")],
        { Kind: FieldKind.Decimal } when field.Scale < FieldGrammar.DecimalMaxPrecision => [("WithTooManyDecimals", Literal(field, FieldGrammar.Step(field.Scale + 1)))],
        { Kind: FieldKind.DateOnly or FieldKind.DateTimeOffset or FieldKind.Guid, Nullable: false } => [("Empty", "default")],
        _ => [],
    };

    private static (string Case, string Value) BoundCase(Field field, string? holder, FieldBound bound)
    {
        decimal step = field.Kind == FieldKind.Decimal ? FieldGrammar.Step(field.Scale) : 1m;
        string constant = Constant(field, holder, bound.Suffix, bound.Value);

        return bound.Operator switch
        {
            ">=" => ("BelowTheMinimum", holder is null ? Literal(field, bound.Value - step) : $"{constant} - {Literal(field, step)}"),
            ">" => ("AtTheExclusiveMinimum", constant),
            "<=" => ("AboveTheMaximum", holder is null ? Literal(field, bound.Value + step) : $"{constant} + {Literal(field, step)}"),
            _ => ("AtTheExclusiveMaximum", constant),
        };
    }

    private static string Constant(Field field, string? holder, string suffix, decimal value) =>
        holder is null ? Literal(field, value) : $"{holder}.{field.Name}{suffix}";

    internal static string Assertion(string owner, Field field, string expected) =>
        $"        {owner}.{field.Name}.ShouldBe({expected});";

    internal static string ValidatorTests(IReadOnlyList<Field> fields, string message, string builder, string holder)
    {
        var tests = new List<string>
        {
            $$"""
                [Fact]
                public async Task AValid{{message}}_IsAccepted() =>
                    (await _validator.ValidateAsync({{builder}}(), TestToken)).IsValid.ShouldBeTrue();
            """,
        };

        foreach (var field in fields)
        {
            if (field.Nullable)
            {
                tests.Add($$"""
                        [Fact]
                        public async Task {{field.Name}}Null_IsAccepted() =>
                            (await _validator.ValidateAsync({{builder}}() with { {{field.Name}} = null }, TestToken)).IsValid.ShouldBeTrue();
                    """);
            }

            if (field.Kind == FieldKind.String)
            {
                tests.Add($$"""
                        [Fact]
                        public async Task {{field.Name}}AtTheLimitOnceTrimmed_IsAccepted() =>
                            (await _validator.ValidateAsync(
                                {{builder}}() with { {{field.Name}} = $"  {new string('a', {{holder}}.{{field.Name}}MaxLength)}  " },
                                TestToken)).IsValid.ShouldBeTrue();
                    """);
            }

            foreach (var (name, value) in Invalid(field, holder))
            {
                tests.Add($$"""
                        [Fact]
                        public async Task {{field.Name}}{{name}}_IsRefused() =>
                            (await _validator.ValidateAsync({{builder}}() with { {{field.Name}} = {{value}} }, TestToken)).Errors
                                .ShouldContain(error => error.PropertyName == nameof({{message}}.{{field.Name}}));
                    """);
            }
        }

        return Joined(tests, "\n\n");
    }

    internal static string Construction(IEnumerable<string> arguments) => "new(\n" + List(arguments, "        ") + ")";

    internal static string Arguments(IReadOnlyList<Field> fields, Func<Field, string> value) => string.Join(", ", fields.Select(value));

    internal static string WithOne(IReadOnlyList<Field> fields, Field changed, string value) =>
        Arguments(fields, field => ReferenceEquals(field, changed) ? value : Sample(field, 1));

    internal static string AggregateTests(IReadOnlyList<Field> fields, bool create, bool update, string ownerArgument)
    {
        var tests = new AggregateTestWriter(fields, ownerArgument);

        return Joined(
            [
                .. create ? tests.Create() : [],
                .. update ? tests.Update() : [],
                .. tests.Rehydrate(),
            ],
            "\n\n");
    }

    private sealed class AggregateTestWriter(IReadOnlyList<Field> fields, string ownerArgument)
    {
        private readonly string _ones = Arguments(fields, field => Sample(field, 1));

        private readonly List<Field> _refusable = [.. fields.Where(field => Invalid(field, "%A%").Count > 0)];

        internal IEnumerable<string> Create() =>
        [
            Joined(
            [
                "    [Fact]",
                "    public void Create_KeepsEveryField_WithItsStringsTrimmed()",
                "    {",
                $"        var %L% = %A%.Create({ownerArgument}{Arguments(fields, Padded)});",
                string.Empty,
                "        %L%.Id.ShouldNotBe(Guid.Empty);",
                Asserted(1),
                "    }",
            ]),
            .. fields.Where(field => field.Kind == FieldKind.DateTimeOffset).Select(InUtc),
            .. _refusable.SelectMany(field => Invalid(field, "%A%").Select(invalid => Refuses(field, invalid.Case, invalid.Value))),
        ];

        internal IEnumerable<string> Update()
        {
            yield return Joined(
            [
                "    [Fact]",
                "    public void Update_ReplacesEveryField()",
                "    {",
                $"        var %L% = %A%.Rehydrate(Guid.CreateVersion7(), {ownerArgument}{_ones});",
                string.Empty,
                $"        %L%.Update({Arguments(fields, field => Sample(field, 2))});",
                string.Empty,
                Asserted(2),
                "    }",
            ]);

            if (_refusable.FirstOrDefault() is { } first)
            {
                yield return UpdateRefuses(first);
            }
        }

        internal IEnumerable<string> Rehydrate()
        {
            if (fields.FirstOrDefault(IsReference) is { } text)
            {
                yield return Joined(
                [
                    "    [Fact]",
                    "    public void Rehydrate_KeepsWhatWasStored_EvenOverTheCurrentLimit()",
                    "    {",
                    $"        string stored = new('a', %A%.{text.Name}MaxLength + 1);",
                    string.Empty,
                    $"        var %L% = %A%.Rehydrate(Guid.CreateVersion7(), {ownerArgument}{WithOne(fields, text, "stored")});",
                    string.Empty,
                    $"        %L%.{text.Name}.ShouldBe(stored);",
                    "    }",
                ]);
            }

            yield return Joined(
            [
                "    [Fact]",
                "    public void Rehydrate_RefusesAnEmptyId() =>",
                $"        Should.Throw<DomainException>(() => %A%.Rehydrate(Guid.Empty, {ownerArgument}{_ones}));",
            ]);
        }

        private static string Padded(Field field) => field.Kind == FieldKind.String ? $"\"  {Sample(field, 1)[1..^1]}  \"" : Sample(field, 1);

        private string Asserted(int which) => Joined(fields.Select(field => Assertion("%L%", field, Sample(field, which))));

        private string InUtc(Field field) =>
            $$"""
                [Fact]
                public void Create_Keeps{{field.Name}}AsTheInstant_InUtc()
                {
                    var sent = new DateTimeOffset(2026, 3, 4, 7, 6, 7, TimeSpan.FromHours(2));

                    var %L% = %A%.Create({{ownerArgument}}{{WithOne(fields, field, "sent")}});

                    %L%.{{field.Name}}.ShouldBe(sent);
                    %L%.{{field.Name}}{{(field.Nullable ? "!.Value" : string.Empty)}}.Offset.ShouldBe(TimeSpan.Zero);
                }
            """;

        private string Refuses(Field field, string name, string value) =>
            $$"""
                [Fact]
                public void Create_Refuses{{field.Name}}{{name}}() =>
                    Should.Throw<DomainException>(() => %A%.Create({{ownerArgument}}{{WithOne(fields, field, value)}}));
            """;

        private string UpdateRefuses(Field first)
        {
            var (name, value) = Invalid(first, "%A%")[0];

            return Joined(
            [
                "    [Fact]",
                $"    public void Update_Refuses{first.Name}{name}_AndChangesNothing()",
                "    {",
                $"        var %L% = %A%.Rehydrate(Guid.CreateVersion7(), {ownerArgument}{_ones});",
                string.Empty,
                $"        Should.Throw<DomainException>(() => %L%.Update({Arguments(fields, field => ReferenceEquals(field, first) ? value : Sample(field, 2))}));",
                string.Empty,
                Asserted(1),
                "    }",
            ]);
        }
    }

    internal static string ConfigurationTests(IReadOnlyList<Field> fields, string holder)
    {
        var tests = new List<string>();
        var texts = fields.Where(IsReference).ToList();
        var decimals = fields.Where(field => field.Kind == FieldKind.Decimal).ToList();

        if (texts.Count > 0)
        {
            tests.Add(Joined(
            [
                "    [Theory]",
                .. texts.Select(field => $"    [InlineData(nameof(%A%Record.{field.Name}), {holder}.{field.Name}MaxLength)]"),
                "    public void EveryTextColumn_IsAsLongAsTheValidatorAllows(string property, int expected) =>",
                "        Entity().FindProperty(property)!.GetMaxLength().ShouldBe(expected);",
            ]));
        }

        if (decimals.Count > 0)
        {
            tests.Add(Joined(
            [
                "    [Theory]",
                .. decimals.Select(field => $"    [InlineData(nameof(%A%Record.{field.Name}), {holder}.{field.Name}Precision, {holder}.{field.Name}Scale)]"),
                "    public void EveryDecimalColumn_HasThePrecisionAndScaleTheValidatorAllows(string property, int precision, int scale)",
                "    {",
                "        var column = Entity().FindProperty(property)!;",
                string.Empty,
                "        column.GetPrecision().ShouldBe(precision);",
                "        column.GetScale().ShouldBe(scale);",
                "    }",
            ]));
        }

        tests.Add(Joined(
        [
            "    [Fact]",
            "    public void EveryRequiredField_IsANonNullableColumn()",
            "    {",
            .. fields.Select(field =>
                $"        Entity().FindProperty(nameof(%A%Record.{field.Name}))!.IsNullable.ShouldBe{(field.Nullable ? "True" : "False")}();"),
            "    }",
        ]));

        return Joined(tests, "\n\n");
    }
}
