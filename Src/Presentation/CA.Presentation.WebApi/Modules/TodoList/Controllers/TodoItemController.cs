using CA.Application.Modules.TodoList.Features.TodoItemFeature.DTOs;
using CA.Application.Modules.TodoList.Features.TodoItemFeature.UseCases;
using CA.Application.Modules.TodoList.Interfaces.Managers;
using Microsoft.AspNetCore.Mvc;

namespace CA.Presentation.WebApi.Modules.TodoList.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TodoItemController(ITodoItemManager todoItemManager) : ControllerBase
    {
        [HttpGet]
        public virtual async Task<ActionResult<List<TodoItemDTO>>> GetAll()
        {


            var items = await todoItemManager.GetAll.ExecuteAsync();
            return Ok(items);

        }

        [HttpGet("{id}")]
        public virtual async Task<ActionResult<List<TodoItemDTO>>> GetById(Guid id)
        {


            var item = await todoItemManager.GetById.ExecuteAsync(id);
            return Ok(item);

        }

        [HttpPost]
        public virtual async Task<ActionResult<List<TodoItemDTO>>> Create(TodoItemCreateRequest request)
        {

            var item = await todoItemManager.Create.ExecuteAsync(request);
            return Ok(item);

        }

        [HttpPut]
        public virtual async Task<ActionResult<List<TodoItemDTO>>> Update(TodoItemUpdateRequest request)
        {


            var item = await todoItemManager.Update.ExecuteAsync(request);
            return Ok(item);

        }

        [HttpDelete("{id}")]
        public virtual async Task<ActionResult<List<TodoItemDTO>>> Delete(Guid id)
        {

            var item = await todoItemManager.Delete.ExecuteAsync(id);
            return Ok(item);

        }
    }
}
