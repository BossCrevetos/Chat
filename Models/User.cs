using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace Backend_chat.Models
{
    public class User : IdentityUser
    {
        [MaxLength(200)]
        public string? AvatarUrl { get; set; }

        [MaxLength(100)]
        public string? DisplayName { get; set; }

        public UserStatus Status { get; set; } = UserStatus.Offline;  // UserStatus из того же пространства имен

        [MaxLength(500)]
        public string? Bio { get; set; }

        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Навигационные свойства
        public virtual ICollection<Message> MessagesSent { get; set; } = new List<Message>();
        public virtual ICollection<ChatUser> ChatUsers { get; set; } = new List<ChatUser>();
        public virtual NotificationSettings NotificationSettings { get; set; }
    }
}