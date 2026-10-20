using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AppTemplate.Application.Core.Common.Ports;

namespace AppTemplate.Infrastructure.InMemory.Features.Files;

public sealed class StoredObjects(IDateTimeProvider dateTimeProvider)
{
    public const string Origin = "https://files.in-memory.invalid";

    private readonly object _gate = new();
    private readonly Dictionary<string, DepositedObject> _objects = new(StringComparer.Ordinal);

    private readonly byte[] _signingKey = RandomNumberGenerator.GetBytes(32);

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

            content.ToArray());

        lock (_gate)
        {
            _objects[objectKey] = deposited;
        }

        return deposited;
    }

    public DepositedObject? Find(string objectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        lock (_gate)
        {
            return _objects.GetValueOrDefault(objectKey);
        }
    }

    public IReadOnlyList<DepositedObject> Snapshot()
    {
        lock (_gate)
        {
            return [.. _objects.Values.OrderBy(stored => stored.ObjectKey, StringComparer.Ordinal)];
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _objects.Clear();
        }
    }

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

        return CryptographicOperations.FixedTimeEquals(expected, presented);
    }

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
