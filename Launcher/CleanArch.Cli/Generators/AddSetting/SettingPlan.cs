using System.Globalization;
using System.Text;
using CleanArch.Cli.Generators.Shared;
using static CleanArch.Cli.Generators.Shared.ProjectFolders;
using static CleanArch.Cli.Generators.Shared.SourceText;

namespace CleanArch.Cli.Generators.AddSetting;

internal static class SettingPlan
{
    internal static string Words(string pascal) =>
        string.Concat(pascal.Select((character, index) => char.IsUpper(character) && index > 0 ? " " + character : character.ToString()));

    internal static List<Emission> Emissions(SettingSpec spec, string root)
    {
        string application = $"{SourceProject(spec.Prefix, "Application")}/Features/{spec.Features}/Configuration/Options";
        var emissions = new List<Emission> { new($"{application}/{spec.Options}.cs", OptionsClass(spec)) };
        string tests = $"Tests/Application/{FolderOf(spec.Prefix, "Application.UnitTests")}";

        if (Directory.Exists(Path.Combine(root, tests)))
        {
            emissions.Add(new Emission($"{tests}/Features/{spec.Features}/Configuration/{spec.Options}Tests.cs", TestsClass(spec)));
        }

        return emissions;
    }

    internal static string OptionsClass(SettingSpec spec)
    {
        var text = new StringBuilder();

        text.Append("using Microsoft.Extensions.Options;\n\n");
        text.Append($"namespace {spec.Namespace};\n\n");
        text.Append($"public sealed class {spec.Options}\n{{\n");
        text.Append($"    public const string SectionName = \"{spec.Section}\";\n");

        foreach (var key in spec.Keys)
        {
            string initializer = key.Kind == KeyKind.Secret || (key.Kind == KeyKind.Text && key.Default is null) ? " = string.Empty" : $" = {key.DefaultLiteral}";

            text.Append($"\n    public {key.ClrType} {key.Name} {{ get; set; }}{initializer};\n");
        }

        text.Append("}\n\n");
        text.Append($"internal sealed class {spec.Validator} : IValidateOptions<{spec.Options}>\n{{\n");
        text.Append($"    public ValidateOptionsResult Validate(string? name, {spec.Options} options)\n    {{\n");
        text.Append("        ArgumentNullException.ThrowIfNull(options);\n\n");
        text.Append("        var failures = new List<string>();\n");
        text.Append($"        string section = {spec.Options}.SectionName;\n");

        foreach (var key in spec.Keys.Where(key => key.IsValidated))
        {
            text.Append('\n').Append(Validation(key));
        }

        text.Append("\n        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);\n");
        text.Append("    }\n}\n");

        return Sorted(text.ToString());
    }

    internal static string Validation(SettingKey key)
    {
        (string condition, string message) = key.Kind switch
        {
            KeyKind.Int or KeyKind.Long => (
                $"options.{key.Name} is < {key.Bound(key.Min)} or > {key.Bound(key.Max)}",
                $"must be between {key.Bound(key.Min)} and {key.Bound(key.Max)}."),
            KeyKind.Duration => (
                $"options.{key.Name} < {key.Literal(key.Min)} || options.{key.Name} > {key.Literal(key.Max)}",
                $"must be between {key.Bound(key.Min)} and {key.Bound(key.Max)}."),
            KeyKind.Text => (
                $"options.{key.Name}.Length is < {key.Min} or > {key.Max}",
                $"must be between {key.Min} and {key.Max} characters{(key.IsRequired ? ", and has no default" : string.Empty)}."),
            _ => (
                $"string.IsNullOrWhiteSpace(options.{key.Name})",
                "is required and has no default: supply it as the environment variable {section}__" + key.Name + ", never in a tracked file."),
        };

        return
            $"        if ({condition})\n"
            + "        {\n"
            + $"            failures.Add($\"'{{section}}:{key.Name}' {message}\");\n"
            + "        }\n";
    }

