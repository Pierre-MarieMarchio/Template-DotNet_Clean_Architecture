using AppTemplate.Application.Features.Files.Ports.FileContentInspector;

namespace AppTemplate.Infrastructure.InMemory.Features.Files;

/// <summary>
/// What the malware scanner would say, arranged per object key. The observable surface of
/// <c>InMemoryFileContentInspector</c>, public for the same reason <see cref="StoredObjects"/> is:
/// a double is only useful if a test can put something in it.
/// <para>
/// <b>It arranges the scanner's half and never the content's half.</b> What a file <em>is</em> comes
/// from the bytes a test actually deposited into <see cref="StoredObjects"/>, through the same
/// signature table production uses — so a test that deposits an SVG under a PNG declaration gets a
/// real refusal from real logic, and a table that stopped recognising SVG would turn that test red.
/// Whether the file carries malware is the half no double can derive from bytes, because deriving it
/// is the entire job of the thing being stood in for. So that half, and only that half, is stated.
/// </para>
/// <para>
/// <b>Nothing arranged means clean.</b> A double that demanded an arrangement per object would make
/// every test that merely uploads a file also state an antivirus verdict it does not care about, and
/// the default that matters — a deployment with no scanner at all — is exactly this one: the content
/// check runs, and nothing looked for malware.
/// </para>
/// <para>
/// A singleton for the life of the host, and internally locked, because an integration test may
/// deposit and inspect from several requests at once.
/// </para>
/// </summary>
public sealed class ArrangedInspections
{
    private readonly object _gate = new();

    private readonly Dictionary<string, (ContentInspectionStatus Status, string? Signature)> _verdicts =
        new(StringComparer.Ordinal);

    /// <summary>The scanner names something in the object under <paramref name="objectKey"/>.</summary>
    /// <param name="signature">What it names it. Any non-empty string; a real one looks like
    /// <c>Win.Test.EICAR_HDB-1</c>.</param>
    public void Infect(string objectKey, string signature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);

        Set(objectKey, ContentInspectionStatus.Infected, signature);
    }

    /// <summary>
    /// The scanner refuses to look at the object under <paramref name="objectKey"/> — it is past the
    /// stream limit the daemon accepts. Permanent, and refused rather than retried.
    /// </summary>
    public void RefuseAsTooLarge(string objectKey) =>
        Set(objectKey, ContentInspectionStatus.NotInspectable, signature: null);

    /// <summary>
    /// Nothing can be examined right now: the store or the scanner is unreachable. The one
    /// arrangement that produces no verdict at all, and the only way to test that an outage neither
    /// releases a file nor destroys it.
    /// </summary>
    public void MakeUnavailable(string objectKey) =>
        Set(objectKey, ContentInspectionStatus.Unavailable, signature: null);

    /// <summary>Forgets every arrangement, so one test's verdicts cannot reach the next.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _verdicts.Clear();
        }
    }

    /// <summary>
    /// What was arranged for a key, or clean when nothing was. Internal: the adapter answers the
    /// port, and a test reading this directly would be asserting against the double.
    /// </summary>
    internal (ContentInspectionStatus Status, string? Signature) VerdictFor(string objectKey)
    {
        lock (_gate)
        {
            return _verdicts.TryGetValue(objectKey, out var arranged)
                ? arranged
                : (ContentInspectionStatus.Clean, null);
        }
    }

    private void Set(string objectKey, ContentInspectionStatus status, string? signature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        lock (_gate)
        {
            _verdicts[objectKey] = (status, signature);
        }
    }
}
