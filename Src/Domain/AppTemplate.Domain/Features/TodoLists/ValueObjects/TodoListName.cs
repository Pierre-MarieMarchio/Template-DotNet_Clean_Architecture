using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Domain.Features.TodoLists.ValueObjects;

/// <summary>
/// The name of a <see cref="Entities.TodoList"/>, non-empty, trimmed, at most 200 characters. A
/// type rather than a bare string, because the rule must hold everywhere a name appears.
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
