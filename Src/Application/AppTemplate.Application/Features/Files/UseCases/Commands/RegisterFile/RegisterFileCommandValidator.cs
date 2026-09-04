using AppTemplate.Domain.Features.Files.ValueObjects;
using FluentValidation;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;

/// <summary>
/// Refuses what can be refused from the request alone. The value objects check the same things
/// again and more besides — reserved device names, token characters, wildcards — and a failure
/// there arrives as a conflict rather than a field error, which is the right shape for a rule the
/// caller could not have known but the wrong shape for a length.
/// </summary>
public sealed class RegisterFileCommandValidator : AbstractValidator<RegisterFileCommand>
{
    public RegisterFileCommandValidator()
    {
        RuleFor(command => command.Name)
            // Every Must below dereferences the value, and FluentValidation runs the remaining rules
            // for a property even after NotEmpty has failed.
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A file name is required.")
            // Measured after the same normalisation the domain applies, or a name the domain would
            // accept is refused here for a length it does not have once trimmed.
            .Must(name => Normalize(name).Length is > 0 and <= StoredFileName.MaxLength)
            .WithMessage($"A file name cannot exceed {StoredFileName.MaxLength} characters.");

        RuleFor(command => command.MediaType)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A media type is required.")
            .Must(mediaType => mediaType.Trim().Length <= DeclaredMediaType.MaxLength)
            .WithMessage($"A media type cannot exceed {DeclaredMediaType.MaxLength} characters.");

        RuleFor(command => command.SizeInBytes)
            .InclusiveBetween(FileSize.MinBytes, FileSize.MaxBytes)
            .WithMessage($"A file must be between {FileSize.MinBytes} and {FileSize.MaxBytes} bytes.");

        RuleFor(command => command.Checksum)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A checksum is required.")
            // Length only. Whether the characters are hexadecimal is the value object's rule, and
            // stating it twice is how the two answers drift apart.
            .Must(checksum => checksum.Trim().Length == Sha256Checksum.Length)
            .WithMessage($"A SHA-256 checksum is exactly {Sha256Checksum.Length} hexadecimal characters.");
    }

    /// <summary>Exactly what <see cref="StoredFileName.Create"/> does before it measures.</summary>
    private static string Normalize(string name) => name.Trim().TrimEnd(' ', '.');
}
