namespace AppTemplate.Api.Features.Files.Contracts.Responses;

/// <summary>The registered file's id and the right to deposit its bytes.</summary>
public sealed record StoredFileRegistrationResponse(Guid Id, UploadGrantResponse Upload);
