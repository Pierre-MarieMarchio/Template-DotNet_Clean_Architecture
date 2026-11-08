using System.Globalization;
using CleanArch.Cli.Generators.Shared;
using Xunit;

namespace CleanArch.Cli.Tests.Generators;

public sealed class FieldCodeTests
{
    private const string _bounded = "Count:int>=1<=9, Total:long>-5<5, Budget:decimal(6,2)>=0.5<=99.75, Ratio:decimal(5,4)>0<1";

    [Fact]
    public void EveryCaseOfABound_BreaksThatBoundAlone() => Expectations.Hold(expect =>
    {
        var fields = FieldGrammar.Parse(_bounded, FieldProfile.Feature, [])!;

        foreach (var field in fields)
        {
            var literal = Cases(field, holder: null);
            var named = Cases(field, holder: "H");

            expect(literal.Count == field.Bounds.Count() && named.Count == literal.Count, $"{field.Declaration} has {literal.Count} case(s) for its bounds, not one per bound.");

            foreach (var (bound, (name, value)) in field.Bounds.Zip(literal))
            {
                decimal number = Number(value);

                expect(
                    !bound.Holds(number) && field.Bounds.Where(other => other != bound).All(other => other.Holds(number)),
                    $"{field.Declaration}: {name} sends {value}, which does not break {bound.Operator}{bound.Literal} alone.");
            }

            foreach (var (name, value) in named)
            {
                string evaluated = field.Bounds.Aggregate(value, (text, each) => text.Replace($"H.{field.Name}{each.Suffix}", each.Literal, StringComparison.Ordinal));

                expect(
                    Number(evaluated) == Number(literal.Single(other => other.Case == name).Value),
                    $"{field.Declaration}: {name} sends {value}, which is not the value it sends without the constants, {literal.Single(other => other.Case == name).Value}.");
            }
        }
    });

    private static List<(string Case, string Value)> Cases(Field field, string? holder) =>
        [.. FieldCode.Invalid(field, holder).Where(invalid => invalid.Case.EndsWith("Minimum", StringComparison.Ordinal) || invalid.Case.EndsWith("Maximum", StringComparison.Ordinal))];

    private static decimal Number(string expression)
    {
        string[] terms = expression.Replace("m", string.Empty, StringComparison.Ordinal).Split(' ');
        decimal value = decimal.Parse(terms[0], CultureInfo.InvariantCulture);

        if (terms.Length == 1)
        {
            return value;
        }

        decimal step = decimal.Parse(terms[2], CultureInfo.InvariantCulture);
        return terms[1] == "-" ? value - step : value + step;
    }
}
