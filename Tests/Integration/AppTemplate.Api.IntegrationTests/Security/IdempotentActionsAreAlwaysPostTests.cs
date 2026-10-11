using System.Reflection;
using AppTemplate.Api.Common.Idempotency;
//#if (localAuth)
using AppTemplate.Api.Features.Auth.Controllers;
//#endif
//#if (externalAuth)
using AppTemplate.Api.Features.ExternalIdentity.Controllers;
//#endif
//#if (files)
using AppTemplate.Api.Features.Files.Controllers;
//#endif
//#if (!publicApi)
using AppTemplate.Api.Features.Maintenance.Controllers;
//#endif
//#if (notes)
using AppTemplate.Api.Features.Notes.Controllers;
//#endif
//#if (reminders)
using AppTemplate.Api.Features.Reminders.Controllers;
//#endif
//#if (todoLists)
using AppTemplate.Api.Features.TodoLists.Controllers;
//#endif
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
        //#if (localAuth)
        typeof(AccountAdministrationController),
        typeof(AccountController),
        //#endif
        //#if (externalAuth)
        typeof(ExternalIdentityAdministrationController),
        //#endif
        //#if (files)
        typeof(FilesController),
        typeof(FileTagsController),
        //#endif
        //#if (!publicApi)
        typeof(MaintenanceController),
        //#endif
        //#if (notes)
        typeof(NotesController),
        //#endif
        //#if (localAuth)
        typeof(PasskeysController),
        typeof(PasswordRecoveryController),
        typeof(RefreshTokenMaintenanceController),
        typeof(RegistrationController),
        //#endif
        //#if (reminders)
        typeof(RemindersController),
        //#endif
        //#if (localAuth)
        typeof(SecurityEventMaintenanceController),
        typeof(SessionsController),
        //#endif
        //#if (todoLists)
        typeof(TodoItemsController),
        typeof(TodoItemTagsController),
        typeof(TodoListsController),
        //#endif
        //#if (localAuth)
        typeof(TwoFactorController),
        //#endif
    ];

    [Fact]
    public void EveryIdempotentAction_IsAPost()
    {
        var idempotentActions = _controllers
            .SelectMany(controller => controller.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttribute<IdempotentAttribute>() is not null)
            .ToList();

        //#if (anyFeature)
        idempotentActions.ShouldNotBeEmpty(
            "No action carrying [Idempotent] was found across the enumerated controllers, so this " +
            "rule is guarding nothing. Either the attribute was renamed, or the controller list above " +
            "has gone stale.");

        //#endif
        int expected = 0
            //#if (notes)
            // NotesController.Create.
            + 1
            //#endif
            //#if (todoLists)
            // TodoListsController.Create and TodoListsController.AddItem.
            + 2
            //#endif
            //#if (reminders)
            // RemindersController.Schedule.
            + 1
            //#endif
            //#if (files)
            // FilesController's registration: the one file action a replay would otherwise
            // duplicate, since it is the only one that names no existing file.
            + 1
            //#endif
            ;
        idempotentActions.Count.ShouldBe(
            expected,
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
        var actualControllers = typeof(IdempotentAttribute).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && type.Name.EndsWith("Controller", StringComparison.Ordinal))
            .ToList();

        /*#if (!publicApi)*/
        actualControllers.ShouldNotBeEmpty(
            "No controller was found in the API assembly by name convention, so this rule cannot " +
            "prove the enumeration above is complete.");
        /*#else
        // Generated without accounts and without examples, the API declares no controller until the
        // first feature brings one; from then on the count below holds the list to it.
        #endif*/

        actualControllers.Count.ShouldBe(
            _controllers.Length,
            "A controller was added to or removed from the API. Update the enumeration in " +
            $"{nameof(IdempotentActionsAreAlwaysPostTests)} so [Idempotent] usage on it stays covered.");
    }
}
