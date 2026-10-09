using AppTemplate.Application.Features.Files.Options;
using AppTemplate.Application.Features.Files.Policies;
using AppTemplate.Domain.Features.Files.ValueObjects;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;

/// <summary>
/// Refuses what the request and the configured categories can refuse; the value objects check the
/// rest and answer with a conflict rather than a field error.
/// </summary>
public sealed class RegisterFileCommandValidator : AbstractValidator<RegisterFileCommand>
{
    public RegisterFileCommandValidator(IOptions<FilesOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var files = options.Value;

        FileCategoryOptions? CategoryOf(RegisterFileCommand command) =>
            StoredFileDeclarationPolicy.CategoryOf(files, command.Category);

        RuleFor(command => command.Category)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A category is required.")
            .Must(category => StoredFileDeclarationPolicy.IsDeclared(files, category))
            .WithMessage(command => $"'{command.Category}' is not a file category this deployment declares.");

        RuleFor(command => command.Name)
            // Stop: every Must below dereferences the value.
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A file name is required.")
            // Measured after the domain's normalisation.
            .Must(StoredFileDeclarationPolicy.HasValidLength)
            .WithMessage($"A file name cannot exceed {StoredFileName.MaxLength} characters.");

        RuleFor(command => command.MediaType)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A media type is required.")
            .Must(mediaType => mediaType.Trim().Length <= DeclaredMediaType.MaxLength)
            .WithMessage($"A media type cannot exceed {DeclaredMediaType.MaxLength} characters.")
            .Must((command, mediaType) => StoredFileDeclarationPolicy.IsAllowed(CategoryOf(command), mediaType))
            .WithMessage(command => $"Category '{command.Category}' does not accept '{command.MediaType.Trim()}'.");

        RuleFor(command => command.SizeInBytes)
            .Cascade(CascadeMode.Stop)
            .InclusiveBetween(FileSize.MinBytes, FileSize.MaxBytes)
            .WithMessage($"A file must be between {FileSize.MinBytes} and {FileSize.MaxBytes} bytes.")
            .Must((command, size) => CategoryOf(command) is not { } category || size <= category.MaxSizeInBytes)
            .WithMessage(command =>
                $"Category '{command.Category}' accepts files of at most {CategoryOf(command)?.MaxSizeInBytes} bytes.");

        RuleFor(command => command.Checksum)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A checksum is required.")
            // Length only; the characters are the value object's rule.
            .Must(checksum => checksum.Trim().Length == Sha256Checksum.Length)
            .WithMessage($"A SHA-256 checksum is exactly {Sha256Checksum.Length} hexadecimal characters.");
    }
}
