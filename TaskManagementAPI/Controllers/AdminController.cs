using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskManagementAPI.Constants;
using TaskManagementAPI.Data;
using TaskManagementAPI.DTOs;
using TaskManagementAPI.Services;

namespace TaskManagementAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "AdminOnly")]  // Entire controller requires Admin role
    public class AdminController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuthService _authService;

        public AdminController(ApplicationDbContext context, IAuthService authService)
        {
            _context = context;
            _authService = authService;
        }

        // GET: api/admin/users - Get all users
        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers() 
        {
            try
            {
                var users = await _context.Users
                    .Include(u => u.Role)
                    .Select(u => new UserResponseDto
                    {
                        Id = u.Id,
                        Username = u.Username,
                        Email = u.Email,
                        CreatedAt = u.CreatedAt,
                        Role = u.Role.Name
                    })
                    .ToListAsync();

                return Ok(users);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while retrieving users", details = ex.Message });
            }
        }

        // GET: api/admin/users/{id} - Get specific user
        [HttpGet("users/{id}")]
        public async Task<IActionResult> GetUserById(int id) 
        {
            try
            {
                var user = await _context.Users
                    .Include(u => u.Role)
                    .Include(u => u.Tasks)
                    .Where(u => u.Id == id)
                    .Select(u => new
                    {
                        u.Id,
                        u.Username,
                        u.Email,
                        u.CreatedAt,
                        Role = u.Role.Name,
                        TaskCount = u.Tasks.Count
                    })
                    .FirstOrDefaultAsync();

                if (user == null)
                {
                    return NotFound(new { message = "User not found" });
                }

                return Ok(user);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while retrieving the user", details = ex.Message });
            }
        }

        // POST: api/admin/users - Create user with specific role
        [HttpPost("users")]
        public async Task<IActionResult> CreateUserWithRole([FromBody] RegisterDto registerDto) 
        {
            try
            {
                // Use the role provided, or default to User
                var roleName = string.IsNullOrWhiteSpace(registerDto.Role)
                    ? RoleConstants.User
                    : registerDto.Role;

                var user = await _authService.RegisterWithRoleAsync(registerDto, roleName);

                return CreatedAtAction(nameof(GetUserById), new { id = user.Id }, user);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while creating the user", details = ex.Message });
            }
        }

        // PUT: api/admin/users/{id}/role - Change user's role
        [HttpPut("users/{id}/role")]
        public async Task<IActionResult> ChangeUserRole(int id, [FromBody] ChangeRoleDto changeRoleDto) 
        {
            try
            {
                var user = await _context.Users
                    .Include(u => u.Role)
                    .FirstOrDefaultAsync(u => u.Id == id);

                if (user == null)
                {
                    return NotFound(new { message = "User not found" });
                }

                var newRole = await _context.Roles
                    .FirstOrDefaultAsync(r => r.Name == changeRoleDto.RoleName);

                if (newRole == null)
                {
                    return BadRequest(new { message = $"Role '{changeRoleDto.RoleName}' does not exist" });
                }

                user.RoleId = newRole.Id;
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = $"User '{user.Username}' role changed to '{newRole.Name}'",
                    user = new UserResponseDto
                    {
                        Id = user.Id,
                        Username = user.Username,
                        Email = user.Email,
                        CreatedAt = user.CreatedAt,
                        Role = newRole.Name
                    }
                });

            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while changing user role", details = ex.Message });
            }
        }

        // DELETE: api/admin/users/{id} - Delete user
        [HttpDelete("users/{id}")]
        public async Task<IActionResult> DeleteUser(int id) 
        {
            try
            {
                var user = await _context.Users
                           .Include(u => u.Tasks)
                           .FirstOrDefaultAsync(u => u.Id == id);

                if (user == null)
                {
                    return NotFound(new { message = "User not found" });
                }

                // Delete all user's tasks first (cascade should handle this, but being explicit)
                _context.Tasks.RemoveRange(user.Tasks);
                _context.Users.Remove(user);

                await _context.SaveChangesAsync();

                return NoContent();


            }
            catch (Exception ex)
            {

                return StatusCode(500, new { message = "An error occurred while deleting the user", details = ex.Message });
            }
        }

        // GET: api/admin/stats - Get system statistics
        [HttpGet("stats")]
        public async Task<IActionResult> GetSystemStats() 
        {

            try
            {
                var totalUsers = await _context.Users.CountAsync();
                var totalTasks = await _context.Tasks.CountAsync();
                var completedTasks = await _context.Tasks.CountAsync(t => t.IsCompleted);
                var pendingTasks = totalTasks - completedTasks;
                var usersByRole = await _context.Users
                                        .Include(u => u.Role)
                                        .GroupBy(u => u.Role.Name)
                                        .Select(g => new { Role = g.Key, Count = g.Count() })
                                        .ToListAsync();

                return Ok(new
                {
                    totalUsers,
                    totalTasks,
                    completedTasks,
                    pendingTasks,
                    usersByRole
                });

            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while retrieving stats", details = ex.Message });
            }
        
        }


    }
}
