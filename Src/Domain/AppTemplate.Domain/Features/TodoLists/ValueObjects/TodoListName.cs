using AppTemplate.Domain.Common.Exceptions;

namespace AppTemplate.Domain.Features.TodoLists.ValueObjects;

/// <summary>
/// The name of a <see cref="TodoList"/>. It exists as a type rather than a bare
/// string because "non-empty, trimmed, at most 200 characters" is a rule that must
/// hold everywhere a name appears, and a type is the only way to guarantee that
/// without repeating the check in every caller.
/// </summary>
public sealed record TodoListName
{
    public const int MaxLength = 200;

    private TodoListName(string value) => Value = value;

    public string Value { get; }

    public static TodoListName Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("A to-do list name cannot be empty.");
        }

        string trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            throw new DomainException($"A to-do list name cannot exceed {MaxLength} characters.");
        }

        return new TodoListName(trimmed);
    }

    public override string ToString() => Value;
}
