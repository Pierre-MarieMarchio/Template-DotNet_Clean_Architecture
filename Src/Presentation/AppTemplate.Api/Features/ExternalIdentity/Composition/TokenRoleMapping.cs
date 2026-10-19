using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Api.Features.ExternalIdentity.Composition;

/// <summary>
/// Reads role names out of a provider's token at a dot path. Read from the token's JSON payload
/// rather than from the claims the handler made of it, because a nested object such as Keycloak's
/// <c>realm_access</c> arrives there as one claim holding a JSON string.
/// </summary>
internal static class TokenRoleMapping
{
    /// <returns>
    /// The distinct, non-blank strings at <paramref name="path"/>; empty when the token is not a JSON
    /// web token or the path leads nowhere. Never a failure: a missing claim means no role, the least
    /// a caller can hold.
    /// </returns>
    internal static IReadOnlyList<string> Read(SecurityToken? token, string path)
    {
        if (token is not JsonWebToken jwt)
        {
            return [];
        }

        using var payload = JsonDocument.Parse(Base64UrlEncoder.Decode(jwt.EncodedPayload));

        return RolesAt(payload.RootElement, path);
    }

    /// <summary>
    /// The strings at <paramref name="path"/> in <paramref name="root"/>: one when the path ends on a
    /// string, each string element when it ends on an array, none otherwise.
    /// </summary>
    /// <remarks>
    /// A property whose own name contains dots — Auth0's namespaced <c>https://example.com/roles</c> —
    /// is matched whole before the path is split, so the same setting reads both shapes.
    /// </remarks>
    internal static IReadOnlyList<string> RolesAt(JsonElement root, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!TryWalk(root, path, out var found))
        {
            return [];
        }

        IEnumerable<string?> values = found.ValueKind switch
        {
            JsonValueKind.String => [found.GetString()],
            JsonValueKind.Array => found.EnumerateArray()
                .Where(element => element.ValueKind == JsonValueKind.String)
                .Select(element => element.GetString()),
            _ => [],
        };

        return [.. values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)];
    }

    private static bool TryWalk(JsonElement element, string remaining, out JsonElement found)
    {
        found = default;

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (element.TryGetProperty(remaining, out found))
        {
            return true;
        }

        for (int dot = remaining.IndexOf('.', StringComparison.Ordinal);
             dot > 0;
             dot = remaining.IndexOf('.', dot + 1))
        {
            if (element.TryGetProperty(remaining[..dot], out var child)
                && TryWalk(child, remaining[(dot + 1)..], out found))
            {
                return true;
            }
        }

        return false;
    }
}
