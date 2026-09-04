using AppTemplate.Domain.Common.Exceptions;

namespace AppTemplate.Domain.Features.TodoLists.ValueObjects;

/// <summary>
/// The title of a <see cref="Entities.TodoItem"/>. It exists as a type rather than a bare
/// string because "non-empty, trimmed, at most 200 characters" is a rule that must hold
/// everywhere a title appears — including on the load path — and a type is the only way to
/// guarantee that without repeating the check in every caller.
/// </summary>
public sealed record TodoItemTitle
{
    public const int MaxLength = 200;

    private TodoItemTitle(string value) => Value = value;

    public string Value { get; }

    /// <exception cref="DomainException">The value is blank or too long.</exception>
    public static TodoItemTitle Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("A to-do item title cannot be empty.");
        }

        string trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            throw new DomainException($"A to-do item title cannot exceed {MaxLength} characters.");
        }

        return new TodoItemTitle(trimmed);
    }

    public override string ToString() => Value;
}
