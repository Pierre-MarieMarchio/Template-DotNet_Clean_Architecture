using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Features.Files.Ports.FileContentInspector;

namespace AppTemplate.Infrastructure.InMemory.Features.Files;

/// <summary>
/// The bucket <c>InMemoryFileContentStore</c> and <c>InMemoryFileContentInventory</c> stand in for.
/// Public and resolvable for the same reason <c>RecordedEmails</c> is: a double is only useful if a
/// test can put something in it and read what came out.
/// <para>
/// <b>A deposit is something a test performs, never something issuing a grant performs.</b> That is
/// not pedantry — it is the shape of the real feature. A grant is a right to write, the client
/// writes directly to the store afterwards, and the case where a grant is issued and the deposit
/// never happens is exactly what confirmation and the abandonment sweep exist for. A double that
/// wrote the object when the grant was minted would make that case untestable.
/// </para>
/// <para>
/// <b>The URLs it mints are signed, and they resolve nowhere.</b> The host is under <c>.invalid</c>,
/// which RFC 2606 reserves so that it can never be registered: a test that follows one fails at DNS
/// rather than reaching something real. The signature is an HMAC over the method, the key and the
/// expiry, under a secret minted per instance — so a grant is a bearer right here too, one issued
/// for reading does not authorise writing, and an expired one stops verifying. That is the property
/// of a signed URL worth reproducing in a double; the bytes are not.
/// </para>
/// <para>
/// A singleton for the life of the host, and internally locked, because an integration test deposits
/// and sweeps from several requests at once.
/// </para>
/// </summary>
public sealed class StoredObjects(IDateTimeProvider dateTimeProvider)
{
    /// <summary>
    /// The origin every minted URL is under. Reserved by RFC 2606 and therefore unresolvable
    /// anywhere, for ever.
    /// </summary>
    public const string Origin = "https://files.in-memory.invalid";

    private readonly object _gate = new();
    private readonly Dictionary<string, DepositedObject> _objects = new(StringComparer.Ordinal);

    /// <summary>
    /// Minted per instance and never leaves it. A fixed secret would let one host verify a grant
    /// another host issued, which is not a property any real signer has.
    /// </summary>
    private readonly byte[] _signingKey = RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// Performs the deposit a client would have made against an upload grant: measures the bytes,
    /// digests them, and files them under <paramref name="objectKey"/>. Depositing twice under one
    /// key replaces the object, exactly as a store does.
    /// </summary>
    public DepositedObject Deposit(string objectKey, string mediaType, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentNullException.ThrowIfNull(content);

        var deposited = new DepositedObject(
            objectKey,
            mediaType,
            content.LongLength,
            Convert.ToHexStringLower(SHA256.HashData(content)),
            dateTimeProvider.UtcNow,

            // Copied rather than aliased: the caller still owns its array, and a test that reused
            // one buffer for two deposits would otherwise find both objects holding the second
            // file's bytes.
            content.AsSpan(0, Math.Min(content.Length, ContentInspectionOutcome.MaxHeadBytes)).ToArray());

        lock (_gate)
        {
            _objects[objectKey] = deposited;
        }

        return deposited;
    }

    /// <summary>What is stored under a key, or <c>null</c> when nothing is.</summary>
    public DepositedObject? Find(string objectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        lock (_gate)
        {
            return _objects.GetValueOrDefault(objectKey);
        }
    }

    /// <summary>
    /// Everything stored, ordered by key, as a snapshot that will not change underneath the caller.
    /// Ordered because the listing the sweep reads is ordered, and a double that answered in hash
    /// order would let a paging defect pass.
    /// </summary>
    public IReadOnlyList<DepositedObject> Snapshot()
    {
        lock (_gate)
        {
            return [.. _objects.Values.OrderBy(stored => stored.ObjectKey, StringComparer.Ordinal)];
        }
    }

