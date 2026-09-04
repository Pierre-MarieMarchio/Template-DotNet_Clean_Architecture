using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Files.Policies;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.Repositories;
using AppTemplate.Domain.Features.Files.ValueObjects;
using FluentValidation;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;

/// <summary>
/// Reserves a place for a file and hands back the right to deposit its bytes. The first half of an
/// upload; <c>ConfirmFileUploadUseCase</c> is the second.
/// <para>
/// <b>The row is committed before the grant is minted, and the order is the whole safety
/// argument.</b> A signed URL names a key, so a grant issued before the commit would authorise bytes
/// against a key that no row names if the commit then failed — content nobody can reach and only the
/// orphan sweep would ever remove. In this order the worst case is the opposite and much cheaper: a
/// registration whose grant was never handed out, which is a pending row the abandonment sweep takes
/// away on its own schedule.
/// </para>
/// <para>
/// The quota is checked before anything is written, because the cost being refused is the grant
/// rather than the row — see <see cref="StoredFileQuotaPolicy"/>.
/// </para>
/// </summary>
public sealed class RegisterFileUseCase(
    IStoredFileRepository repository,
    IStoredFileQueries queries,
    IFileContentStore content,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider,
    IValidator<RegisterFileCommand> validator) : IRegisterFileUseCase
{
    /// <summary>
    /// How long the client has to deposit. Long enough for a slow connection to send five gigabytes,
    /// short enough that a URL found in a log afterwards is worthless — and a client that misses it
    /// registers again rather than being stuck, since the abandoned registration costs it nothing
    /// but a slot it gets back.
    /// </summary>
    private static readonly TimeSpan _uploadWindow = TimeSpan.FromMinutes(30);

    public async Task<Result<RegisterFileOutcome>> ExecuteAsync(
        RegisterFileCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<RegisterFileOutcome>();
        }

        var validation = await validator.EnsureValidAsync(command, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<RegisterFileOutcome>();
        }

        var usage = await queries.GetUsageForOwnerAsync(userId.Value, cancellationToken);
        var quota = StoredFileQuotaPolicy.EnsureRoomFor(usage, command.SizeInBytes);

        if (quota.IsFailure)
        {
            return quota.To<RegisterFileOutcome>();
        }

        // Caught rather than left to throw: the value objects refuse things the validator above
        // cannot state without restating their rules — a reserved device name, a wildcard media
        // type, a checksum that is the right length and not hexadecimal.
        var registration = DomainGuard.Try(() => StoredFile.Register(
            userId.Value,
            StoredFileName.Create(command.Name),
            DeclaredMediaType.Create(command.MediaType),
            FileSize.Create(command.SizeInBytes),
            Sha256Checksum.Create(command.Checksum),
            dateTimeProvider.UtcNow));

        if (registration.IsFailure)
        {
            return registration.To<RegisterFileOutcome>();
        }

        var storedFile = registration.Value;

        repository.Add(storedFile);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var grant = await content.CreateUploadGrantAsync(
            storedFile.ObjectKey.Value,
            storedFile.DeclaredMediaType.Value,
            storedFile.Size.Bytes,
            _uploadWindow,
            cancellationToken);

        return new RegisterFileOutcome(storedFile.Id, grant);
    }
}
