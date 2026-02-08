using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using TaskManagementAPI.Constants;
using TaskManagementAPI.Data;
using TaskManagementAPI.DTOs;
using TaskManagementAPI.Models;

namespace TaskManagementAPI.Services
{
    public class AuthService : IAuthService
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthService(ApplicationDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public async Task<UserResponseDto> RegisterAsync(RegisterDto registerDto)
        {
           return await RegisterWithRoleAsync(registerDto, RoleConstants.User);  // Default to "User" role
        }

        public async Task<LoginResponseDto> LoginAsync(LoginDto loginDto) 
        {
            // Find user by email
            var user = await _context.Users
                .Include(u => u.Role)  // Eager load the role
                .FirstOrDefaultAsync(u => u.Email == loginDto.Email);

            // Check if user exists
            if (user == null)
            {
                throw new UnauthorizedAccessException("Invalid email or password");
            }

            // Verify password
            bool isPasswordValid = BCrypt.Net.BCrypt.Verify(loginDto.Password, user.PasswordHash);

            if (!isPasswordValid)
            {
                throw new UnauthorizedAccessException("Invalid email or password");
            }

            // Generate JWT token
            var token = GenerateJwtToken(user);
            var expiresAt = DateTime.UtcNow.AddMinutes(double.Parse(_configuration["JwtSettings:ExpiryMinutes"]!));

            // Return login response
            return new LoginResponseDto
            {
                Token = token,
                User = new UserResponseDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Email = user.Email,
                    CreatedAt = user.CreatedAt,
                    Role = user.Role.Name  // Include role
                },
                ExpiresAt = expiresAt
            };
        }

        public async Task<UserResponseDto> RegisterWithRoleAsync(RegisterDto registerDto, string roleName) 
        {
            // Check if user already exists
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == registerDto.Email ||
                                         u.Username == registerDto.Username);

            if (existingUser != null)
            {
                throw new InvalidOperationException("User with this email or username already exists");
            }

            // Get the role
            var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == registerDto.Role);
            if (role == null) 
            {
                // Default to User role if specified role doesn't exist
                role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == RoleConstants.User);
                if (role == null)
                {
                    throw new InvalidOperationException("Default user role not found. Please ensure roles are seeded.");
                }
            }

            // Hash the password
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(registerDto.Password);

            // Create new user
            var user = new User
            {
                Username = registerDto.Username,
                Email = registerDto.Email,
                PasswordHash = passwordHash,
                RoleId = role.Id,  // Assign role
                CreatedAt = DateTime.UtcNow
            };

            // Add to database
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Load the role navigation property
            await _context.Entry(user).Reference(u => u.Role).LoadAsync();

            // Return response DTO
            return new UserResponseDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                CreatedAt = user.CreatedAt,
                Role = user.Role.Name  // Include role name
            };
        }


        private string GenerateJwtToken(User user) 
        {
            // Get JWT settings from configuration
            var secretKey = _configuration["JwtSettings:SecretKey"];
            var issuer = _configuration["JwtSettings:Issuer"];
            var audience = _configuration["JwtSettings:Audience"];
            var expiryMinutes = double.Parse(_configuration["JwtSettings:ExpiryMinutes"]!);

            // Create security key
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey!));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            // Define claims (user information to include in token)
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
                new Claim(ClaimTypes.Role, user.Role.Name),  // Add role claim
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            // Create token
            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
                signingCredentials: credentials
            );

            // Return token as string
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
