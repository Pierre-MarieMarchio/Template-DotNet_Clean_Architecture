using System.Text.Json.Nodes;
using CleanArch.Tooling.Manifests;
using Shouldly;
using Xunit;

namespace CleanArch.Tooling.Tests.Manifests;

public sealed class OfferTests
{
    private const int _boolOptions = 17;

    [Fact]
    public void ARefusalEveryCompletionHolds_IsUnavoidable_HoweverManyOptionsItReads()
    {
        string[] names = [.. Enumerable.Range(0, _boolOptions).Select(index => $"option{index}")];
        var symbols = new JsonObject();

        foreach (string name in names)
        {
            symbols[name] = new JsonObject { ["type"] = "parameter", ["datatype"] = "bool", ["defaultValue"] = "false" };
        }

        symbols["alwaysRefused"] = new JsonObject
        {
            ["type"] = "parameter",
            ["datatype"] = "choice",
            ["choices"] = new JsonArray(new JsonObject { ["choice"] = "refused", ["description"] = "Never set." }),
            ["isRequired"] = $"({string.Join(" || ", names.Select(name => $"{name} || !{name}"))})",
            ["description"] = "Never set. Every answer is refused.",
        };

        TemplateManifest manifest = TemplateManifest.Read(new JsonObject { ["shortName"] = "sample", ["symbols"] = symbols }.ToJsonString(), null, null);

        Offer.Unavoidable(manifest, new Dictionary<string, string>(), new Dictionary<string, Condition>())
            .Select(refusal => refusal.Symbol)
            .ShouldBe(["alwaysRefused"]);
    }
}
