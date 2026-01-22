using Backend_chat.Data;
using Backend_chat.DTOs;
using Backend_chat.Hubs;
using Backend_chat.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Backend_chat.Services
{
    public class ChatService : IChatService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ChatService> _logger;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly INotificationService _notificationService;

        public ChatService(
            ApplicationDbContext context,
            ILogger<ChatService> logger,
            IHubContext<ChatHub> hubContext,
            INotificationService notificationService)
        {
            _context = context;
            _logger = logger;
            _hubContext = hubContext;
            _notificationService = notificationService;
        }

        public async Task<List<ChatDto>> GetUserChatsAsync(string userId)
        {
            var chats = await _context.Chats
                .Include(c => c.ChatUsers)
                    .ThenInclude(cu => cu.User)
                .Include(c => c.Messages.OrderByDescending(m => m.SentAt).Take(1))
                .Where(c => c.ChatUsers.Any(cu => cu.UserId == userId))
                .OrderByDescending(c => c.Messages.OrderByDescending(m => m.SentAt).FirstOrDefault().SentAt)
                .ToListAsync();

            return chats.Select(chat => {
                var dto = MapToChatDto(chat);

                // Гарантируем, что participants всегда массив
                if (dto.Participants == null)
                {
                    dto.Participants = new List<UserDto>();
                }

                return dto;
            }).ToList();
        }

        public async Task<ChatDto> GetOrCreatePrivateChatAsync(string userId, string otherUserId)
        {
            _logger.LogInformation($"Creating chat between {userId} and {otherUserId}");

            try
            {
                // Проверяем, что оба пользователя существуют
                var user1 = await _context.Users.FindAsync(userId);
                var user2 = await _context.Users.FindAsync(otherUserId);

                if (user1 == null || user2 == null)
                {
                    _logger.LogWarning($"Users not found: {userId} or {otherUserId}");
                    throw new ArgumentException("One or both users not found");
                }

                var chat = await _context.Chats
                    .Include(c => c.ChatUsers)
                        .ThenInclude(cu => cu.User)
                    .FirstOrDefaultAsync(c => c.ChatType == "private" &&
                        c.ChatUsers.Count == 2 &&
                        c.ChatUsers.Any(cu => cu.UserId == userId) &&
                        c.ChatUsers.Any(cu => cu.UserId == otherUserId));

                _logger.LogInformation($"Existing chat found: {chat != null}");

                if (chat == null)
                {
                    _logger.LogInformation("Creating new chat...");

                    chat = new Chat
                    {
                        ChatType = "private",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    _context.Chats.Add(chat);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"New chat ID: {chat.Id}");

                    var chatUsers = new[]
                    {
                        new ChatUser { UserId = userId, ChatId = chat.Id, User = user1 },
                        new ChatUser { UserId = otherUserId, ChatId = chat.Id, User = user2 }
                    };

                    _context.ChatUsers.AddRange(chatUsers);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"Added users to chat: {userId}, {otherUserId}");

                    // Загружаем чат с данными
                    chat = await _context.Chats
                        .Include(c => c.ChatUsers)
                            .ThenInclude(cu => cu.User)
                        .FirstOrDefaultAsync(c => c.Id == chat.Id);
                }

                return MapToChatDto(chat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetOrCreatePrivateChatAsync");
                throw;
            }
        }

        public async Task<List<MessageDto>> GetChatMessagesAsync(int chatId, string userId, int skip = 0, int take = 50)
        {
            var messages = await _context.Messages
                .Include(m => m.Sender)
                .Where(m => m.ChatId == chatId && !m.IsDeleted)
                .OrderByDescending(m => m.SentAt)
                .Skip(skip)
                .Take(take)
                .ToListAsync();

            return messages.Select(m => MapToMessageDto(m, userId)).ToList();
        }

        public async Task<MessageDto> SendMessageAsync(string senderId, SendMessageDto messageDto)
        {
            _logger.LogInformation($"=== SendMessageAsync STARTED ===");
            _logger.LogInformation($"SenderId: {senderId}, ChatId: {messageDto.ChatId}");

            try
            {
                _logger.LogInformation("Finding chat...");
                var chat = await _context.Chats
                    .Include(c => c.ChatUsers)
                    .FirstOrDefaultAsync(c => c.Id == messageDto.ChatId);

                if (chat == null)
                {
                    _logger.LogError($"Chat {messageDto.ChatId} not found!");
                    throw new ArgumentException("Chat not found");
                }

                _logger.LogInformation("Creating message entity...");
                var message = new Message
                {
                    Content = messageDto.Content,
                    MessageType = Enum.Parse<MessageType>(messageDto.MessageType, true),
                    FileUrl = messageDto.FileUrl,
                    FileName = messageDto.FileName,
                    FileSize = messageDto.FileSize,
                    SenderId = senderId,
                    ChatId = messageDto.ChatId,
                    SentAt = DateTime.UtcNow,
                    DeliveredAt = DateTime.UtcNow
                };

                _logger.LogInformation("Saving to database...");
                _context.Messages.Add(message);
                chat.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Message saved with ID: {message.Id}");

                // Загружаем отправителя для DTO
                _logger.LogInformation("Loading sender data...");
                await _context.Entry(message)
                    .Reference(m => m.Sender)
                    .LoadAsync();

                // ЗАГРУЖАЕМ ПОЛНУЮ ИНФОРМАЦИЮ О СООБЩЕНИИ ДЛЯ SIGNALR
                var fullMessage = await _context.Messages
                    .Include(m => m.Sender)
                    .FirstOrDefaultAsync(m => m.Id == message.Id);

                // ОТПРАВЛЯЕМ СООБЩЕНИЕ ЧЕРЕЗ SIGNALR НЕМЕДЛЕННО
                await SendMessageViaSignalR(fullMessage);

                // СОЗДАЕМ УВЕДОМЛЕНИЯ ДЛЯ ДРУГИХ УЧАСТНИКОВ ЧАТА
                await CreateNotificationsForChatUsers(fullMessage);

                _logger.LogInformation($"=== SendMessageAsync COMPLETED ===");
                return MapToMessageDto(fullMessage, senderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"=== SendMessageAsync FAILED ===");
                throw;
            }
        }

        private async Task SendMessageViaSignalR(Message message)
        {
            try
            {
                if (message == null) return;

                // Формируем объект для SignalR
                var signalrMessage = new
                {
                    Id = message.Id,
                    ChatId = message.ChatId,
                    Content = message.Content,
                    MessageType = message.MessageType.ToString(),
                    FileUrl = message.FileUrl,
                    FileName = message.FileName,
                    FileSize = message.FileSize,
                    SenderId = message.SenderId,
                    Sender = new
                    {
                        Id = message.SenderId,
                        DisplayName = message.Sender?.DisplayName ??
                                    message.Sender?.Email?.Split('@')[0] ??
                                    "Пользователь",
                        Email = message.Sender?.Email,
                        AvatarUrl = message.Sender?.AvatarUrl,
                        Status = message.Sender?.Status.ToString() ?? "Online"
                    },
                    SentAt = message.SentAt,
                    IsMine = false
                };

                // Отправляем всем в группе чата КРОМЕ отправителя
                await _hubContext.Clients.GroupExcept($"chat_{message.ChatId}", new[] { message.SenderId })
                    .SendAsync("ReceiveMessage", signalrMessage);

                _logger.LogInformation($"Message {message.Id} sent via SignalR to chat {message.ChatId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message via SignalR");
                // Не выбрасываем исключение - сообщение уже сохранено в БД
            }
        }

        private async Task CreateNotificationsForChatUsers(Message message)
        {
            try
            {
                // Получаем всех участников чата кроме отправителя
                var chatUsers = await _context.ChatUsers
                    .Include(cu => cu.User)
                    .ThenInclude(u => u.NotificationSettings)
                    .Where(cu => cu.ChatId == message.ChatId && cu.UserId != message.SenderId)
                    .ToListAsync();

                foreach (var chatUser in chatUsers)
                {
                    // Проверяем настройки уведомлений пользователя
                    if (chatUser.User?.NotificationSettings?.EnableNotifications ?? true)
                    {
                        var senderName = message.Sender?.DisplayName ??
                                        message.Sender?.Email?.Split('@')[0] ??
                                        "Пользователь";

                        var messagePreview = message.Content.Length > 100
                            ? message.Content.Substring(0, 100) + "..."
                            : message.Content;

                        var notificationData = new
                        {
                            chatId = message.ChatId,
                            messageId = message.Id,
                            senderId = message.SenderId,
                            senderName = senderName
                        };

                        await _notificationService.CreateNotificationAsync(chatUser.UserId, new CreateNotificationDto
                        {
                            Title = $"Новое сообщение от {senderName}",
                            Message = messagePreview,
                            NotificationType = "message",
                            Data = System.Text.Json.JsonSerializer.Serialize(notificationData)
                        });

                        _logger.LogInformation($"Создано уведомление для пользователя {chatUser.UserId}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating notifications for chat users");
            }
        }

        public async Task<bool> UpdateMessageAsync(int messageId, string userId, UpdateMessageDto updateDto)
        {
            var message = await _context.Messages.FindAsync(messageId);
            if (message == null || message.SenderId != userId || message.IsDeleted)
                return false;

            message.Content = updateDto.Content;
            message.IsEdited = true;
            message.EditedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteMessageAsync(int messageId, string userId)
        {
            var message = await _context.Messages.FindAsync(messageId);
            if (message == null || message.SenderId != userId)
                return false;

            message.IsDeleted = true;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> MarkMessageAsReadAsync(string userId, int messageId)
        {
            var message = await _context.Messages.FindAsync(messageId);
            if (message == null || message.SenderId == userId)
                return false;

            message.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateUserStatusAsync(string userId, UserStatus status)
        {
            try
            {
                _logger.LogInformation($"Updating user {userId} status to {status}");

                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning($"User {userId} not found");
                    return false;
                }

                user.Status = status;
                user.LastSeen = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation($"User {userId} status updated to {status}");

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating user status for {userId}");
                return false;
            }
        }

        // Новый метод для получения статусов пользователей
        public async Task<Dictionary<string, string>> GetUsersStatuses(List<string> userIds)
        {
            var result = new Dictionary<string, string>();

            var users = await _context.Users
                .Where(u => userIds.Contains(u.Id))
                .ToListAsync();

            foreach (var user in users)
            {
                result[user.Id] = user.Status.ToString();
            }

            // Добавляем отсутствующих пользователей как Offline
            foreach (var userId in userIds)
            {
                if (!result.ContainsKey(userId))
                {
                    result[userId] = "Offline";
                }
            }

            return result;
        }

        public async Task<List<UserDto>> SearchUsersAsync(string userId, string query)
        {
            var users = await _context.Users
                .Where(u => u.Id != userId &&
                           (u.Email.Contains(query) ||
                            u.DisplayName.Contains(query) ||
                            u.UserName.Contains(query)))
                .Take(20)
                .ToListAsync();

            return users.Select(MapToUserDto).ToList();
        }

        private ChatDto MapToChatDto(Chat chat)
        {
            if (chat == null)
                return new ChatDto
                {
                    Id = 0,
                    ChatType = "private",
                    ChatName = "Unknown Chat",
                    ChatImage = null,
                    UpdatedAt = DateTime.UtcNow,
                    LastMessage = string.Empty,
                    LastMessageTime = null,
                    UnreadCount = 0,
                    Participants = new List<UserDto>()
                };

            var lastMessage = chat.Messages?.FirstOrDefault();
            var participants = chat.ChatUsers?
                .Where(cu => cu?.User != null)
                .Select(cu => MapToUserDto(cu.User))
                .ToList() ?? new List<UserDto>();

            var chatName = chat.ChatName;
            if (string.IsNullOrEmpty(chatName) && participants.Count > 0)
            {
                // Для приватного чата - имя другого участника
                var otherParticipants = participants.Where(p => p.Id != participants.FirstOrDefault()?.Id).ToList();
                if (otherParticipants.Any())
                {
                    chatName = otherParticipants.First().DisplayName;
                }
                else if (participants.Any())
                {
                    chatName = participants.First().DisplayName;
                }
            }

            return new ChatDto
            {
                Id = chat.Id,
                ChatType = chat.ChatType,
                ChatName = chatName ?? "Unknown Chat",
                ChatImage = chat.ChatImage,
                UpdatedAt = chat.UpdatedAt,
                LastMessage = lastMessage?.Content ?? string.Empty,
                LastMessageTime = lastMessage?.SentAt,
                UnreadCount = 0,
                Participants = participants
            };
        }

        private MessageDto MapToMessageDto(Message message, string currentUserId)
        {
            if (message == null)
                return new MessageDto
                {
                    Id = 0,
                    Content = string.Empty,
                    MessageType = "Text",
                    FileUrl = null,
                    FileName = null,
                    FileSize = null,
                    SentAt = DateTime.UtcNow,
                    DeliveredAt = null,
                    ReadAt = null,
                    IsEdited = false,
                    IsDeleted = false,
                    Sender = new UserDto(),
                    ChatId = 0,
                    IsMine = false
                };

            return new MessageDto
            {
                Id = message.Id,
                Content = message.Content,
                MessageType = message.MessageType.ToString(),
                FileUrl = message.FileUrl,
                FileName = message.FileName,
                FileSize = message.FileSize,
                SentAt = message.SentAt,
                DeliveredAt = message.DeliveredAt,
                ReadAt = message.ReadAt,
                IsEdited = message.IsEdited,
                IsDeleted = message.IsDeleted,
                Sender = MapToUserDto(message.Sender),
                ChatId = message.ChatId,
                IsMine = message.SenderId == currentUserId
            };
        }

        private UserDto MapToUserDto(User user)
        {
            if (user == null)
                return new UserDto
                {
                    Id = string.Empty,
                    Email = string.Empty,
                    DisplayName = "Unknown User",
                    AvatarUrl = null,
                    Status = "Offline",
                    Bio = null,
                    LastSeen = DateTime.UtcNow
                };

            return new UserDto
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                DisplayName = user.DisplayName ?? user.Email?.Split('@')[0] ?? "Unknown",
                AvatarUrl = user.AvatarUrl,
                Status = user.Status.ToString(),
                Bio = user.Bio,
                LastSeen = user.LastSeen
            };
        }
    }
}