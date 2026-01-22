using Backend_chat.DTOs;
// УБЕРИ using Backend_chat.Enums; // НЕ НУЖЕН

namespace Backend_chat.Services
{
    public interface IChatService
    {
        Task<List<ChatDto>> GetUserChatsAsync(string userId);
        Task<ChatDto> GetOrCreatePrivateChatAsync(string userId, string otherUserId);
        Task<List<MessageDto>> GetChatMessagesAsync(int chatId, string userId, int skip = 0, int take = 50);
        Task<MessageDto> SendMessageAsync(string senderId, SendMessageDto messageDto);
        Task<bool> UpdateMessageAsync(int messageId, string userId, UpdateMessageDto updateDto);
        Task<bool> DeleteMessageAsync(int messageId, string userId);
        Task<bool> MarkMessageAsReadAsync(string userId, int messageId);
        Task<bool> UpdateUserStatusAsync(string userId, UserStatus status);  // UserStatus доступен без using
        Task<List<UserDto>> SearchUsersAsync(string userId, string query);
    }
}