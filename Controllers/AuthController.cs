using Microsoft.AspNetCore.Mvc;
using Backend_chat.Services;
using Backend_chat.DTOs;

namespace Backend_chat.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto model)
        {
            Console.WriteLine($"=== REGISTER REQUEST ===");
            Console.WriteLine($"Email: {model.Email}");
            Console.WriteLine($"DisplayName: {model.DisplayName}");
            Console.WriteLine($"Password length: {model.Password?.Length}");
            Console.WriteLine($"=== END ===");

            var result = await _authService.RegisterAsync(model);
            if (!result.Success)
                return BadRequest(new { result.Message });

            return Ok(result);
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto model)
        {
            var result = await _authService.LoginAsync(model);
            if (!result.Success)
                return Unauthorized(new { result.Message });

            return Ok(result);
        }

        [HttpPost("external-login")]
        public async Task<IActionResult> ExternalLogin([FromBody] ExternalAuthDto model)
        {
            var result = await _authService.ExternalLoginAsync(model);
            if (!result.Success)
                return Unauthorized(new { result.Message });

            return Ok(result);
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto model)
        {
            var success = await _authService.ForgotPasswordAsync(model.Email);
            return Ok(new { Success = success });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto model)
        {
            var success = await _authService.ResetPasswordAsync(model);
            return Ok(new { Success = success });
        }
    }
}