    /// <summary>Empties the store, so one test's objects cannot be found by the next.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _objects.Clear();
        }
    }

    /// <summary>
    /// Whether <paramref name="url"/> is a grant this instance issued, for
    /// <paramref name="expectedMethod"/>, still valid at <paramref name="atInstant"/>. This is what
    /// a test asserts on instead of on the shape of a string: it is the only way to tell a grant
    /// that expires from one that carries an expiry nothing reads.
    /// </summary>
    public bool IsGrantValid(string url, string expectedMethod, DateTimeOffset atInstant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedMethod);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var grant)
            || !string.Equals(grant.GetLeftPart(UriPartial.Authority), Origin, StringComparison.Ordinal))
        {
            return false;
        }

        var parameters = QueryOf(grant);

        if (!parameters.TryGetValue("expires", out string? expires)
            || !parameters.TryGetValue("signature", out string? signature)
            || !long.TryParse(expires, CultureInfo.InvariantCulture, out long expiresAt))
        {
            return false;
        }

        if (DateTimeOffset.FromUnixTimeSeconds(expiresAt) < atInstant)
        {
            return false;
        }

        string objectKey = KeyOf(grant);
        byte[] expected = Sign(expectedMethod, objectKey, expiresAt);
        byte[] presented;

        try
        {
            presented = Convert.FromHexString(signature);
        }
        catch (FormatException)
        {
            return false;
        }

        // Fixed-time, because a double that compared signatures with string equality would teach
        // whoever reads it the wrong lesson about how this comparison is written.
        return CryptographicOperations.FixedTimeEquals(expected, presented);
    }

    /// <summary>
    /// Mints the URL a grant carries. Internal: the adapters issue grants, and a test that minted
    /// one directly would be asserting against the double instead of against the port.
    /// </summary>
    internal string SignedUrl(
        string method,
        string objectKey,
        DateTimeOffset expiresAt,
        string? downloadFileName = null)
    {
        long expires = expiresAt.ToUnixTimeSeconds();
        string signature = Convert.ToHexStringLower(Sign(method, objectKey, expires));

        var url = new StringBuilder(Origin)
            .Append('/')
            .AppendJoin('/', objectKey.Split('/').Select(Uri.EscapeDataString))
            .Append(CultureInfo.InvariantCulture, $"?method={Uri.EscapeDataString(method)}")
            .Append(CultureInfo.InvariantCulture, $"&expires={expires}")
            .Append(CultureInfo.InvariantCulture, $"&signature={signature}");

        if (downloadFileName is not null)
        {
            url.Append(CultureInfo.InvariantCulture, $"&filename={Uri.EscapeDataString(downloadFileName)}");
        }

        return url.ToString();
    }

    internal void Remove(string objectKey)
    {
        lock (_gate)
        {
            _objects.Remove(objectKey);
        }
    }

    /// <summary>
    /// The keys under a prefix, ordered — the same ordering an object store's listing has, which is
    /// what the orphan sweep's paging depends on.
    /// </summary>
    internal List<string> KeysUnder(string prefix)
    {
        lock (_gate)
        {
            return
            [
                .. _objects.Keys
                    .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                    .OrderBy(key => key, StringComparer.Ordinal)
            ];
        }
    }

    private byte[] Sign(string method, string objectKey, long expiresAtUnixSeconds) =>
        HMACSHA256.HashData(
            _signingKey,
            Encoding.UTF8.GetBytes(
                string.Create(CultureInfo.InvariantCulture, $"{method}\n{objectKey}\n{expiresAtUnixSeconds}")));

    private static string KeyOf(Uri grant) =>
        string.Join('/', grant.AbsolutePath.TrimStart('/').Split('/').Select(Uri.UnescapeDataString));

    private static Dictionary<string, string> QueryOf(Uri grant)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string pair in grant.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=', StringComparison.Ordinal);

            if (separator > 0)
            {
                parameters[pair[..separator]] = Uri.UnescapeDataString(pair[(separator + 1)..]);
            }
        }

        return parameters;
    }
}
