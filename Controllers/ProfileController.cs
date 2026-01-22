using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Backend_chat.Services;
using Backend_chat.DTOs;
using Backend_chat.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Backend_chat.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ProfileController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<ProfileController> _logger;

        public ProfileController(
            ApplicationDbContext context,
            IWebHostEnvironment env,
            ILogger<ProfileController> logger)
        {
            _context = context;
            _env = env;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetProfile()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var user = await _context.Users
                .Include(u => u.NotificationSettings)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return NotFound();

            var userDto = new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                DisplayName = user.DisplayName ?? user.Email.Split('@')[0],
                AvatarUrl = user.AvatarUrl,
                Status = user.Status.ToString(),
                Bio = user.Bio,
                LastSeen = user.LastSeen
            };

            return Ok(userDto);
        }

        [HttpPut]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto updateDto)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return NotFound();

            user.DisplayName = updateDto.DisplayName ?? user.DisplayName;
            user.Bio = updateDto.Bio ?? user.Bio;
            user.AvatarUrl = updateDto.AvatarUrl ?? user.AvatarUrl;
            user.Status = updateDto.Status;

            await _context.SaveChangesAsync();

            return Ok(new { Success = true });
        }

        [HttpGet("notifications")]
        public async Task<IActionResult> GetNotificationSettings()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var settings = await _context.NotificationSettings
                .FirstOrDefaultAsync(ns => ns.UserId == userId);

            if (settings == null)
                return NotFound();

            var settingsDto = new NotificationSettingsDto
            {
                EnableNotifications = settings.EnableNotifications,
                EnableSound = settings.EnableSound,
                ShowBanner = settings.ShowBanner,
                SmartNotifications = settings.SmartNotifications
            };

            return Ok(settingsDto);
        }

        [HttpPut("notifications")]
        public async Task<IActionResult> UpdateNotificationSettings([FromBody] NotificationSettingsDto settingsDto)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var settings = await _context.NotificationSettings
                .FirstOrDefaultAsync(ns => ns.UserId == userId);

            if (settings == null)
                return NotFound();

            settings.EnableNotifications = settingsDto.EnableNotifications;
            settings.EnableSound = settingsDto.EnableSound;
            settings.ShowBanner = settingsDto.ShowBanner;
            settings.SmartNotifications = settingsDto.SmartNotifications;

            await _context.SaveChangesAsync();

            return Ok(new { Success = true });
        }

        [HttpPost("avatar")]
        public async Task<IActionResult> UploadAvatar(IFormFile file)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            if (file == null || file.Length == 0)
                return BadRequest("Файл не выбран");

            if (file.Length > 5 * 1024 * 1024)
                return BadRequest("Аватар слишком большой (максимум 5 МБ)");

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
            var extension = Path.GetExtension(file.FileName).ToLower();

            if (!allowedExtensions.Contains(extension))
                return BadRequest($"Недопустимый тип файла для аватара. Разрешены: {string.Join(", ", allowedExtensions)}");

            try
            {
                var uploadsPath = Path.Combine(_env.WebRootPath, "uploads", "avatars");
                if (!Directory.Exists(uploadsPath))
                    Directory.CreateDirectory(uploadsPath);

                var fileName = $"avatar_{userId}{extension}";
                var filePath = Path.Combine(uploadsPath, fileName);

                // Удаляем старый аватар
                var oldFiles = Directory.GetFiles(uploadsPath, $"avatar_{userId}.*");
                foreach (var oldFile in oldFiles)
                {
                    System.IO.File.Delete(oldFile);
                }

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var avatarUrl = $"/uploads/avatars/{fileName}";

                // Обновляем аватар пользователя
                var user = await _context.Users.FindAsync(userId);
                if (user != null)
                {
                    user.AvatarUrl = avatarUrl;
                    await _context.SaveChangesAsync();
                }

                return Ok(new { avatarUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка загрузки аватара");
                return StatusCode(500, "Ошибка загрузки аватара");
            }
        }
    }
}