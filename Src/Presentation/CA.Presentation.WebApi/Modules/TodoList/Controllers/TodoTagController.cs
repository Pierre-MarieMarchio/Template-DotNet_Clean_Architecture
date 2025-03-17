using CA.Application.Modules.TodoList.Features.TodoTagFeature.DTOs;
using CA.Application.Modules.TodoList.Features.TodoTagFeature.UseCases;
using CA.Application.Modules.TodoList.Interfaces.Managers;
using Microsoft.AspNetCore.Mvc;

namespace CA.Presentation.WebApi.Modules.TodoList.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TodoTagController(ITodoTagManager todoTagManager) : ControllerBase
    {
        [HttpGet]
        public virtual async Task<ActionResult<List<TodoTagDTO>>> GetAll()
        {


            var items = await todoTagManager.GetAll.ExecuteAsync();
            return Ok(items);

        }

        [HttpGet("{id}")]
        public virtual async Task<ActionResult<List<TodoTagDTO>>> GetById(Guid id)
        {


            var item = await todoTagManager.GetById.ExecuteAsync(id);
            return Ok(item);
        }

        [HttpPost]
        public virtual async Task<ActionResult<List<TodoTagDTO>>> Create(TodoTagCreateRequest request)
        {

            var item = await todoTagManager.Create.ExecuteAsync(request);
            return Ok(item);
        }

        [HttpPut]
        public virtual async Task<ActionResult<List<TodoTagDTO>>> Update(TodoTagUpdateRequest request)
        {


            var item = await todoTagManager.Update.ExecuteAsync(request);
            return Ok(item);
        }

        [HttpDelete("{id}")]
        public virtual async Task<ActionResult<List<TodoTagDTO>>> Delete(Guid id)
        {

            var item = await todoTagManager.Delete.ExecuteAsync(id);
            return Ok(item);

        }
    }
}
