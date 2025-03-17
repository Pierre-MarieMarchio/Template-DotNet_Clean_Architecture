using CA.Application.Modules.TodoList.Features.ListTodosFeature.DTOs;
using CA.Application.Modules.TodoList.Features.ListTodosFeature.UseCases;
using CA.Application.Modules.TodoList.Interfaces.Managers;
using Microsoft.AspNetCore.Mvc;

namespace CA.Presentation.WebApi.Modules.TodoList.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ListTodosController(IListTodosManager listTodosManager) : ControllerBase
    {
        [HttpGet]
        public virtual async Task<ActionResult<List<ListTodosDTO>>> GetAll()
        {


            var items = await listTodosManager.GetAll.ExecuteAsync();
            return Ok(items);


        }

        [HttpGet("{id}")]
        public virtual async Task<ActionResult<List<ListTodosDTO>>> GetById(Guid id)
        {


            var item = await listTodosManager.GetById.ExecuteAsync(id);
            return Ok(item);

        }

        [HttpPost]
        public virtual async Task<ActionResult<List<ListTodosDTO>>> Create(ListTodosCreateRequest request)
        {

            var item = await listTodosManager.Create.ExecuteAsync(request);
            return Ok(item);


        }

        [HttpPut]
        public virtual async Task<ActionResult<List<ListTodosDTO>>> Update(ListTodosUpdateRequest request)
        {


            var item = await listTodosManager.Update.ExecuteAsync(request);
            return Ok(item);

        }

        [HttpDelete("{id}")]
        public virtual async Task<ActionResult<List<ListTodosDTO>>> Delete(Guid id)
        {


            var item = await listTodosManager.Delete.ExecuteAsync(id);
            return Ok(item);

        }
    }
}
