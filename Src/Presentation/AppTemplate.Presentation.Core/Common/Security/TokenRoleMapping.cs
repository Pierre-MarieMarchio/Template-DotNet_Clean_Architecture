using System.Buffers.Text;
using System.Text.Json;

namespace AppTemplate.Presentation.Core.Common.Security;

public static class TokenRoleMapping
{
    public static IReadOnlyList<string> Read(string? encodedPayload, string path)
    {
        if (string.IsNullOrEmpty(encodedPayload))
        {
            return [];
        }

        using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(encodedPayload));

        return RolesAt(payload.RootElement, path);
    }

    public static IReadOnlyList<string> RolesAt(JsonElement root, string path)
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
