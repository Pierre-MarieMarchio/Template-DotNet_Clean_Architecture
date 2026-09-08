using System.Reflection;
using AppTemplate.Api.Common.Security;
using AppTemplate.Application;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ChangePassword;
using AppTemplate.Application.Core.Common.Concurrency;
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
    /// <summary>
    /// The host, anchored on a type that is not a controller. Anchoring on
    /// <c>ApiControllerBase</c> would name the SDK project the base class lives in, which holds no
    /// controller at all — and anchoring on a controller would make
    /// <see cref="Discovery_FindsTheControllers"/> unable to fail.
    /// </summary>
    private static readonly Assembly _api = typeof(AuthorizationPolicies).Assembly;

    /// <summary>
    /// Every assembly of the application layer, not one of them. The layer is split across three
    /// projects, and a rule comparing by assembly identity against a single one stops seeing a DTO
    /// reached from either of the other two — an Auth response leaking onto the wire would have
    /// read as compliant.
    /// </summary>
    private static readonly Assembly[] _applicationLayer =
    [
        typeof(ApplicationModule).Assembly,
        typeof(VersionPrecondition).Assembly,
        typeof(ChangePasswordCommand).Assembly,
    ];

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

        // And it flags one from every project of the layer, which "not empty" above does not say.
        // Drop an assembly from the list and this is what notices; the rules themselves would go on
        // reporting no offender for whatever that project holds.
        var authAction = typeof(LeakingController).GetMethod(nameof(LeakingController.LeakFromAuth))!;

        ApplicationTypesIn(ResponseTypes(action).Concat(ResponseTypes(authAction)))
            .Select(type => type.Assembly)
            .Distinct()
            .ShouldBe(
                _applicationLayer,
                ignoreOrder: true,
                "The walk flags types from some projects of the application layer and not others, "
                + "so a contract standing on one of the projects it misses reads as compliant.");
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
    /// Walks generic arguments and array elements, so that a contract standing on a type from any
    /// project of the application layer — a page of DTOs, a versioned DTO, an Auth result — is
    /// caught rather than hidden one level down.
    /// </summary>
    private static IReadOnlyList<Type> ApplicationTypesIn(IEnumerable<Type> types) =>
        [.. types.SelectMany(Closure).Where(type => _applicationLayer.Contains(type.Assembly)).Distinct()];

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
        // One leak per project of the application layer: the DTO from the business project, the
        // Versioned<> wrapper from the Core, and the command from Auth. That is what makes the
        // three-assembly list above self-verifying rather than asserted.
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TodoItemDto))]
        public Task<ActionResult<Versioned<TodoItemDto>>> Leak(TodoItemDto body) =>
            throw new NotSupportedException(nameof(Leak));

        public Task<ActionResult<ChangePasswordCommand>> LeakFromAuth(ChangePasswordCommand body) =>
            throw new NotSupportedException(nameof(LeakFromAuth));
    }
}
