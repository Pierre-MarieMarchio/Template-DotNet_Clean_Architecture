using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using AppTemplate.Application.Features.Files.Ports.FileContentInspector;
using AppTemplate.Infrastructure.Storage.Buckets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Inspection;

/// <summary>
/// <see cref="IFileContentInspector"/> over an S3-compatible object store and, where one is
/// configured, a <c>clamd</c> daemon.
/// <para>
/// <b>The prefix names where the bytes come from, not what examines them.</b> Two modules implement
/// this port — this one and <c>InMemoryFileContentInspector</c> — so each says which it is, exactly
/// as the two content stores do. The scanner is a collaborator this adapter may or may not have, and
/// naming the class after it would promise a dependency that half of the deployments using this
/// template will not configure.
/// </para>
/// <para>
/// <b>It lives in this module because reading the store is what it does.</b> An inspection cannot be
/// performed without opening the object, and opening the object means the bucket, the credentials,
/// the endpoint and the retry budget that <see cref="Buckets.StorageOptions"/> and
/// <see cref="BucketBudget"/> already hold. A separate module would need its own copy of all of it —
/// and could not borrow this one's, because only the persistence project may be shared between
/// infrastructure modules, and a port in the application layer declared so that one adapter could
/// call another would be a port no use case consumes, which the architecture rules refuse by name.
/// So the module's reason to change is unchanged and stated more precisely than before: it is how
/// this application reaches a file's bytes. Examining them is reaching them.
/// </para>
/// <para>
/// <b>This module escapes the hosts' outbound HTTP policy twice over</b>: the AWS SDK builds its own
/// <c>HttpClient</c>, and <c>clamd</c> does not speak HTTP at all. <see cref="BucketBudget"/> and
/// <see cref="ScannerBudget"/> are where the same budget is restated for each, and why.
/// </para>
/// <para>
/// <b>It unpacks nothing.</b> It reads a bounded prefix and copies the remainder past the scanner
/// without interpreting a byte, so the decompression bomb <c>SECURITY.md</c> records as unaddressed
/// stays outside this process. <see cref="ClamAvScanner"/> says where that hazard does land and what
/// a deployment owes it.
/// </para>
/// </summary>
internal sealed class S3FileContentInspector(
    IAmazonS3 client,
    IOptions<StorageOptions> storage,
    IOptions<ContentInspectionOptions> inspection,
    ILogger<S3FileContentInspector> logger) : IFileContentInspector
{
    public async Task<ContentInspectionOutcome> InspectAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        // One budget over both halves. The scanner's own total happens to be the same number, and
        // the two are deliberately not added together: an inspection that has taken thirty seconds
        // has failed whichever half was slow, and a file whose read and scan each take twenty-nine
        // seconds is a file this deployment cannot inspect at the size it is accepting.
        using var budget = ScannerBudget.Start(cancellationToken);

        try
        {
            using var response = await client.GetObjectAsync(
                new GetObjectRequest { BucketName = storage.Value.BucketName, Key = objectKey },
                budget.Token);

            await using var content = response.ResponseStream;

            byte[] head = new byte[ContentInspectionOutcome.MaxHeadBytes];

            // ReadAtLeastAsync, not ReadAsync: a single read of a network stream returns whatever
            // has arrived, which for a large object is routinely a few hundred bytes. Sniffing from
            // a short prefix would make detection depend on packet timing — the same file would be
            // recognised on one pass and not on the next.
            int read = await content.ReadAtLeastAsync(
                head,
                head.Length,
                throwOnEndOfStream: false,
                budget.Token);

            var prefix = head.AsMemory(0, read);

            if (string.IsNullOrWhiteSpace(inspection.Value.ScannerHost))
            {
                // No scanner: the head is still read and the type check above this port still runs.
                // Reported as Clean because nothing was found — nothing looked — and the deployment
                // chose that. SECURITY.md is where it is written down for whoever inherits it.
                return new ContentInspectionOutcome(ContentInspectionStatus.Clean, prefix, null);
            }

            if (response.ContentLength > inspection.Value.MaxScannableBytes)
            {
                // Decided before a byte is streamed, which is the point of holding the ceiling here
                // as well as in the daemon: the alternative is discovering it half-way through a
                // transfer, as a broken pipe that has already cost the bandwidth.
                return new ContentInspectionOutcome(ContentInspectionStatus.NotInspectable, prefix, null);
            }

            (var status, string? signature) = await ClamAvScanner.ScanAsync(
                inspection.Value.ScannerHost,
                inspection.Value.ScannerPort,
                prefix,
                content,
                budget.Token);

            return new ContentInspectionOutcome(status, prefix, signature);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // The row says a deposit was confirmed and the store has nothing under the key, so
            // something removed the object from underneath a live row. Reported as no verdict rather
            // than as a refusal: nothing was found in content nobody read, and quarantining on the
            // strength of an absence would refuse a file over an object-store fault. It will be
            // offered again on the next pass, and the warning is what makes a permanent one visible.
            logger.LogWarning(
                "Nothing is stored under '{ObjectKey}', although a file's deposit was confirmed " +
                "against it. Its content cannot be inspected and it stays unavailable.",
                objectKey);

            return Unavailable;
        }
        catch (AmazonS3Exception exception)
        {
            logger.LogWarning(
                exception,
                "The object store could not be read while inspecting '{ObjectKey}'; the next pass " +
                "will try again.",
                objectKey);

            return Unavailable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown, not a failed inspection. Rethrowing keeps cancellation honest rather than
            // reporting a stopping host as an unreachable store.
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Inspecting '{ObjectKey}' ran past its {Budget} budget; the next pass will try again.",
                objectKey,
                ScannerBudget.TotalTimeout);

            return Unavailable;
        }
    }

    /// <summary>
    /// The one outcome that carries nothing: no head, because nothing was read, and no signature,
    /// because nothing looked.
    /// </summary>
    private static ContentInspectionOutcome Unavailable { get; } =
        new(ContentInspectionStatus.Unavailable, ReadOnlyMemory<byte>.Empty, null);
}
