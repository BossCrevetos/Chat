using Backend_chat.DTOs;

namespace Backend_chat.Services
{
    public interface IAuthService
    {
        Task<AuthResponseDto> RegisterAsync(AuthDto registerDto);
        Task<AuthResponseDto> LoginAsync(LoginDto loginDto);
        Task<AuthResponseDto> ExternalLoginAsync(ExternalAuthDto externalAuthDto);
        Task<bool> ForgotPasswordAsync(string email);
        Task<bool> ResetPasswordAsync(ResetPasswordDto resetPasswordDto);
        Task<bool> ChangePasswordAsync(string userId, ChangePasswordDto changePasswordDto);
    }
}