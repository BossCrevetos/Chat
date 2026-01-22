using System.ComponentModel.DataAnnotations;

namespace Backend_chat.Models
{
    public class Chat
    {
        public int Id { get; set; }

        [MaxLength(20)]
        public string ChatType { get; set; } = "private";

        [MaxLength(100)]
        public string? ChatName { get; set; }

        [MaxLength(200)]
        public string? ChatImage { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Участники чата
        public virtual ICollection<User> Users { get; set; } = new List<User>();
        public virtual ICollection<Message> Messages { get; set; } = new List<Message>();

        // Для связи многие-ко-многим
        public virtual ICollection<ChatUser> ChatUsers { get; set; } = new List<ChatUser>();
    }

    public class ChatUser
    {
        public string UserId { get; set; }
        public int ChatId { get; set; }
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        public virtual User User { get; set; }
        public virtual Chat Chat { get; set; }
    }
}