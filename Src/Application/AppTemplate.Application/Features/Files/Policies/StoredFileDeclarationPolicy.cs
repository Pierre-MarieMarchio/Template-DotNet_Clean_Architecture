using AppTemplate.Application.Features.Files.Options;
using AppTemplate.Domain.Core.Common.Exceptions;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Application.Features.Files.Policies;

/// <summary>
/// The field-shaped half of what a new file may declare, shared by both upload modes' validators.
/// The value objects remain the rule; this only lets a validator refuse early on the right field.
/// </summary>
internal static class StoredFileDeclarationPolicy
{
    /// <summary>The declared category's settings, or <c>null</c> when it is not declared.</summary>
    public static FileCategoryOptions? CategoryOf(FilesOptions files, string? category) =>
        category is null ? null : files.Categories.GetValueOrDefault(category);

    /// <summary>Whether <paramref name="category"/> is well formed and declared.</summary>
    public static bool IsDeclared(FilesOptions files, string category) =>
        FileCategory.IsValid(category) && files.Categories.ContainsKey(category);

    /// <summary>Whether the name fits once normalised exactly as <see cref="StoredFileName.Create"/> does.</summary>
    public static bool HasValidLength(string name)
    {
        int length = name.Trim().TrimEnd(' ', '.').Length;

        return length is > 0 and <= StoredFileName.MaxLength;
    }

    /// <summary>
    /// Whether <paramref name="category"/> accepts <paramref name="mediaType"/>. An unknown category
    /// and a media type the domain refuses both pass: each is reported elsewhere, once.
    /// </summary>
    public static bool IsAllowed(FileCategoryOptions? category, string mediaType)
    {
        if (category is null)
        {
            return true;
        }

        try
        {
            return category.Allows(DeclaredMediaType.Create(mediaType));
        }
        catch (DomainException)
        {
            return true;
        }
    }
}
