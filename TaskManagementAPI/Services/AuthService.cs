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
using TaskManagementAPI.Repositories;

namespace TaskManagementAPI.Services
{
    public class AuthService : IAuthService
    {
        // Repositories replace direct DbContext usage
        // UserRepository    → all user-related DB operations
        // RefreshTokenRepository → all token-related DB operations
        // _context kept only for role lookup (no RoleRepository in this implementation)

        private readonly IUserRepository _userRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthService(
            IUserRepository userRepository,
            IRefreshTokenRepository refreshTokenRepository,
            ApplicationDbContext context,
            IConfiguration configuration)
        {
            _userRepository = userRepository;
            _refreshTokenRepository = refreshTokenRepository;
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
            // UserRepository.GetByEmailAsync()
            // ↑ Uses base.FirstOrDefaultAsync() with Role eagerly loaded
            var user = await _userRepository.GetByEmailAsync(loginDto.Email);

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

            // Uses base generic AddAsync() from Repository<RefreshToken>
            await _refreshTokenRepository.AddAsync(refreshToken);

            // Remove old/inactive refresh tokens for this user (optional cleanup)
            // RefreshTokenRepository.RemoveOldTokensAsync()
            // ↑ Uses base.RemoveRange() internally for cleanup
            await _refreshTokenRepository.RemoveOldTokensAsync(user.Id);

            // Uses base generic SaveChangesAsync() from Repository<RefreshToken>
            await _refreshTokenRepository.SaveChangesAsync();

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
            // UserRepository.EmailExistsAsync()
            // ↑ Uses base.AnyAsync() internally
            if (await _userRepository.EmailExistsAsync(registerDto.Email))
            {
                throw new InvalidOperationException(
                    "User with this email already exists");
            }

            // UserRepository.UsernameExistsAsync()
            // ↑ Uses base.AnyAsync() internally
            if (await _userRepository.UsernameExistsAsync(registerDto.Username))
            {
                throw new InvalidOperationException(
                    "User with this username already exists");
            }



            // Role lookup still uses _context directly
            // We don't have a RoleRepository - it's a simple lookup
            var role = await _context.Roles
                .FirstOrDefaultAsync(r => r.Name == roleName)
                ?? await _context.Roles
                    .FirstOrDefaultAsync(r => r.Name == RoleConstants.User)
                ?? throw new InvalidOperationException(
                    "Default user role not found. Please ensure roles are seeded.");

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

            // Uses base generic AddAsync() from Repository<User>
            await _userRepository.AddAsync(user);

            // Uses base generic SaveChangesAsync() from Repository<User>
            await _userRepository.SaveChangesAsync();



            // Load the role navigation property
            // UserRepository.GetUserWithRoleAsync()
            // ↑ Uses base.FirstOrDefaultAsync() with Role included
            var userWithRole = await _userRepository.GetUserWithRoleAsync(user.Id);

            // Return response DTO
            return new UserResponseDto
            {
                Id = userWithRole!.Id,
                Username = userWithRole.Username,
                Email = userWithRole.Email,
                CreatedAt = userWithRole.CreatedAt,
                Role = userWithRole.Role.Name  // Include role name
            };
        }

        public async Task<RefreshTokenResponseDto> RefreshTokenAsync(string token, string ipAddress) 
        {
            // UserRepository.GetUserWithRefreshTokensAsync()
            // ↑ Custom query - needs User + Role + RefreshTokens loaded
            var user = await _userRepository.GetUserWithRefreshTokensAsync(token);

            if (user == null)
            {
                throw new UnauthorizedAccessException("Invalid refresh token");
            }

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

            // Uses base generic Update() from Repository<RefreshToken>
            _refreshTokenRepository.Update(refreshToken);

            // Save new refresh token
            // Uses base generic AddAsync() from Repository<RefreshToken>
            await _refreshTokenRepository.AddAsync(newRefreshToken);

            // Remove old refresh tokens
            // RefreshTokenRepository.RemoveOldTokensAsync()
            // ↑ Uses base.RemoveRange() internally for cleanup
            await _refreshTokenRepository.RemoveOldTokensAsync(user.Id);

            // Uses base generic SaveChangesAsync() from Repository<RefreshToken>
            await _refreshTokenRepository.SaveChangesAsync();

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
            // UserRepository.GetUserWithRefreshTokensAsync()
            // ↑ Custom query - needs User + RefreshTokens loaded
            var user = await _userRepository.GetUserWithRefreshTokensAsync(token);

            if (user == null)
            {
                return false;
            }

            var refreshToken = user.RefreshTokens.Single(rt => rt.Token == token);

            if (!refreshToken.IsActive)
            {
                return false;
            }

            // Revoke token
            refreshToken.RevokedAt = DateTime.UtcNow;
            refreshToken.RevokedByIp = ipAddress;

            // Uses base generic Update() from Repository<RefreshToken>
            _refreshTokenRepository.Update(refreshToken);


            // Uses base generic SaveChangesAsync() from Repository<RefreshToken>
            await _refreshTokenRepository.SaveChangesAsync();

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
