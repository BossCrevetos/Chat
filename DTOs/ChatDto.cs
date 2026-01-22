using System.ComponentModel.DataAnnotations;

namespace Backend_chat.DTOs
{
    public class ChatDto
    {
        public int Id { get; set; }
        public string ChatType { get; set; }
        public string ChatName { get; set; }
        public string ChatImage { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string LastMessage { get; set; }
        public DateTime? LastMessageTime { get; set; }
        public int UnreadCount { get; set; }
        public List<UserDto> Participants { get; set; } = new List<UserDto>();
    }

    public class CreateChatDto
    {
        [Required]
        public string UserId { get; set; } = string.Empty;

        public string? ChatName { get; set; }
    }

    public class MessageDto
    {
        public int Id { get; set; }
        public string Content { get; set; } = string.Empty;
        public string MessageType { get; set; } = string.Empty;
        public string? FileUrl { get; set; }
        public string? FileName { get; set; }
        public long? FileSize { get; set; }
        public DateTime SentAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public DateTime? ReadAt { get; set; }
        public bool IsEdited { get; set; }
        public bool IsDeleted { get; set; }
        public UserDto Sender { get; set; } = new UserDto();
        public int ChatId { get; set; }
        public bool IsMine { get; set; }
    }

    public class SendMessageDto
    {
        [Required]
        public int ChatId { get; set; }

        [Required]
        public string Content { get; set; } = string.Empty;

        public string MessageType { get; set; } = "text";
        public string? FileUrl { get; set; }
        public string? FileName { get; set; }
        public long? FileSize { get; set; }
    }

    public class UpdateMessageDto
    {
        [Required]
        public string Content { get; set; } = string.Empty;
    }
}