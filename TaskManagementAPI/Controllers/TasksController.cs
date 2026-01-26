using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaskManagementAPI.DTOs;
using TaskManagementAPI.Extensions;
using TaskManagementAPI.Services;

namespace TaskManagementAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // This entire controller requires authentication!
    public class TasksController : ControllerBase
    {
        private readonly ITaskService _taskService;

        public TasksController(ITaskService taskService)
        {
            _taskService = taskService;
        }

        [HttpPost]
        public async Task<IActionResult> CreateTask([FromBody] CreateTaskDto createTaskDto) 
        {
            try
            {
                // Get the current user's ID from the JWT token
                var userId = User.GetUserId();

                // Create the task
                var task = await _taskService.CreateTaskAsync(createTaskDto, userId);

                return CreatedAtAction(nameof(GetTaskById), new { id = task.Id }, task);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while creating the task", details = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetUserTasks() 
        {
            try
            {
                // Get the current user's ID from the JWT token
                var userId = User.GetUserId();

                // Get all tasks for this user
                var tasks = await _taskService.GetUserTasksAsync(userId);

                return Ok(tasks);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while retrieving tasks", details = ex.Message });
            }

        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetTaskById(int id) 
        {
            try
            {
                // Get the current user's ID from the JWT token
                var userId = User.GetUserId();

                // Get the task (includes authorization check)
                var task = await _taskService.GetTaskByIdAsync(id, userId);

                if (task == null)
                {
                    return NotFound(new { message = "Task not found or you don't have permission to access it" });
                }

                return Ok(task);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while retrieving the task", details = ex.Message });
            }
        }







    }
}
