namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>The one answer a well-formed registration gets, whatever the state of the address.</summary>
public sealed record RegisterResponse(string Status)
{
    public static RegisterResponse Pending { get; } = new("pending");
}
