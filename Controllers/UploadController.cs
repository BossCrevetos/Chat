using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Backend_chat.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class UploadController : ControllerBase
    {
        private readonly string _uploadPath;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<UploadController> _logger;

        public UploadController(IWebHostEnvironment env, ILogger<UploadController> logger)
        {
            _env = env;
            _logger = logger;
            _uploadPath = Path.Combine(_env.WebRootPath, "uploads");

            // Создаем папку если не существует
            if (!Directory.Exists(_uploadPath))
            {
                Directory.CreateDirectory(_uploadPath);
            }
        }

        [HttpPost("file")]
        public async Task<IActionResult> UploadFile(IFormFile file)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized();

                if (file == null || file.Length == 0)
                    return BadRequest("Файл не выбран");

                // Проверка размера файла (макс 10MB)
                if (file.Length > 10 * 1024 * 1024)
                    return BadRequest("Файл слишком большой (максимум 10 МБ)");

                // Проверка типа файла
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".pdf", ".doc", ".docx", ".txt", ".mp3", ".mp4" };
                var extension = Path.GetExtension(file.FileName).ToLower();

                if (!allowedExtensions.Contains(extension))
                    return BadRequest($"Недопустимый тип файла. Разрешены: {string.Join(", ", allowedExtensions)}");

                // Генерируем уникальное имя файла
                var fileName = $"{Guid.NewGuid()}{extension}";
                var filePath = Path.Combine(_uploadPath, fileName);

                // Сохраняем файл
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                _logger.LogInformation($"Файл загружен: {file.FileName} -> {fileName} пользователем {userId}");

                return Ok(new
                {
                    FileName = file.FileName,
                    FileUrl = $"/uploads/{fileName}",
                    FileSize = file.Length,
                    ContentType = file.ContentType,
                    Extension = extension
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка загрузки файла");
                return StatusCode(500, "Ошибка загрузки файла");
            }
        }

        [HttpPost("avatar")]
        public async Task<IActionResult> UploadAvatar(IFormFile file)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized();

                if (file == null || file.Length == 0)
                    return BadRequest("Файл не выбран");

                // Проверка размера (макс 5MB для аватара)
                if (file.Length > 5 * 1024 * 1024)
                    return BadRequest("Аватар слишком большой (максимум 5 МБ)");

                // Проверка типа файла (только изображения)
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
                var extension = Path.GetExtension(file.FileName).ToLower();

                if (!allowedExtensions.Contains(extension))
                    return BadRequest($"Недопустимый тип файла для аватара. Разрешены: {string.Join(", ", allowedExtensions)}");

                // Генерируем имя файла
                var fileName = $"avatar_{userId}{extension}";
                var filePath = Path.Combine(_uploadPath, fileName);

                // Удаляем старый аватар если есть
                var oldFiles = Directory.GetFiles(_uploadPath, $"avatar_{userId}.*");
                foreach (var oldFile in oldFiles)
                {
                    System.IO.File.Delete(oldFile);
                }

                // Сохраняем файл
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var avatarUrl = $"/uploads/{fileName}";

                // Обновляем URL аватара в профиле пользователя
                // (Нужно будет добавить метод в ProfileController)

                _logger.LogInformation($"Аватар загружен для пользователя {userId}: {avatarUrl}");

                return Ok(new
                {
                    AvatarUrl = avatarUrl
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка загрузки аватара");
                return StatusCode(500, "Ошибка загрузки аватара");
            }
        }

        [HttpGet("files")]
        public IActionResult GetUserFiles()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            // Возвращаем список файлов пользователя
            // (В реальном приложении нужно хранить информацию о файлах в БД)
            return Ok(new { Message = "Функционал списка файлов будет реализован" });
        }
    }
}