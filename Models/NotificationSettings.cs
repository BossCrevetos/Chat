using System.ComponentModel.DataAnnotations;

namespace Backend_chat.Models
{
    public class NotificationSettings
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; }

        public bool EnableNotifications { get; set; } = true;
        public bool EnableSound { get; set; } = true;
        public bool ShowBanner { get; set; } = true;
        public bool SmartNotifications { get; set; } = true;

        public virtual User User { get; set; }
    }
}