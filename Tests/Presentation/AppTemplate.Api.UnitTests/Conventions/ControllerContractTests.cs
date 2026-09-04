using System.Reflection;
using AppTemplate.Api.Common.Controllers;
using AppTemplate.Application;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Features.TodoLists.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Conventions;

/// <summary>
/// The wire format is the API's own contract, not a view of the application layer's types.
/// </summary>
/// <remarks>
/// A rule rather than a review habit, and a rule rather than a per-endpoint assertion: it covers the
/// endpoints that do not exist yet, which is the only way a convention survives the next feature.
/// <para>
/// What it buys concretely: an application DTO reached by an action is a type two layers can rename.
/// Adding a field to it publishes that field; adding a field to a request record makes it bindable
/// from an untrusted body. Neither is visible in the diff of the layer that caused it. Behind a
/// contract of its own, the same change is inert until somebody edits the contract.
/// </para>
/// <para>
/// Written with reflection rather than NetArchTest: the subject is a method's return type and its
/// declared response types, which a type-level rule engine cannot address.
/// </para>
/// </remarks>
public sealed class ControllerContractTests
{
    private static readonly Assembly _api = typeof(ApiControllerBase).Assembly;
    private static readonly Assembly _application = typeof(ApplicationModule).Assembly;

    [Fact]
    public void Discovery_FindsTheControllers()
    {
        var controllers = Controllers();

        controllers.ShouldNotBeEmpty(
            "No controller was found in the API assembly, so every rule in this class would pass "
            + "for the wrong reason.");

        controllers.SelectMany(Actions).ShouldNotBeEmpty("The controllers found expose no actions to inspect.");
    }

    [Fact]
    public void NoAction_ReturnsAnApplicationType()
    {
        Offenders(ResponseTypes).ShouldBeEmpty(
            "An action must answer with a contract from Api/Features/<Feature>/Contracts/Responses/, "
            + "mapped explicitly. Serialising an application type makes the wire format change "
            + "whenever that type does, from a layer whose diff never mentions HTTP.");
    }

    [Fact]
    public void NoAction_BindsAnApplicationType()
    {
        Offenders(BoundTypes).ShouldBeEmpty(
            "An action must bind a contract from Api/Features/<Feature>/Contracts/Requests/. Binding "
            + "a command directly makes every member added to it settable from an untrusted body.");
    }

    /// <summary>
    /// Proves the detector can fail. A rule that cannot detect a violation is not a guarantee, and
    /// both rules above are written as "no offender", which an inert detector satisfies.
    /// </summary>
    [Fact]
    public void TheDetector_FlagsAControllerThatDoesLeak()
    {
        var action = typeof(LeakingController).GetMethod(nameof(LeakingController.Leak))!;

        ApplicationTypesIn(ResponseTypes(action)).ShouldNotBeEmpty(
            "The response-type walk did not flag an action returning an application DTO, so "
            + $"{nameof(NoAction_ReturnsAnApplicationType)} is vacuous.");

        ApplicationTypesIn(BoundTypes(action)).ShouldNotBeEmpty(
            "The parameter walk did not flag an action binding an application command, so "
            + $"{nameof(NoAction_BindsAnApplicationType)} is vacuous.");
    }

    private static IReadOnlyList<string> Offenders(Func<MethodInfo, IEnumerable<Type>> subject) =>
    [
        .. from controller in Controllers()
           from action in Actions(controller)
           from leaked in ApplicationTypesIn(subject(action))
           select $"{controller.Name}.{action.Name} → {leaked.Name}",
    ];

    // Nested types are skipped so that LeakingController, which exists to fail, cannot leak into the
    // rules it exists to validate.
    private static IReadOnlyList<Type> Controllers() =>
    [
        .. _api.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsNested: false })
            .Where(typeof(ControllerBase).IsAssignableFrom)
            .OrderBy(type => type.Name, StringComparer.Ordinal),
    ];

    private static IReadOnlyList<MethodInfo> Actions(Type controller) =>
    [
        .. controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .OrderBy(method => method.Name, StringComparer.Ordinal),
    ];

    /// <summary>
    /// Everything the action declares it can answer with: the value inside its
    /// <see cref="ActionResult{TValue}"/>, and every <see cref="ProducesResponseTypeAttribute"/> type.
    /// A bare <see cref="ActionResult"/> declares no type, so only the attributes speak for it — which
    /// is exactly why they are read here rather than trusted to agree with the signature.
    /// </summary>
    private static IEnumerable<Type> ResponseTypes(MethodInfo action)
    {
        var returned = action.ReturnType;

        if (returned.IsGenericType && returned.GetGenericTypeDefinition() == typeof(Task<>))
        {
            returned = returned.GetGenericArguments()[0];
        }

        if (returned.IsGenericType && returned.GetGenericTypeDefinition() == typeof(ActionResult<>))
        {
            yield return returned.GetGenericArguments()[0];
        }

        foreach (var declared in action.GetCustomAttributes<ProducesResponseTypeAttribute>())
        {
            if (declared.Type is { } type && type != typeof(void))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<Type> BoundTypes(MethodInfo action) =>
        action.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Where(type => type != typeof(CancellationToken));

    /// <summary>
    /// Walks generic arguments and array elements, so that a contract standing on an application type
    /// — a page of DTOs, a versioned DTO — is caught rather than hidden one level down.
    /// </summary>
    private static IReadOnlyList<Type> ApplicationTypesIn(IEnumerable<Type> types) =>
        [.. types.SelectMany(Closure).Where(type => type.Assembly == _application).Distinct()];

    private static IEnumerable<Type> Closure(Type type)
    {
        yield return type;

        if (type.GetElementType() is { } element)
        {
            foreach (var nested in Closure(element))
            {
                yield return nested;
            }
        }

        foreach (var argument in type.IsGenericType ? type.GetGenericArguments() : [])
        {
            foreach (var nested in Closure(argument))
            {
                yield return nested;
            }
        }
    }

    /// <summary>The violation both rules are written to catch, kept nested so discovery skips it.</summary>
    private sealed class LeakingController : ControllerBase
    {
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TodoItemDto))]
        public Task<ActionResult<Versioned<TodoItemDto>>> Leak(TodoItemDto body) =>
            throw new NotSupportedException(nameof(Leak));
    }
}
