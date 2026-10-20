using AppTemplate.Application.Features.Files.Configuration.Options;
using AppTemplate.Domain.Core.Common.Exceptions;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;

namespace AppTemplate.Application.Features.Files.Model.Policies;

internal static class StoredFileDeclarationPolicy
{
    public static FileCategoryOptions? CategoryOf(FilesOptions files, string? category) =>
        category is null ? null : files.Categories.GetValueOrDefault(category);

    public static bool IsDeclared(FilesOptions files, string category) =>
        FileCategory.IsValid(category) && files.Categories.ContainsKey(category);

    public static bool HasValidLength(string name)
    {
        int length = name.Trim().TrimEnd(' ', '.').Length;

        return length is > 0 and <= StoredFileName.MaxLength;
    }

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
