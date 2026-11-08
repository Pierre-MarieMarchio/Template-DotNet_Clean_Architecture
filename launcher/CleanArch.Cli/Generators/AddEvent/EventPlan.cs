using CleanArch.Cli.Generators.Shared;
using static CleanArch.Cli.Generators.Shared.ProjectFolders;
using static CleanArch.Cli.Generators.Shared.SourceText;

namespace CleanArch.Cli.Generators.AddEvent;

internal static class EventPlan
{
    internal static List<Emission> Emissions(EventSpec spec, string root)
    {
        string domain = $"{SourceProject(spec.Prefix, "Domain")}/Features/{spec.Features}/Model/Events";
        var emissions = new List<Emission> { new($"{domain}/{spec.Name}.cs", EventRecord(spec)) };
        string domainTests = $"tests/Domain/{FolderOf(spec.Prefix, "Domain.UnitTests")}";

        if (Directory.Exists(Path.Combine(root, domainTests)))
        {
            emissions.Add(new Emission($"{domainTests}/Features/{spec.Features}/Model/Events/{spec.Name}Tests.cs", EventTests(spec)));
        }

        if (spec.ConsumerFeatures is not null)
        {
            string application = $"{SourceProject(spec.Prefix, "Application")}/Features/{spec.ConsumerFeatures}/Services/Consumers/{spec.Base}";

            emissions.Add(new Emission($"{application}/{spec.ConsumerName}.cs", ConsumerClass(spec)));

            string applicationTests = $"tests/Application/{FolderOf(spec.Prefix, "Application.UnitTests")}";

            if (Directory.Exists(Path.Combine(root, applicationTests)))
            {
                emissions.Add(new Emission($"{applicationTests}/Features/{spec.ConsumerFeatures}/Services/Consumers/{spec.Base}/{spec.ConsumerName}Tests.cs", ConsumerTests(spec)));
            }
        }

        return emissions;
    }

    private static string EventRecord(EventSpec spec)
    {
        string parameters = string.Concat(spec.Fields.Select(field => $"    {field.ClrType} {field.Name},\n"));

        return Sorted(
            $"using {spec.Prefix}.Domain.Core.Common.Events;\n\n"
            + $"namespace {spec.EventNamespace};\n\n"
            + $"public sealed record {spec.Name}(\n{parameters}    DateTimeOffset OccurredOn) : IDomainEvent;\n");
    }

    private static string Sample(Field field, int index) => field.Kind switch
    {
        FieldKind.Guid => $"guid{index}",
        FieldKind.String => $"\"{Words(field.Name)}\"",
        FieldKind.Int => "1",
        FieldKind.Long => "1L",
        FieldKind.Bool => "true",
        FieldKind.Decimal => "1m",
        FieldKind.DateOnly => "new DateOnly(2026, 1, 1)",
        _ => "new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)",
    };

    private static string Words(string pascal) =>
        string.Concat(pascal.Select((character, index) => char.IsUpper(character) && index > 0 ? " " + char.ToLowerInvariant(character) : char.ToLowerInvariant(character).ToString()));

    private static string GuidDeclarations(EventSpec spec) =>
        string.Concat(spec.Fields.Select((field, index) => (field, index)).Where(pair => pair.field.Kind == FieldKind.Guid).Select(pair => $"        var guid{pair.index} = Guid.CreateVersion7();\n"));

