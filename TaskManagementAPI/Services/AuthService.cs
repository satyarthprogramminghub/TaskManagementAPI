using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
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

        public async Task<LoginResponseDto> LoginAsync(LoginDto loginDto,string ipAddress) 
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

            // Generate tokens
            var accessToken = GenerateAccessToken(user);
            var refreshToken = GenerateRefreshToken(ipAddress);

            // Save refresh token to database
            refreshToken.UserId = user.Id;
            _context.RefreshTokens.Add(refreshToken);

            // Remove old/inactive refresh tokens for this user (optional cleanup)
            await RemoveOldRefreshTokens(user.Id);

            await _context.SaveChangesAsync();

            var accessTokenExpiry = DateTime.UtcNow.AddMinutes(
                double.Parse(_configuration["JwtSettings:AccessTokenExpiryMinutes"]!));


            return new LoginResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken.Token,
                User = new UserResponseDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Email = user.Email,
                    CreatedAt = user.CreatedAt,
                    Role = user.Role.Name
                },
                AccessTokenExpiresAt = accessTokenExpiry,
                RefreshTokenExpiresAt = refreshToken.ExpiresAt
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

        public async Task<RefreshTokenResponseDto> RefreshTokenAsync(string token, string ipAddress) 
        {
            var user = await GetUserByRefreshToken(token);
            var refreshToken = user.RefreshTokens.Single(rt => rt.Token == token);

            if (!refreshToken.IsActive)
            {
                throw new UnauthorizedAccessException("Invalid or expired refresh token");
            }

            // Generate new tokens
            var newAccessToken = GenerateAccessToken(user);
            var newRefreshToken = GenerateRefreshToken(ipAddress);
            newRefreshToken.UserId = user.Id;

            // Mark old refresh token as replaced
            refreshToken.RevokedAt = DateTime.UtcNow;
            refreshToken.RevokedByIp = ipAddress;
            refreshToken.ReplacedByToken = newRefreshToken.Token;

            // Save new refresh token
            _context.RefreshTokens.Add(newRefreshToken);

            // Remove old refresh tokens
            await RemoveOldRefreshTokens(user.Id);

            await _context.SaveChangesAsync();

            var accessTokenExpiry = DateTime.UtcNow.AddMinutes(
                double.Parse(_configuration["JwtSettings:AccessTokenExpiryMinutes"]!));

            return new RefreshTokenResponseDto
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken.Token,
                AccessTokenExpiresAt = accessTokenExpiry,
                RefreshTokenExpiresAt = newRefreshToken.ExpiresAt
            };
        }

        public async Task<bool> RevokeTokenAsync(string token, string ipAddress) 
        {
            var user = await GetUserByRefreshToken(token);
            var refreshToken = user.RefreshTokens.Single(rt => rt.Token == token);

            if (!refreshToken.IsActive)
            {
                return false;
            }

            // Revoke token
            refreshToken.RevokedAt = DateTime.UtcNow;
            refreshToken.RevokedByIp = ipAddress;

            await _context.SaveChangesAsync();

            return true;
        }


        private string GenerateAccessToken(User user) 
        {
            var secretKey = _configuration["JwtSettings:SecretKey"];
            var issuer = _configuration["JwtSettings:Issuer"];
            var audience = _configuration["JwtSettings:Audience"];
            var expiryMinutes = double.Parse(_configuration["JwtSettings:AccessTokenExpiryMinutes"]!);

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey!));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
                new Claim(ClaimTypes.Role, user.Role.Name),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private RefreshToken GenerateRefreshToken(string ipAddress) 
        {
            var refreshTokenExpiry = double.Parse(_configuration["JwtSettings:RefreshTokenExpiryDays"]!);

            // Generate cryptographically secure random token
            var randomBytes = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);
            var token = Convert.ToBase64String(randomBytes);

            return new RefreshToken
            {
                Token = token,
                ExpiresAt = DateTime.UtcNow.AddDays(refreshTokenExpiry),
                CreatedAt = DateTime.UtcNow,
                CreatedByIp = ipAddress
            };
        }

        private async Task RemoveOldRefreshTokens(int userId) 
        {
            // Remove refresh tokens that are expired or revoked and older than 30 days
            var tokensToRemove = await _context.RefreshTokens
                .Where(rt => rt.UserId == userId &&
                            //!rt.IsActive &&
                            rt.RevokedAt != null &&
                            rt.CreatedAt.AddDays(30) < DateTime.UtcNow)
                .ToListAsync();

            if (tokensToRemove.Any())
            {
                _context.RefreshTokens.RemoveRange(tokensToRemove);
            }

        }

        private async Task<User> GetUserByRefreshToken(string token) 
        {
            var user = await _context.Users
                    .Include(u => u.Role)
                    .Include(u => u.RefreshTokens)
                    .SingleOrDefaultAsync(u => u.RefreshTokens.Any(rt => rt.Token == token));

            if (user == null)
            {
                throw new UnauthorizedAccessException("Invalid refresh token");
            }

            return user;
        }
    }
}
