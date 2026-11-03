using AppTemplate.Architecture.Tests.Fixtures;
using FluentValidation;
using Mediator;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class ValidatorTargetTests
{
    [Fact]
    public void EveryValidator_TargetsAMessageOrIsInjectedWhereItRuns()
    {
        var types = ArchitectureAssemblies.ApplicationLayer.SelectMany(assembly => assembly.GetTypes()).ToList();

        types.ShouldNotBeEmpty("No application type was read, so this rule would hold by reading nothing.");

        UnexecutedValidators(types)
            .ShouldBeEmpty(
                "A validator runs only when something calls it: the validation behaviour does for the " +
                "message it targets, and a service does for a DTO when it takes IValidator<T> in its " +
                "constructor. These are neither, so nothing runs them and their messages are never " +
                "produced. Point the validator at the Command or Query it guards, inject it where it is " +
                "used, or delete it.");
    }

    [Fact]
    public void TheValidatorTargets_AreSensitive_AndSeeAValidatorNothingRuns()
    {
        UnexecutedValidators([typeof(MessageValidator), typeof(Message), typeof(OrphanValidator), typeof(Orphan)])
            .ShouldBe(
                [typeof(OrphanValidator).FullName!],
                "A validator of a message is run by the pipeline and is not reported; a validator of a type " +
                "that is no message and that nothing injects is.");

        UnexecutedValidators([typeof(OrphanValidator), typeof(Orphan), typeof(Consumer)])
            .ShouldBeEmpty("A validator injected into a class of the application layer is run by that class.");

        UnexecutedValidators([typeof(DerivedValidator), typeof(Message)])
            .ShouldBeEmpty("A validator that derives from another is read through its base type.");
    }

    private static List<string> UnexecutedValidators(IReadOnlyCollection<Type> types)
    {
        var injected = types
            .SelectMany(type => type.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Where(parameter => parameter.IsGenericType && parameter.GetGenericTypeDefinition() == typeof(IValidator<>))
            .Select(parameter => parameter.GetGenericArguments()[0])
            .ToHashSet();

        return
        [
            .. types
                .Where(type => type is { IsClass: true, IsAbstract: false })
                .Select(type => (Validator: type, Target: TargetOf(type)))
                .Where(pair => pair.Target is not null)
                .Where(pair => !typeof(IMessage).IsAssignableFrom(pair.Target) && !injected.Contains(pair.Target!))
                .Select(pair => pair.Validator.FullName!)
                .Order(StringComparer.Ordinal),
        ];
    }

    private static Type? TargetOf(Type validator)
    {
        for (var type = validator.BaseType; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private sealed record Message : IMessage;

    private sealed record Orphan;

    private sealed class MessageValidator : AbstractValidator<Message>;

    private class BaseValidator : AbstractValidator<Message>;

    private sealed class DerivedValidator : BaseValidator;

    private sealed class OrphanValidator : AbstractValidator<Orphan>;

    private sealed class Consumer(IValidator<Orphan> validator)
    {
        public IValidator<Orphan> Validator { get; } = validator;
    }
}
