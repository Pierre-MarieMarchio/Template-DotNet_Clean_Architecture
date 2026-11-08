using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Domain.Features.TodoLists.Model.ValueObjects;

public sealed record TodoItemTitle
{
    public const int MaxLength = 200;

    private TodoItemTitle(string value) => Value = value;

    public string Value { get; }

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
