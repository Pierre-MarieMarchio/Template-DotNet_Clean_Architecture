namespace AppTemplate.Api.Features.Maintenance.Contracts.Responses;

/// <summary>
/// The outcome of a purge, shared by every purge endpoint: they all report the same thing, and an
/// object leaves room for a second field where a bare scalar body would have to break its callers.
/// </summary>
/// <param name="Deleted">Rows removed by this call. Zero is a normal answer, not an absence.</param>
public sealed record PurgeResponse(int Deleted);
