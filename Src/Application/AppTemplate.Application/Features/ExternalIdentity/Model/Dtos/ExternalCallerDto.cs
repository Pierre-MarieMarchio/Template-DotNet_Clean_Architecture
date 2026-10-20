namespace AppTemplate.Application.Features.ExternalIdentity.Model.Dtos;

public sealed record ExternalCallerDto(string Issuer, string Subject, string? Email, string? DisplayName);
