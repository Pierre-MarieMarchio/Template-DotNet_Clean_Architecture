using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AppTemplate.Application.Core.Common.Ports;

namespace AppTemplate.Infrastructure.InMemory.Features.Files;

/// <summary>
/// The bucket the in-memory content store, direct transfer and inventory stand in for. Public so a
/// test can put something in it and read what came out.
/// <para>
/// <b>A deposit is something a test performs, never something issuing a grant performs</b>: the case
/// where a grant is issued and the deposit never happens is what confirmation and the abandonment
/// sweep exist for. Minted URLs are HMAC-signed over method, key and expiry under a per-instance
/// secret, so a grant is a bearer right here too (a read grant does not write, an expired one stops
/// verifying) and resolves nowhere. A singleton, internally locked.
/// </para>
/// </summary>
public sealed class StoredObjects(IDateTimeProvider dateTimeProvider)
{
    /// <summary>The origin of every minted URL; under <c>.invalid</c> (RFC 2606), so it never resolves.</summary>
    public const string Origin = "https://files.in-memory.invalid";

    private readonly object _gate = new();
    private readonly Dictionary<string, DepositedObject> _objects = new(StringComparer.Ordinal);

    /// <summary>Minted per instance, so one host cannot verify a grant another issued.</summary>
    private readonly byte[] _signingKey = RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// Performs the deposit a client would make against an upload grant. Depositing twice under one key
    /// replaces the object, as a store does.
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

            // Copied: the caller still owns its array.
            content.ToArray());

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

    /// <summary>Everything stored, ordered by key (as a listing is), as a snapshot.</summary>
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
    /// Whether <paramref name="url"/> is a grant this instance issued for <paramref name="expectedMethod"/>
    /// and still valid at <paramref name="atInstant"/>.
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

        // Fixed-time, as a real signer would compare.
        return CryptographicOperations.FixedTimeEquals(expected, presented);
    }

    /// <summary>
    /// Mints a grant URL. Internal: only the adapters issue grants, so a test cannot assert against the
    /// double instead of the port.
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

    /// <summary>The keys under a prefix, in the order a store's listing has.</summary>
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
