using System.Reflection;
using AppTemplate.Api.Common.Idempotency;
using AppTemplate.Api.Features.Auth.Controllers;
using AppTemplate.Api.Features.Maintenance.Controllers;
using AppTemplate.Api.Features.Reminders.Controllers;
using AppTemplate.Api.Features.TodoLists.Controllers;
using Microsoft.AspNetCore.Mvc;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

/// <summary>
/// Where <see cref="IdempotentAttribute"/> may be applied.
/// </summary>
/// <remarks>
/// Replaying a <c>GET</c> is meaningless — a read has no effect to deduplicate — and replaying a
/// <c>PUT</c> or <c>DELETE</c> hides the idempotence those verbs already have by definition. The
/// attribute is only load-bearing on a <c>POST</c>, so this is a reflection rule rather than an
/// architecture-assembly one: it reads the controllers directly, which is why it lives here and not
/// in <c>AppTemplate.Architecture.Tests</c> — that project deliberately does not reference
/// <c>AppTemplate.Api</c> (see its own csproj comment), composing the API's modules itself instead of
/// the API assembly. This project already references <c>AppTemplate.Api</c> to drive it over HTTP, so
/// the reference this rule needs already exists.
/// </remarks>
public sealed class IdempotentActionsAreAlwaysPostTests
{
    /// <summary>Every controller the API declares. A new controller silently missing here would let a
    /// misapplied [Idempotent] on it escape this rule.</summary>
    private static readonly Type[] _controllers =
    [
        typeof(AuthController),
        typeof(MaintenanceController),
        typeof(RemindersController),
        typeof(TodoListsController),
    ];

    [Fact]
    public void EveryIdempotentAction_IsAPost()
    {
        var idempotentActions = _controllers
            .SelectMany(controller => controller.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttribute<IdempotentAttribute>() is not null)
            .ToList();

        idempotentActions.ShouldNotBeEmpty(
            "No action carrying [Idempotent] was found across the enumerated controllers, so this " +
            "rule is guarding nothing. Either the attribute was renamed, or the controller list above " +
            "has gone stale.");

        // Today: TodoListsController.Create, TodoListsController.AddItem, and
        // RemindersController.Schedule.
        idempotentActions.Count.ShouldBe(
            3,
            "A different number of [Idempotent] actions was found than this template is known to " +
            "declare. Update this rule's expectation alongside whichever action was added or removed.");

        idempotentActions
            .Where(method => method.GetCustomAttribute<HttpPostAttribute>() is null)
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "[Idempotent] is only meaningful on a POST: replaying a GET has no effect to " +
                "deduplicate, and replaying a PUT/DELETE would hide the idempotence those verbs " +
                "already have by definition.");
    }

    /// <summary>
    /// Proves the enumeration above is complete, the same way
    /// <c>DefaultDenyAuthorizationTests.TheEnumerationCoversEveryActionOnTheController</c> does for its
    /// own list: this fails the moment a new controller is added, which is exactly when somebody needs
    /// to be reminded to add it to <see cref="_controllers"/>.
    /// </summary>
    [Fact]
    public void TheControllerList_CoversEveryControllerInTheApiAssembly()
    {
        var actualControllers = typeof(AuthController).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && type.Name.EndsWith("Controller", StringComparison.Ordinal))
            .ToList();

        actualControllers.ShouldNotBeEmpty(
            "No controller was found in the API assembly by name convention, so this rule cannot " +
            "prove the enumeration above is complete.");

        actualControllers.Count.ShouldBe(
            _controllers.Length,
            "A controller was added to or removed from the API. Update the enumeration in " +
            $"{nameof(IdempotentActionsAreAlwaysPostTests)} so [Idempotent] usage on it stays covered.");
    }
}