    internal static string TestsClass(SettingSpec spec)
    {
        var text = new StringBuilder();
        string f = spec.Features;
        string options = spec.Options;
        var validated = spec.Keys.Where(key => key.IsValidated).ToList();

        text.Append($"using {spec.Namespace};\n");
        text.Append("using Microsoft.Extensions.Configuration;\n");
        text.Append("using Microsoft.Extensions.DependencyInjection;\n");
        text.Append("using Microsoft.Extensions.Options;\n");
        text.Append("using Shouldly;\nusing Xunit;\n\n");
        text.Append($"namespace {spec.Prefix}.Application.UnitTests.Features.{f}.Configuration;\n\n");
        text.Append($"public sealed class {options}Tests\n{{\n");

        foreach (var key in validated)
        {
            text.Append(KeyTests(spec, key));
        }

        text.Append(CompositionTests(spec));
        text.Append($"    private static {options} ValidOptions() =>\n        new()");

        string[] required = [.. spec.Keys.Where(key => key.IsRequired).Select(key => $"{key.Name} = {ValidRequired(key)}")];

        text.Append(required.Length == 0 ? ";\n" : "\n        {\n" + string.Join(string.Empty, required.Select(line => $"            {line},\n")) + "        };\n");
        text.Append($"\n    private static ValidateOptionsResult Validate(Action<{options}> change)\n    {{\n");
        text.Append($"        var options = ValidOptions();\n        change(options);\n\n");
        text.Append($"        return new {spec.Validator}().Validate(name: null, options);\n    }}\n}}\n");

        return Sorted(text.ToString());
    }

    internal static string ValidRequired(SettingKey key) => key.Kind == KeyKind.Secret ? "\"a-secret\"" : $"new string('a', {key.Min})";

    internal static string KeyTests(SettingSpec spec, SettingKey key)
    {
        string name = key.Name;
        string name2 = $"{spec.Section}:{name}";

        if (key.Kind == KeyKind.Secret)
        {
            return
                "    [Fact]\n"
                + $"    public void {name}_WhenPresent_Passes() =>\n        Validate(options => options.{name} = \"a-secret\").Succeeded.ShouldBeTrue();\n\n"
                + "    [Theory]\n    [InlineData(\"\")]\n    [InlineData(\"   \")]\n"
                + $"    public void {name}_WhenBlank_NamesTheKey(string value)\n    {{\n        var result = Validate(options => options.{name} = value);\n\n        result.Failed.ShouldBeTrue();\n        result.FailureMessage.ShouldContain(\"{name2}\");\n    }}\n\n";
        }

        string suffix = key.Kind == KeyKind.Long ? "L" : string.Empty;
        string parameter = key.Kind switch { KeyKind.Int => "int value", KeyKind.Long => "long value", KeyKind.Duration => "int seconds", _ => "int length" };
        string assign = key.Kind switch
        {
            KeyKind.Duration => $"options.{name} = TimeSpan.FromSeconds(seconds)",
            KeyKind.Text => $"options.{name} = new string('a', length)",
            _ => $"options.{name} = value",
        };
        string[] inside = [Num(key.Min) + suffix, Num(key.Max) + suffix];
        string[] outside = key.Kind == KeyKind.Text && key.Min < 1 ? [Num(key.Max + 1)] : [Num(key.Min - 1) + suffix, Num(key.Max + 1) + suffix];

        string Data(string[] values) => string.Concat(values.Select(value => $"    [InlineData({value})]\n"));

        return
            "    [Theory]\n" + Data(inside)
            + $"    public void {name}_AtItsBounds_Passes({parameter}) =>\n        Validate(options => {assign}).Succeeded.ShouldBeTrue();\n\n"
            + "    [Theory]\n" + Data(outside)
            + $"    public void {name}_PastItsBounds_NamesTheKey({parameter})\n    {{\n        var result = Validate(options => {assign});\n\n        result.Failed.ShouldBeTrue();\n        result.FailureMessage.ShouldContain(\"{name2}\");\n    }}\n\n";
    }

