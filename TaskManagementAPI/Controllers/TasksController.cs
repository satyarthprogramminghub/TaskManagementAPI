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

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTask(int id, [FromBody] UpdateTaskDto updateTaskDto)
        {
            try
            {
                var userId = User.GetUserId();
                var task = await _taskService.UpdateTaskAsync(id, updateTaskDto, userId);

                if (task == null)
                {
                    return NotFound(new { message = "Task not found or you don't have permission to update it" });
                }

                return Ok(task);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while updating the task", details = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTask(int id)
        {
            try
            {
                var userId = User.GetUserId();
                var success = await _taskService.DeleteTaskAsync(id, userId);

                if (!success)
                {
                    return NotFound(new { message = "Task not found or you don't have permission to delete it" });
                }

                return NoContent();
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while deleting the task", details = ex.Message });
            }
        }

        [HttpPatch("{id}/toggle")]
        public async Task<IActionResult> ToggleTaskCompletion(int id)
        {
            try
            {
                var userId = User.GetUserId();
                var task = await _taskService.ToggleTaskCompletionAsync(id, userId);

                if (task == null)
                {
                    return NotFound(new { message = "Task not found or you don't have permission to modify it" });
                }

                return Ok(task);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while toggling task completion", details = ex.Message });
            }
        }







    }
}
