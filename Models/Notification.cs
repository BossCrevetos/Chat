using System.ComponentModel.DataAnnotations;

namespace Backend_chat.Models
{
    public class Notification
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Message { get; set; } = string.Empty;

        [MaxLength(50)]
        public string NotificationType { get; set; } = "message"; // message, system, friend_request

        public string? Data { get; set; } // JSON с дополнительными данными

        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Навигационное свойство
        public virtual User User { get; set; }
    }
}