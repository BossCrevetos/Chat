using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Backend_chat.Data;
using Backend_chat.DTOs;
using Backend_chat.Models;

namespace Backend_chat.Services
{
    public class AuthService : IAuthService
    {
        private readonly UserManager<User> _userManager;
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;

        public AuthService(
            UserManager<User> userManager,
            IConfiguration configuration,
            ApplicationDbContext context)
        {
            _userManager = userManager;
            _configuration = configuration;
            _context = context;
        }

        public async Task<AuthResponseDto> RegisterAsync(RegisterDto registerDto)
        {
            Console.WriteLine($"=== REGISTER START ===");
            Console.WriteLine($"Email: {registerDto.Email}");
            Console.WriteLine($"DisplayName: {registerDto.DisplayName}");
            Console.WriteLine($"Password length: {registerDto.Password?.Length}");

            try
            {
                Console.WriteLine($"1. Checking existing user...");
                var userExists = await _userManager.FindByEmailAsync(registerDto.Email);
                if (userExists != null)
                {
                    Console.WriteLine($"ERROR: User already exists");
                    return new AuthResponseDto { Success = false, Message = "User already exists" };
                }

                Console.WriteLine($"2. Creating user object...");
                var user = new User
                {
                    Email = registerDto.Email,
                    UserName = registerDto.Email,
                    DisplayName = registerDto.DisplayName,
                    CreatedAt = DateTime.UtcNow
                };

                Console.WriteLine($"3. Saving user to database...");
                var result = await _userManager.CreateAsync(user, registerDto.Password);

                if (!result.Succeeded)
                {
                    Console.WriteLine($"ERROR: User creation failed");
                    foreach (var error in result.Errors)
                    {
                        Console.WriteLine($"  - {error.Description}");
                    }
                    return new AuthResponseDto
                    {
                        Success = false,
                        Message = string.Join(", ", result.Errors.Select(e => e.Description))
                    };
                }
                Console.WriteLine($"SUCCESS: User created with ID: {user.Id}");

                Console.WriteLine($"4. Creating notification settings...");
                var notificationSettings = new NotificationSettings
                {
                    UserId = user.Id,
                    EnableNotifications = true,
                    EnableSound = true,
                    ShowBanner = true,
                    SmartNotifications = true
                };

                _context.NotificationSettings.Add(notificationSettings);
                await _context.SaveChangesAsync();
                Console.WriteLine($"SUCCESS: Notification settings created");

                Console.WriteLine($"5. Generating JWT token...");
                var token = GenerateJwtToken(user);
                Console.WriteLine($"SUCCESS: Token generated");

                Console.WriteLine($"=== REGISTER COMPLETE ===");
                return new AuthResponseDto
                {
                    Success = true,
                    Message = "Registration successful",
                    Token = token,
                    Expiration = DateTime.UtcNow.AddDays(7),
                    User = MapToUserDto(user)
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== CRITICAL ERROR ===");
                Console.WriteLine($"Exception: {ex.GetType().Name}");
                Console.WriteLine($"Message: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }

                return new AuthResponseDto
                {
                    Success = false,
                    Message = $"Registration failed: {ex.Message}"
                };
            }
        }

        public async Task<AuthResponseDto> LoginAsync(LoginDto loginDto)
        {
            Console.WriteLine($"=== LOGIN ATTEMPT ===");
            Console.WriteLine($"Email: {loginDto.Email}");

            try
            {
                var user = await _userManager.FindByEmailAsync(loginDto.Email);
                if (user == null)
                {
                    Console.WriteLine($"ERROR: User not found");
                    return new AuthResponseDto { Success = false, Message = "Invalid credentials" };
                }

                Console.WriteLine($"User found: {user.Id}");

                var passwordValid = await _userManager.CheckPasswordAsync(user, loginDto.Password);
                if (!passwordValid)
                {
                    Console.WriteLine($"ERROR: Invalid password");
                    return new AuthResponseDto { Success = false, Message = "Invalid credentials" };
                }

                user.LastSeen = DateTime.UtcNow;
                await _userManager.UpdateAsync(user);

                var token = GenerateJwtToken(user);
                Console.WriteLine($"SUCCESS: Login successful for {user.Email}");

                return new AuthResponseDto
                {
                    Success = true,
                    Message = "Login successful",
                    Token = token,
                    Expiration = DateTime.UtcNow.AddDays(7),
                    User = MapToUserDto(user)
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LOGIN ERROR: {ex.Message}");
                return new AuthResponseDto
                {
                    Success = false,
                    Message = $"Login failed: {ex.Message}"
                };
            }
        }

        public async Task<AuthResponseDto> ExternalLoginAsync(ExternalAuthDto externalAuthDto)
        {
            return new AuthResponseDto
            {
                Success = false,
                Message = "External login not implemented yet"
            };
        }

        public async Task<bool> ForgotPasswordAsync(string email)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null) return false;

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            return true;
        }

        public async Task<bool> ResetPasswordAsync(ResetPasswordDto resetPasswordDto)
        {
            var user = await _userManager.FindByEmailAsync(resetPasswordDto.Email);
            if (user == null) return false;

            var result = await _userManager.ResetPasswordAsync(
                user, resetPasswordDto.Token, resetPasswordDto.NewPassword);

            return result.Succeeded;
        }

        public async Task<bool> ChangePasswordAsync(string userId, ChangePasswordDto changePasswordDto)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return false;

            var result = await _userManager.ChangePasswordAsync(
                user, changePasswordDto.CurrentPassword, changePasswordDto.NewPassword);

            return result.Succeeded;
        }

        private string GenerateJwtToken(User user)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();

                var jwtKey = _configuration["Jwt:Key"];
                if (string.IsNullOrEmpty(jwtKey))
                {
                    Console.WriteLine($"WARNING: JWT Key is empty, using default");
                    jwtKey = "default_dev_key_change_in_production_12345";
                }

                var key = Encoding.ASCII.GetBytes(jwtKey);

                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.Id),
                        new Claim(ClaimTypes.Email, user.Email),
                        new Claim(ClaimTypes.Name, user.DisplayName ?? user.Email)
                    }),
                    Expires = DateTime.UtcNow.AddDays(7),
                    Issuer = _configuration["Jwt:Issuer"] ?? "BackendChat",
                    Audience = _configuration["Jwt:Audience"] ?? "BackendChatClient",
                    SigningCredentials = new SigningCredentials(
                        new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
                };

                var token = tokenHandler.CreateToken(tokenDescriptor);
                return tokenHandler.WriteToken(token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"JWT GENERATION ERROR: {ex.Message}");
                throw;
            }
        }

        private UserDto MapToUserDto(User user)
        {
            return new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                DisplayName = user.DisplayName ?? user.Email.Split('@')[0],
                AvatarUrl = user.AvatarUrl,
                Status = user.Status.ToString(),
                Bio = user.Bio,
                LastSeen = user.LastSeen
            };
        }
    }
}