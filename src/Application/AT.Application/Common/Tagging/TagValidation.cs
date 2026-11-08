using AppTemplate.Domain.Common.Tagging;
using FluentValidation;

namespace AppTemplate.Application.Common.Tagging;

public static class TagValidation
{
    public static IRuleBuilderOptions<T, string> IsATag<T>(this IRuleBuilder<T, string> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule
            .NotEmpty().WithMessage("A tag cannot be blank.")
            .Must(tag => string.IsNullOrWhiteSpace(tag) || tag.Trim().Length <= Tag.MaxLength)
            .WithMessage($"A tag cannot exceed {Tag.MaxLength} characters.");
    }

    public static IRuleBuilderOptions<T, IReadOnlyCollection<string>> IsATagSet<T>(
        this IRuleBuilder<T, IReadOnlyCollection<string>> rule,
        int capacity,
        string subject)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        return rule
            .NotNull().WithMessage($"A tag set is required; send an empty list to clear the {subject}'s tags.")
            .Must(tags => tags is null || tags.Count <= capacity)
            .WithMessage($"A {subject} cannot carry more than {capacity} tags.");
    }
}
