using System.ComponentModel.DataAnnotations;
using Backend_chat.Models;

namespace Backend_chat.DTOs
{
    public class UserDto
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Bio { get; set; }
        public DateTime LastSeen { get; set; }
    }

    public class UpdateProfileDto
    {
        public string? DisplayName { get; set; }
        public string? Bio { get; set; }
        public string? AvatarUrl { get; set; }
        public UserStatus Status { get; set; }
    }

    public class ChangePasswordDto
    {
        [Required]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 6)]
        public string NewPassword { get; set; } = string.Empty;

        [Required]
        [Compare("NewPassword")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class NotificationSettingsDto
    {
        public bool EnableNotifications { get; set; }
        public bool EnableSound { get; set; }
        public bool ShowBanner { get; set; }
        public bool SmartNotifications { get; set; }
    }
}