    internal static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);

    internal static string CompositionTests(SettingSpec spec)
    {
        var text = new StringBuilder();
        string options = spec.Options;
        var broken = spec.Keys.First(key => key.IsValidated);
        string call = spec.TakesConfiguration ? $"services.Add{spec.Features}(configuration);" : $"services.AddSingleton<IConfiguration>(configuration);\n        services.Add{spec.Features}();";

        string Setting(string key, string value) => $"                [\"{spec.Section}:{key}\"] = {value},\n";

        string BrokenValue() => broken.Kind switch
        {
            KeyKind.Int or KeyKind.Long => $"\"{broken.Bound(broken.Max + 1)}\"",
            KeyKind.Duration => $"\"{broken.Bound(broken.Max + 1)}\"",
            KeyKind.Text => $"new string('a', {broken.Max + 1})",
            _ => "string.Empty",
        };

        string ValidValue(SettingKey key) => key.Kind switch
        {
            KeyKind.Secret => "\"a-secret\"",
            KeyKind.Text => $"new string('b', {OtherLength(key)})",
            KeyKind.Bool => key.Default == "true" ? "\"false\"" : "\"true\"",
            _ => $"\"{key.Bound(OtherNumber(key))}\"",
        };

        text.Append("    [Fact]\n");
        text.Append($"    public void ASectionOutOfRange_StopsTheHostAtStart()\n    {{\n");
        text.Append($"        var configuration = new ConfigurationBuilder()\n            .AddInMemoryCollection(new Dictionary<string, string?>\n            {{\n");

        foreach (var key in spec.Keys.Where(key => key.IsRequired && key.Name != broken.Name))
        {
            text.Append(Setting(key.Name, ValidValue(key)));
        }

        text.Append(Setting(broken.Name, BrokenValue()));
        text.Append("            })\n            .Build();\n");
        text.Append("        var services = new ServiceCollection();\n");
        text.Append($"        {call}\n\n");
        text.Append("        using var provider = services.BuildServiceProvider();\n\n");
        text.Append("        var failure = Should.Throw<Exception>(() => provider.GetRequiredService<IStartupValidator>().Validate());\n\n");
        text.Append($"        failure.ToString().ShouldContain(\"'{spec.Section}:{broken.Name}'\");\n    }}\n\n");

        text.Append("    [Fact]\n");
        text.Append("    public void ASectionInRange_IsBoundFromItsConfiguration()\n    {\n");
        text.Append("        var configuration = new ConfigurationBuilder()\n            .AddInMemoryCollection(new Dictionary<string, string?>\n            {\n");

        foreach (var key in spec.Keys)
        {
            text.Append(Setting(key.Name, ValidValue(key)));
        }

        text.Append("            })\n            .Build();\n");
        text.Append("        var services = new ServiceCollection();\n");
        text.Append($"        {call}\n\n");
        text.Append("        using var provider = services.BuildServiceProvider();\n");
        text.Append($"        var bound = provider.GetRequiredService<IOptions<{options}>>().Value;\n\n");

        foreach (var key in spec.Keys)
        {
            text.Append($"        bound.{key.Name}.ShouldBe({Expected(key)});\n");
        }

        text.Append("    }\n\n");

        return text.ToString();
    }

    internal static long OtherNumber(SettingKey key) => long.Parse(key.Default!, CultureInfo.InvariantCulture) == key.Min ? key.Max : key.Min;

    internal static long OtherLength(SettingKey key) => key.Default is { } value && value.Length == key.Min ? key.Max : key.Min;

    internal static string Expected(SettingKey key) => key.Kind switch
    {
        KeyKind.Secret => "\"a-secret\"",
        KeyKind.Text => $"new string('b', {OtherLength(key)})",
        KeyKind.Bool => key.Default == "true" ? "false" : "true",
        KeyKind.Duration => SettingKey.SpanExpression(OtherNumber(key)),
        KeyKind.Long => Num(OtherNumber(key)) + "L",
        _ => Num(OtherNumber(key)),
    };
}