    private static string EventTests(EventSpec spec)
    {
        string declarations = GuidDeclarations(spec);
        string arguments = string.Join(", ", spec.Fields.Select(Sample).Append("_occurredOn"));
        string assertions = string.Concat(spec.Fields.Select((field, index) => $"        domainEvent.{field.Name}.ShouldBe({Sample(field, index)});\n"));

        return Sorted(
            $"using {spec.Prefix}.Domain.Core.Common.Events;\n"
            + $"using {spec.EventNamespace};\n"
            + "using Shouldly;\nusing Xunit;\n\n"
            + $"namespace {spec.Prefix}.Domain.UnitTests.Features.{spec.Features}.Model.Events;\n\n"
            + $"public sealed class {spec.Name}tests\n{{\n"
            + "    private static readonly DateTimeOffset _occurredOn = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);\n\n"
            + "    [Fact]\n    public void TheEvent_CarriesWhatItWasGiven()\n    {\n"
            + declarations + (declarations.Length > 0 ? "\n" : string.Empty)
            + $"        var domainEvent = new {spec.Name}({arguments});\n\n"
            + assertions
            + "        domainEvent.OccurredOn.ShouldBe(_occurredOn);\n    }\n\n"
            + $"    [Fact]\n    public void TheEvent_IsADomainEvent() =>\n        typeof(IDomainEvent).IsAssignableFrom(typeof({spec.Name})).ShouldBeTrue();\n}}\n");
    }

    private static string ConsumerClass(EventSpec spec) =>
        Sorted(
            $"using {spec.Prefix}.Application.Core.Common.Events;\n"
            + $"using {spec.EventNamespace};\n\n"
            + $"namespace {spec.ConsumerNamespace};\n\n"
            + $"internal sealed class {spec.ConsumerName} : IDomainEventConsumer<{spec.Name}>\n{{\n"
            + $"    public Task ConsumeAsync(\n        {spec.Name} domainEvent,\n        CancellationToken cancellationToken = default)\n    {{\n"
            + "        ArgumentNullException.ThrowIfNull(domainEvent);\n\n"
            + $"        throw new NotImplementedException(\n            \"{spec.Reaction} is not written yet: re-derive the precondition from the stores, act, then commit through IUnitOfWork, so that running twice changes nothing.\");\n"
            + "    }\n}\n");

    private static string ConsumerTests(EventSpec spec)
    {
        string call = spec.ConsumerTakesConfiguration
            ? $"services.Add{spec.ConsumerFeatures}(new ConfigurationBuilder().Build());"
            : $"services.Add{spec.ConsumerFeatures}();";
        string configuration = spec.ConsumerTakesConfiguration ? "using Microsoft.Extensions.Configuration;\n" : string.Empty;
        return Sorted(
            $"using {spec.Prefix}.Application.Core.Common.Events;\n"
            + $"using {spec.Prefix}.Application.Features.{spec.ConsumerFeatures}.Services.Consumers.{spec.Base};\n"
            + $"using {spec.EventNamespace};\n"
            + configuration
            + "using Microsoft.Extensions.DependencyInjection;\nusing Shouldly;\nusing Xunit;\n\n"
            + $"namespace {spec.Prefix}.Application.UnitTests.Features.{spec.ConsumerFeatures}.Services.Consumers.{spec.Base};\n\n"
            + $"public sealed class {spec.ConsumerName}tests\n{{\n"
            + "    [Fact]\n"
            + $"    public void TheConsumer_IsRegisteredForItsEvent()\n    {{\n        var services = new ServiceCollection();\n        {call}\n\n"
            + $"        services.ShouldContain(descriptor =>\n            descriptor.ServiceType == typeof(IDomainEventConsumer<{spec.Name}>)\n            && descriptor.ImplementationType == typeof({spec.ConsumerName}));\n    }}\n\n"
            + "    [Fact]\n"
            + $"    public async Task AMissingEvent_IsRefused()\n    {{\n        var consumer = new {spec.ConsumerName}();\n\n"
            + "        await Should.ThrowAsync<ArgumentNullException>(() => consumer.ConsumeAsync(null!, TestContext.Current.CancellationToken));\n    }\n\n"
            + "    [Fact]\n"
            + $"    public void TheReaction_IsWritten() =>\n        Assert.Fail(\n            \"{spec.Reaction} is not written: {spec.ConsumerName} throws NotImplementedException, and the dispatcher logs that and drops the event. \"\n            + \"Re-derive the precondition from the stores, act, commit through IUnitOfWork so that running twice changes nothing (ADR 0006), \"\n            + \"then replace this test with the ones that hold the reaction.\");\n}}\n");
    }
}
