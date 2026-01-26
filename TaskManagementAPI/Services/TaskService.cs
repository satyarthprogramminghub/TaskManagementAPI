using Microsoft.EntityFrameworkCore;
using TaskManagementAPI.Data;
using TaskManagementAPI.DTOs;
using TaskManagementAPI.Models;

namespace TaskManagementAPI.Services
{
    public class TaskService : ITaskService
    {
        private readonly ApplicationDbContext _context;
        public TaskService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<TaskResponseDto> CreateTaskAsync(CreateTaskDto createTaskDto, int userId) 
        {
            // Create new task entity
            var task = new TaskItem
            {
                Title = createTaskDto.Title,
                Description = createTaskDto.Description,
                Priority = createTaskDto.Priority,
                DueDate = createTaskDto.DueDate,
                IsCompleted = false,
                CreatedAt = DateTime.UtcNow,
                UserId = userId // Associate with current user
            };

            // Add to database
            _context.Tasks.Add(task);
            await _context.SaveChangesAsync();

            // Return response DTO
            return new TaskResponseDto
            {
                Id = task.Id,
                Title = task.Title,
                Description = task.Description,
                IsCompleted = task.IsCompleted,
                Priority = task.Priority,
                DueDate = task.DueDate,
                CreatedAt = task.CreatedAt,
                UserId = task.UserId
            };

        }

        public async Task<List<TaskResponseDto>> GetUserTasksAsync(int userId) 
        {
            // Get all tasks for this user, ordered by creation date (newest first)
            var tasks = await _context.Tasks
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new TaskResponseDto
                {
                    Id = t.Id,
                    Title = t.Title,
                    Description = t.Description,
                    IsCompleted = t.IsCompleted,
                    Priority = t.Priority,
                    DueDate = t.DueDate,
                    CreatedAt = t.CreatedAt,
                    UserId = t.UserId
                })
                .ToListAsync();

            return tasks;
        }

        public async Task<TaskResponseDto?> GetTaskByIdAsync(int taskId, int userId) 
        {
            // Find task by ID and ensure it belongs to the user
            var task = await _context.Tasks
                .Where(t => t.Id == taskId && t.UserId == userId)
                .Select(t => new TaskResponseDto
                {
                    Id = t.Id,
                    Title = t.Title,
                    Description = t.Description,
                    IsCompleted = t.IsCompleted,
                    Priority = t.Priority,
                    DueDate = t.DueDate,
                    CreatedAt = t.CreatedAt,
                    UserId = t.UserId
                })
                .FirstOrDefaultAsync();

            return task;
        }

    }
}
