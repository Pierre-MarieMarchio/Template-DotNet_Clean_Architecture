using AppTemplate.Application.Features.Files.Options;
using AppTemplate.Application.Features.Files.Policies;
using AppTemplate.Domain.Features.Files.ValueObjects;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.UploadFile;

/// <summary>
/// The category's rules as for a direct registration, plus the API upload ceiling. What concerns
/// the content — its media type, its size, the stream itself — is reported on <c>file</c>, the form
/// field that carries it. The value objects check the rest and answer with a conflict.
/// </summary>
public sealed class UploadFileCommandValidator : AbstractValidator<UploadFileCommand>
{
    private const string _file = "File";

    public UploadFileCommandValidator(IOptions<FilesOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var files = options.Value;

        FileCategoryOptions? CategoryOf(UploadFileCommand command) =>
            StoredFileDeclarationPolicy.CategoryOf(files, command.Category);

        RuleFor(command => command.Category)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A category is required.")
            .Must(category => StoredFileDeclarationPolicy.IsDeclared(files, category))
            .WithMessage(command => $"'{command.Category}' is not a file category this deployment declares.");

        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A file name is required.")
            .Must(StoredFileDeclarationPolicy.HasValidLength)
            .WithMessage($"A file name cannot exceed {StoredFileName.MaxLength} characters.");

        RuleFor(command => command.MediaType)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("The file part must declare a media type.")
            .Must(mediaType => mediaType.Trim().Length <= DeclaredMediaType.MaxLength)
            .WithMessage($"A media type cannot exceed {DeclaredMediaType.MaxLength} characters.")
            .Must((command, mediaType) => StoredFileDeclarationPolicy.IsAllowed(CategoryOf(command), mediaType))
            .WithMessage(command => $"Category '{command.Category}' does not accept '{command.MediaType.Trim()}'.")
            .OverridePropertyName(_file);

        RuleFor(command => command.SizeInBytes)
            .Cascade(CascadeMode.Stop)
            .InclusiveBetween(FileSize.MinBytes, FileSize.MaxBytes)
            .WithMessage($"A file must be between {FileSize.MinBytes} and {FileSize.MaxBytes} bytes.")
            .LessThanOrEqualTo(files.ApiUploadMaxBytes)
            .WithMessage($"A file uploaded through the API is at most {files.ApiUploadMaxBytes} bytes.")
            .Must((command, size) => CategoryOf(command) is not { } category || size <= category.MaxSizeInBytes)
            .WithMessage(command =>
                $"Category '{command.Category}' accepts files of at most {CategoryOf(command)?.MaxSizeInBytes} bytes.")
            .OverridePropertyName(_file);

        RuleFor(command => command.Content)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("A file is required.")
            .Must((command, content) => content.CanSeek && content.Length == command.SizeInBytes)
            .WithMessage("The file's content does not have the length it was announced with.")
            .OverridePropertyName(_file);
    }
}
