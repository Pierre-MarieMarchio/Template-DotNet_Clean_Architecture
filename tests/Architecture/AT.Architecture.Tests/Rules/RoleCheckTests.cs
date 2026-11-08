using System.Reflection;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Architecture.Tests.Fixtures;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class RoleCheckTests
{
    private static readonly string[] _roleWords = ["Role", "Claim", "Principal", "Policy"];

    private static readonly string[] _roleCheckingNamespaces =
    [
        "System.Security.Claims",
        "System.Security.Principal",
        "AppTemplate.Presentation.Core.Common.Security",
    ];

    [Fact]
    public void TheCurrentUser_NamesTheCallerAndCarriesNoRole()
    {
        var members = typeof(ICurrentUser)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Concat(ArchitectureAssemblies.ApplicationCore.GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(method => method.GetParameters() is [var first, ..] && first.ParameterType == typeof(ICurrentUser)))
            .ToList();

        members.ShouldNotBeEmpty("ICurrentUser exposes the caller's id; finding no member means the scan is broken.");

        members
            .Where(member => _roleWords.Any(word => member.Name.Contains(word, StringComparison.Ordinal))
                || TypeOf(member)?.Namespace?.StartsWith("System.Security", StringComparison.Ordinal) == true)
            .Select(member => $"{member.DeclaringType?.Name}.{member.Name}")
            .ShouldBeEmpty(
                "A use case knows who the caller is, never the caller's roles or claims: a role is a policy the host "
                + "puts on what it exposes. Remove the member, and check the role in the host.");
    }

    [Fact]
    public void NoApplicationType_ChecksARoleOrAPolicy()
    {
        foreach (var application in ArchitectureAssemblies.ApplicationLayer)
        {
            RuleAssertions.RequireTypes(application);

            Types.InAssembly(application)
                .ShouldNot()
                .HaveDependencyOnAny(_roleCheckingNamespaces)
                .GetResult()
                .ShouldHold(
                    $"{ArchitectureAssemblies.NamespaceOf(application)} reads a principal, calls IsInRole or names an "
                    + "authorisation policy. A role is checked by the host that exposes the use case; "
                    + "the use case sees the caller through ICurrentUser alone.");
        }
    }

    private static Type? TypeOf(MemberInfo member) => member switch
    {
        PropertyInfo property => property.PropertyType,
        MethodInfo method => method.ReturnType,
        _ => null,
    };
}
