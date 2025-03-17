using CA.Application.Commons.Interfaces.Services;
using CA.Application.Modules.TodoList.Features.TodoItemFeature.DTOs;
using CA.Application.Modules.TodoList.Interfaces.Repositories;
using CA.Application.Modules.TodoList.Interfaces.UseCases;
using CA.Domain.Modules.TodoList.Entity;
using FluentValidation;


namespace CA.Application.Modules.TodoList.Features.TodoItemFeature.UseCases;

public class ListTodosUpdateUseCase(ITodoItemRepository todoItemRepository, IAuthenticatedUserService authenticatedUser) : ITodoItemUpdateUseCase
{
    public async Task<TodoItemDTO> ExecuteAsync(TodoItemUpdateRequest request)
    {
        var todoItem = new TodoItem
        {
            Id = request.Id,
            Title = request.Title,
            Description = request.Description,
            TodoTags = request.TagIds?.Select(tagId => new TodoTag { Id = tagId }).ToList(),
            TodoListId = request.TodoListId,
            UserId = Guid.Parse(authenticatedUser.UserId),
        };

        var result = await todoItemRepository.UpdateAsync(todoItem);

        var res = new TodoItemDTO(result);

        return res;
    }
}

class TodoItemUpdateValidator : AbstractValidator<TodoItemUpdateRequest>
{
    public TodoItemUpdateValidator()
    {
        RuleFor(x => x.Id)
           .NotEmpty().WithMessage("L'ID du TodoItem est requis.");

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Le titre est requis.")
            .MaximumLength(200).WithMessage("Le titre ne peut pas dépasser 200 caractères.");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("La description ne peut pas dépasser 1000 caractères.");

        RuleFor(x => x.TodoListId)
            .NotEmpty().WithMessage("L'ID de la liste est requis.");

        RuleFor(x => x.TagIds)
            .Must(tags => tags == null || tags.All(id => id != Guid.Empty))
            .WithMessage("Tous les identifiants de tags doivent être valides.");
    }
}

public class TodoItemUpdateRequest
{
    public required Guid Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public ICollection<Guid>? TagIds { get; set; }
    public required Guid TodoListId { get; set; }
}
