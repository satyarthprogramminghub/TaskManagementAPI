namespace TaskManagementAPI.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Role relationship
        public int RoleId { get; set; }  // Foreign key
        public Role Role { get; set; } = null!;  // Navigation property

        // Navigation property
        public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
        public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    }
}
