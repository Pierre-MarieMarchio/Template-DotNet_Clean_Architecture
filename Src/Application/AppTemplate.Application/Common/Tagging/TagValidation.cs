using AppTemplate.Domain.Common.Tagging;
using FluentValidation;

namespace AppTemplate.Application.Common.Tagging;

/// <summary>What a caller's tag input has to satisfy before it reaches the domain, for every feature that accepts tags.</summary>
/// <remarks>The domain holds the rules — these restate them so a caller gets one 400 naming every
/// field it got wrong rather than a 500 from the first exception, and read the domain's constants
/// rather than inventing bounds. It lives in this layer's business <c>Common/</c> because it names
/// <see cref="Tag"/>, and a Core project may name no business type.</remarks>
public static class TagValidation
{
    /// <summary>One tag: not blank, and within the bound after trimming, the way the domain measures it.</summary>
    /// <typeparam name="T">The command being validated.</typeparam>
    /// <param name="rule">The property holding the tag.</param>
    /// <returns>The rule, for chaining.</returns>
    /// <remarks>The length rule tolerates a blank value rather than relying on <c>CascadeMode.Stop</c>,
    /// so one rule serves both <c>RuleFor</c> and <c>RuleForEach</c> and a blank tag still produces
    /// exactly one message.</remarks>
    public static IRuleBuilderOptions<T, string> IsATag<T>(this IRuleBuilder<T, string> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule
            .NotEmpty().WithMessage("A tag cannot be blank.")
            // Measured after trimming, like the domain measures it.
            .Must(tag => string.IsNullOrWhiteSpace(tag) || tag.Trim().Length <= Tag.MaxLength)
            .WithMessage($"A tag cannot exceed {Tag.MaxLength} characters.");
    }

    /// <summary>A whole tag set sent as a replacement: present, and no larger than the owner will carry.</summary>
    /// <typeparam name="T">The command being validated.</typeparam>
    /// <param name="rule">The property holding the set.</param>
    /// <param name="capacity">The owning aggregate's cap, read from the aggregate itself.</param>
    /// <param name="subject">How the owner is named in a refusal — the same word the aggregate gives
    /// <see cref="TagSet"/>, so a caller reads one sentence for one rule whichever layer refused it.</param>
    /// <returns>The rule, for chaining.</returns>
    /// <remarks>An absent set is refused and an empty one is not: the first is a caller that forgot
    /// the field, the second a caller asking for no tags — answering them the same way would make
    /// clearing a set impossible to express.</remarks>
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
