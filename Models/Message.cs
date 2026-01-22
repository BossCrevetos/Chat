using System.ComponentModel.DataAnnotations;

namespace Backend_chat.Models
{
    public class Message
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(2000)]
        public string Content { get; set; } = string.Empty;

        public MessageType MessageType { get; set; } = MessageType.Text;  // MessageType из того же пространства имен

        [MaxLength(500)]
        public string? FileUrl { get; set; }

        [MaxLength(200)]
        public string? FileName { get; set; }

        public long? FileSize { get; set; }
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public DateTime? DeliveredAt { get; set; }
        public DateTime? ReadAt { get; set; }
        public DateTime? EditedAt { get; set; }
        public bool IsEdited { get; set; }
        public bool IsDeleted { get; set; }

        // Внешние ключи
        public string SenderId { get; set; } = string.Empty;
        public int ChatId { get; set; }

        // Навигационные свойства
        public virtual User Sender { get; set; }
        public virtual Chat Chat { get; set; }
    }
}