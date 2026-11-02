using System.Reflection;
using AppTemplate.Api.Common.Security;
using AppTemplate.Application;
//#if (localAuth)
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ChangePassword;
//#endif
using AppTemplate.Application.Core.Common.Concurrency;
//#if (todoLists)
using AppTemplate.Application.Features.TodoLists.Model.Dtos;
//#endif
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Conventions;

public sealed class ControllerContractTests
{
    private static readonly Assembly _api = typeof(SecurityHeaderOptions).Assembly;

    private static readonly Assembly[] _applicationLayer =
    [
        typeof(ApplicationModule).Assembly,
        typeof(VersionPrecondition).Assembly,
        //#if (localAuth)
        typeof(ChangePasswordCommand).Assembly,
        //#endif
    ];

    [Fact]
    public void Discovery_FindsTheControllers()
    {
        var controllers = Controllers();

        /*#if (!publicApi)*/
        controllers.ShouldNotBeEmpty(
            "No controller was found in the API assembly, so every rule in this class would pass "
            + "for the wrong reason.");
        /*#else
        if (controllers.Count == 0)
        {
            return;
        }
        #endif*/

        controllers.SelectMany(Actions).ShouldNotBeEmpty("The controllers found expose no actions to inspect.");
    }

    [Fact]
    public void EveryRouteName_IsUsedByExactlyOneAction()
    {
        var duplicated = Controllers()
            .SelectMany(Actions)
            .SelectMany(action => action.GetCustomAttributes<Attribute>(inherit: false)
                .OfType<IRouteTemplateProvider>()
                .Where(route => route.Name is not null)
                .Select(route => (route.Name, Action: $"{action.DeclaringType!.Name}.{action.Name}")))
            .GroupBy(route => route.Name, StringComparer.Ordinal)
            .Where(group => group.Select(route => route.Action).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(route => route.Action).Distinct(StringComparer.Ordinal))}")
            .Order(StringComparer.Ordinal)
            .ToList();

        duplicated.ShouldBeEmpty(
            "A route name is global to the application: MVC refuses two attribute routes with the same "
            + "name and different templates. Name each route after what it returns (GetNote, GetTodoList).");
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

        /*#if (localAuth)*/
        var authAction = typeof(LeakingController).GetMethod(nameof(LeakingController.LeakFromAuth))!;

        ApplicationTypesIn(ResponseTypes(action).Concat(ResponseTypes(authAction)))
        /*#else
        ApplicationTypesIn(ResponseTypes(action))
        #endif*/
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

    private sealed class LeakingController : ControllerBase
    {
        /*#if (todoLists)*/
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TodoItemDto))]
        public Task<ActionResult<Versioned<TodoItemDto>>> Leak(TodoItemDto body) =>
            throw new NotSupportedException(nameof(Leak));
        /*#else
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ApplicationModule))]
        public Task<ActionResult<Versioned<VersionPrecondition>>> Leak(VersionPrecondition body) =>
            throw new NotSupportedException(nameof(Leak));
        #endif*/
        //#if (localAuth)

        public Task<ActionResult<ChangePasswordCommand>> LeakFromAuth(ChangePasswordCommand body) =>
            throw new NotSupportedException(nameof(LeakFromAuth));
        //#endif
    }
}
