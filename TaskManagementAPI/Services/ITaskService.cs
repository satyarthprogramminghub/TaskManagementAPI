using TaskManagementAPI.DTOs;

namespace TaskManagementAPI.Services
{
    public interface ITaskService
    {
        Task<TaskResponseDto> CreateTaskAsync(CreateTaskDto createTaskDto, int userId);
        Task<List<TaskResponseDto>> GetUserTasksAsync(int userId);
        Task<TaskResponseDto?> GetTaskByIdAsync(int taskId, int userId);
        Task<TaskResponseDto?> UpdateTaskAsync(int taskId, UpdateTaskDto updateTaskDto, int userId);
        Task<bool> DeleteTaskAsync(int taskId, int userId);
        Task<TaskResponseDto?> ToggleTaskCompletionAsync(int taskId, int userId);

        // New admin/manager methods
        Task<List<TaskResponseDto>> GetAllTasksAsync();  // For managers and admins
        Task<TaskResponseDto?> GetAnyTaskByIdAsync(int taskId);  // Get any task regardless of owner
        Task<TaskResponseDto?> UpdateAnyTaskAsync(int taskId, UpdateTaskDto updateTaskDto);  // Update any task
        Task<bool> DeleteAnyTaskAsync(int taskId);  // Delete any task
    }
}
