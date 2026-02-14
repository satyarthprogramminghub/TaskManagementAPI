using System.ComponentModel.DataAnnotations;

namespace TaskManagementAPI.DTOs
{
    public class RefreshTokenRequestDto
    {
        [Required(ErrorMessage = "Refresh token is required")]
        public string RefreshToken { get; set; } = string.Empty;
    }
}
