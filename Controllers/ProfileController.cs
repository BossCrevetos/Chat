using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Backend_chat.Services;
using Backend_chat.DTOs;
using Backend_chat.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Backend_chat.Models;
using Microsoft.AspNetCore.Identity;

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
        private readonly IChatService _chatService;
        private readonly UserManager<User> _userManager;

        public ProfileController(
    ApplicationDbContext context,
    IWebHostEnvironment env,
    ILogger<ProfileController> logger,
    IChatService chatService,  // ← ИЗМЕНИТЕ НА ИНТЕРФЕЙС
    UserManager<User> userManager)
        {
            _context = context;
            _env = env;
            _logger = logger;
            _chatService = chatService;
            _userManager = userManager;
        }

        [HttpGet]
        public async Task<IActionResult> GetProfile()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized();

                var user = await _context.Users
                    .Include(u => u.NotificationSettings)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                    return NotFound(new { message = "User not found" });

                // Создаем настройки уведомлений, если их нет
                if (user.NotificationSettings == null)
                {
                    user.NotificationSettings = new NotificationSettings
                    {
                        UserId = userId,
                        EnableNotifications = true,
                        EnableSound = true,
                        ShowBanner = true,
                        SmartNotifications = true
                    };

                    await _context.SaveChangesAsync();
                }

                var userDto = new UserDto
                {
                    Id = user.Id,
                    Email = user.Email,
                    DisplayName = user.DisplayName ?? user.Email?.Split('@')[0] ?? "Пользователь",
                    AvatarUrl = user.AvatarUrl,
                    Status = user.Status.ToString(),
                    Bio = user.Bio,
                    LastSeen = user.LastSeen
                };

                return Ok(userDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения профиля");
                return StatusCode(500, new { message = "Internal server error" });
            }
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

            if (Enum.TryParse<UserStatus>(updateDto.Status, out var status))
            {
                user.Status = status;
            }

            await _context.SaveChangesAsync();

            return Ok(new { Success = true });
        }

        [HttpGet("notifications")]
        public async Task<IActionResult> GetNotificationSettings()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            try
            {
                var settings = await _context.NotificationSettings
                    .FirstOrDefaultAsync(ns => ns.UserId == userId);

                if (settings == null)
                {
                    // Создаем настройки по умолчанию, если их нет
                    settings = new NotificationSettings
                    {
                        UserId = userId,
                        EnableNotifications = true,
                        EnableSound = true,
                        ShowBanner = true,
                        SmartNotifications = true
                    };

                    _context.NotificationSettings.Add(settings);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"Созданы настройки уведомлений по умолчанию для пользователя {userId}");
                }

                var settingsDto = new NotificationSettingsDto
                {
                    EnableNotifications = settings.EnableNotifications,
                    EnableSound = settings.EnableSound,
                    ShowBanner = settings.ShowBanner,
                    SmartNotifications = settings.SmartNotifications
                };

                return Ok(settingsDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения настроек уведомлений");
                return StatusCode(500, "Internal server error");
            }
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

        // Новый метод для обновления статуса
        [HttpPut("status")]
        public async Task<IActionResult> UpdateStatus([FromBody] UpdateStatusDto dto)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized();

                _logger.LogInformation($"User {userId} updating status to {dto.Status}");

                if (!Enum.TryParse<UserStatus>(dto.Status, out var status))
                    return BadRequest("Invalid status value");

                var user = await _userManager.FindByIdAsync(userId);
                if (user == null)
                    return NotFound("User not found");

                user.Status = status;
                user.LastSeen = DateTime.UtcNow;

                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                    return BadRequest(result.Errors);

                return Ok(new
                {
                    success = true,
                    message = "Status updated successfully",
                    status = dto.Status
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating status");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // Новый метод для получения статусов пользователей
        [HttpGet("statuses")]
        public async Task<IActionResult> GetUserStatuses([FromQuery] List<string> userIds)
        {
            try
            {
                var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(currentUserId))
                    return Unauthorized();

                if (userIds == null || userIds.Count == 0)
                    return Ok(new Dictionary<string, string>());

                // Фильтруем null/empty значения
                var validUserIds = userIds.Where(id => !string.IsNullOrEmpty(id)).ToList();

                if (validUserIds.Count == 0)
                    return Ok(new Dictionary<string, string>());

                // Просто получаем статусы из БД напрямую
                var users = await _context.Users
                    .Where(u => validUserIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.Status })
                    .ToListAsync();

                var result = new Dictionary<string, string>();

                foreach (var userId in validUserIds)
                {
                    var user = users.FirstOrDefault(u => u.Id == userId);
                    result[userId] = user?.Status.ToString() ?? "Offline";
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user statuses");
                return Ok(new Dictionary<string, string>());
            }
        }

        public class UpdateStatusDto
        {
            public string Status { get; set; } = string.Empty;
        }

        public class UpdateProfileDto
        {
            public string? DisplayName { get; set; }
            public string? Bio { get; set; }
            public string? AvatarUrl { get; set; }
            public string Status { get; set; } = "Online";
        }

        public class NotificationSettingsDto
        {
            public bool EnableNotifications { get; set; }
            public bool EnableSound { get; set; }
            public bool ShowBanner { get; set; }
            public bool SmartNotifications { get; set; }
        }
    } // ← ЗДЕСЬ ДОБАВЬТЕ ЗАКРЫВАЮЩУЮ СКОБКУ ДЛЯ КЛАССА
}