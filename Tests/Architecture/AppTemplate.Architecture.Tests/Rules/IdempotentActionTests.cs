using System.Reflection;
using AppTemplate.Api.Common.Idempotency;
using Microsoft.AspNetCore.Mvc;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class IdempotentActionTests
{
    [Fact]
    public void EveryIdempotentAction_IsAPost()
    {
        NonPostIdempotentActions(ApiControllers()).ShouldBeEmpty(
            "[Idempotent] is only meaningful on a POST: replaying a GET has no effect to " +
            "deduplicate, and replaying a PUT/DELETE would hide the idempotence those verbs " +
            "already have by definition.");
    }
    //#if (!publicApi)

    [Fact]
    public void TheScan_FindsTheApisControllers()
    {
        ApiControllers().ShouldNotBeEmpty(
            "No controller was found in the API assembly, so the rule above reads nothing and " +
            "passes whatever the actions declare.");
    }
    //#endif

    [Fact]
    public void TheRule_IsSensitive_AndSeesAnIdempotentGet()
    {
        NonPostIdempotentActions([typeof(DeliberatelyIdempotentGetController)]).ShouldBe(
            [$"{nameof(DeliberatelyIdempotentGetController)}.{nameof(DeliberatelyIdempotentGetController.Read)}"],
            $"{nameof(DeliberatelyIdempotentGetController)} marks a GET [Idempotent]. That the rule " +
            $"does not see it means {nameof(EveryIdempotentAction_IsAPost)} is decorative.");
    }

    private static List<Type> ApiControllers() =>
        [.. typeof(IdempotentAttribute).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(type))];

    private static List<string> NonPostIdempotentActions(IEnumerable<Type> controllers) =>
        [.. controllers
            .SelectMany(controller => controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttribute<IdempotentAttribute>() is not null
                && method.GetCustomAttribute<HttpPostAttribute>() is null)
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .Order(StringComparer.Ordinal)];

    private sealed class DeliberatelyIdempotentGetController : ControllerBase
    {
        [Idempotent]
        [HttpGet]
        public OkResult Read() => Ok();

        [Idempotent]
        [HttpPost]
        public OkResult Create() => Ok();
    }
}
