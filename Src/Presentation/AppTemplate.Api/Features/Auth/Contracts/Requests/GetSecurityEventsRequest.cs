namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>
/// Bound from the query string. Both security-event listings share it: the only input either
/// takes is which page, since whose events they are never is.
/// </summary>
/// <remarks>
/// <c>page</c> and <c>pageSize</c> are typed as <see cref="int"/>?, so a value that is not a number
/// never reaches the controller: model binding answers 400 with <c>request.validationFailed</c>
/// and names the offending field in <c>errors</c> — the same code an Application-layer validation
/// failure carries, so a client need not tell a rejected shape from a rejected value. The bounds
/// themselves — a page past the ceiling, a size over the maximum — are the collection policy's to
/// refuse, and come back under its own code.
/// </remarks>
public sealed record GetSecurityEventsRequest(int? Page, int? PageSize);
