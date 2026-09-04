using AppTemplate.Application.Features.Files.Ports.FileContentInspector;

namespace AppTemplate.Infrastructure.InMemory.Features.Files;

/// <summary>
/// An <see cref="IFileContentInspector"/> that reads the objects <see cref="StoredObjects"/> holds
/// and opens no socket.
/// <para>
/// Internal and sealed, like every other double here: the observable surface is
/// <see cref="ArrangedInspections"/>, not this class.
/// </para>
/// <para>
/// <b>Half of what it answers is real and half is arranged, and the split is the same one the port
/// itself makes.</b> The head comes from the bytes a test actually deposited, so the type check
/// above this port runs against real content through real logic; the malware verdict is whatever the
/// test said, because a scanner is precisely the thing no double can stand in for by computing.
/// </para>
/// <para>
/// <b>An object that is not there is reported as unavailable rather than as clean.</b> That is what
/// the real adapter does when the store answers 404 for a live row, and it is the safe direction:
/// treating a missing object as nothing-found would release a file whose content nobody ever saw.
/// </para>
/// </summary>
internal sealed class InMemoryFileContentInspector(StoredObjects objects, ArrangedInspections inspections)
    : IFileContentInspector
{
    public Task<ContentInspectionOutcome> InspectAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        cancellationToken.ThrowIfCancellationRequested();

        (var status, string? signature) = inspections.VerdictFor(objectKey);

        // Nothing under the key, or nothing readable: no head, because the real adapter has none to
        // give when the read is what failed.
        if (status == ContentInspectionStatus.Unavailable || objects.Find(objectKey) is not { } stored)
        {
            return Task.FromResult(
                new ContentInspectionOutcome(
                    ContentInspectionStatus.Unavailable,
                    ReadOnlyMemory<byte>.Empty,
                    null));
        }

        // The head travels even with a verdict of Infected or NotInspectable, exactly as the real
        // adapter's does: the policy reaches those branches first and never looks at it, and a
        // double that withheld it would be encoding that ordering a second time, where it could
        // quietly disagree.
        return Task.FromResult(new ContentInspectionOutcome(status, stored.Head, signature));
    }
}
