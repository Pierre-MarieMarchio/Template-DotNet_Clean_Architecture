using System.Buffers.Text;

namespace AppTemplate.Application.Auth.Features.AccessKeys.Model.Policies;

public static class AccessKeyPolicy
{
    public const string ReadScope = "read";

    public const string WriteScope = "write";

    public const string KeyPrefix = "ak_";

    public const int NameMaxLength = 100;

    public const int MaxActiveKeysPerAccount = 25;

    public const int SecretSizeInBytes = 32;

    public const int DisplayPrefixLength = 11;

    public static IReadOnlyList<string> Scopes { get; } = [ReadScope, WriteScope];

    public static TimeSpan LastUseResolution { get; } = TimeSpan.FromMinutes(1);

    public static bool IsWellFormed(string? key) =>
        key is not null
        && key.StartsWith(KeyPrefix, StringComparison.Ordinal)
        && Base64Url.IsValid(key.AsSpan(KeyPrefix.Length), out int secretSize)
        && secretSize == SecretSizeInBytes;
}
