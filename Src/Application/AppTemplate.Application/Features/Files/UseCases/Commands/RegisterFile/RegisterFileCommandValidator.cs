using AppTemplate.Application.Features.Files.Options;
using AppTemplate.Domain.Core.Common.Exceptions;
using AppTemplate.Domain.Features.Files.ValueObjects;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;

/// <summary>
/// Refuses what can be refused from the request and the configured categories: an unknown
/// category, a media type the category does not allow, a size above its maximum. The value objects
/// check the rest — reserved device names, token characters, wildcards — and their refusal arrives
/// as a conflict rather than a field error.
/// </summary>
public sealed class RegisterFileCommandValidator : AbstractValidator<RegisterFileCommand>
{
    public RegisterFileCommandValidator(IOptions<FilesOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var files = options.Value;

        RuleFor(command => command.Category)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A category is required.")
            .Must(category => FileCategory.IsValid(category) && files.Categories.ContainsKey(category))
            .WithMessage(command => $"'{command.Category}' is not a file category this deployment declares.");

        RuleFor(command => command.Name)
            // FluentValidation runs the remaining rules even after NotEmpty fails, and every Must
            // below dereferences the value.
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A file name is required.")
            // Measured after the normalisation the domain applies, or a name it would accept is
            // refused here for a length it does not have once trimmed.
            .Must(name => Normalize(name).Length is > 0 and <= StoredFileName.MaxLength)
            .WithMessage($"A file name cannot exceed {StoredFileName.MaxLength} characters.");

        RuleFor(command => command.MediaType)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A media type is required.")
            .Must(mediaType => mediaType.Trim().Length <= DeclaredMediaType.MaxLength)
            .WithMessage($"A media type cannot exceed {DeclaredMediaType.MaxLength} characters.")
            .Must((command, mediaType) => IsAllowed(CategoryOf(files, command), mediaType))
            .WithMessage(command => $"Category '{command.Category}' does not accept '{command.MediaType.Trim()}'.");

        RuleFor(command => command.SizeInBytes)
            .Cascade(CascadeMode.Stop)
            .InclusiveBetween(FileSize.MinBytes, FileSize.MaxBytes)
            .WithMessage($"A file must be between {FileSize.MinBytes} and {FileSize.MaxBytes} bytes.")
            .Must((command, size) => CategoryOf(files, command) is not { } category || size <= category.MaxSizeInBytes)
            .WithMessage(command =>
                $"Category '{command.Category}' accepts files of at most {CategoryOf(files, command)?.MaxSizeInBytes} bytes.");

        RuleFor(command => command.Checksum)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A checksum is required.")
            // Length only: whether the characters are hexadecimal is the value object's rule.
            .Must(checksum => checksum.Trim().Length == Sha256Checksum.Length)
            .WithMessage($"A SHA-256 checksum is exactly {Sha256Checksum.Length} hexadecimal characters.");
    }

    /// <summary>Exactly what <see cref="StoredFileName.Create"/> does before it measures.</summary>
    private static string Normalize(string name) => name.Trim().TrimEnd(' ', '.');

    /// <summary>The named category, or <c>null</c> when it is unknown — which the category rule reports.</summary>
    private static FileCategoryOptions? CategoryOf(FilesOptions files, RegisterFileCommand command) =>
        command.Category is { } name ? files.Categories.GetValueOrDefault(name) : null;

    /// <summary>
    /// An unknown category is reported once, on its own field. A media type the domain refuses is
    /// left to the domain, which answers with the precise reason.
    /// </summary>
    private static bool IsAllowed(FileCategoryOptions? category, string mediaType)
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
