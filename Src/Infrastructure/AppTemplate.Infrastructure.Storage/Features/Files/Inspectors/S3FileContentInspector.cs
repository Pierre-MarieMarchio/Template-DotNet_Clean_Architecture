using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using AppTemplate.Application.Features.Files.Ports.FileContentInspector;
using AppTemplate.Infrastructure.Storage.Common.Budgets;
using AppTemplate.Infrastructure.Storage.Common.Options;
using AppTemplate.Infrastructure.Storage.Features.Files.Options;
using AppTemplate.Infrastructure.Storage.Features.Files.Scanners;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Inspectors;

/// <summary>
/// <see cref="IFileContentInspector"/> over an S3-compatible object store and, where one is
/// configured, a <c>clamd</c> daemon.
/// The <c>S3</c> prefix names where the bytes come from, not what examines them: the scanner is a
/// collaborator this adapter may or may not have, and the other implementation of the port is
/// <c>InMemoryFileContentInspector</c>.
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

        // One budget over both halves, not one each: an inspection that has taken the whole budget
        // has failed whichever half was slow.
        using var budget = ScannerBudget.Start(cancellationToken);

        try
        {
            using var response = await client.GetObjectAsync(
                new GetObjectRequest { BucketName = storage.Value.BucketName, Key = objectKey },
                budget.Token);

            await using var content = response.ResponseStream;

            byte[] head = new byte[ContentInspectionOutcome.MaxHeadBytes];

            // ReadAtLeastAsync, not ReadAsync: one read of a network stream returns whatever has
            // arrived, so sniffing from it would make detection depend on packet timing.
            int read = await content.ReadAtLeastAsync(
                head,
                head.Length,
                throwOnEndOfStream: false,
                budget.Token);

            var prefix = head.AsMemory(0, read);

            if (string.IsNullOrWhiteSpace(inspection.Value.ScannerHost))
            {
                // No scanner, so nothing looked and nothing was found. The head is still read and
                // the type check above this port still runs — SECURITY.md records the gap.
                return new ContentInspectionOutcome(ContentInspectionStatus.Clean, prefix, null);
            }

            if (response.ContentLength > inspection.Value.MaxScannableBytes)
            {
                // The ceiling is held here as well as in the daemon, so this is decided before a
                // byte is streamed rather than as a broken pipe half-way through.
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
            // A confirmed deposit whose object is gone: something removed it from under a live row.
            // No verdict rather than a refusal, since quarantining on an absence would refuse a file
            // over a store fault. The next pass offers it again.
            logger.LogWarning(
                exception,
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
            // Shutdown, not a failed inspection.
            throw;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning(
                exception,
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
