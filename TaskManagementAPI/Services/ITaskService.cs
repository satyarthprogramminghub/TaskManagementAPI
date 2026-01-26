using TaskManagementAPI.DTOs;

namespace TaskManagementAPI.Services
{
    public interface ITaskService
    {
        Task<TaskResponseDto> CreateTaskAsync(CreateTaskDto createTaskDto, int userId);
        Task<List<TaskResponseDto>> GetUserTasksAsync(int userId);
        Task<TaskResponseDto?> GetTaskByIdAsync(int taskId, int userId);
    }
}
