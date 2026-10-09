namespace AppTemplate.Application.Features.Files.Ports.FileContentStore;

/// <summary>What <see cref="IFileContentStore.WriteAsync"/> did with the bytes it was given.</summary>
public enum ContentWriteOutcome
{
    /// <summary>The bytes matched the declared size and digest and are stored under the key.</summary>
    Written,

    /// <summary>The bytes did not match. Nothing is stored under the key.</summary>
    ContentMismatch,
}
