using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Backend_chat.Services;
using Backend_chat.DTOs;
using Backend_chat.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Backend_chat.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend_chat.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly IChatService _chatService;
        private readonly ILogger<ChatHub> _logger;
        private readonly UserManager<User> _userManager;
        private readonly INotificationService _notificationService;
        private readonly ApplicationDbContext _context;

        // Для отслеживания активности пользователей в чатах
        private static readonly Dictionary<string, int> _activeUsersInChats = new();
        private static readonly Dictionary<string, DateTime> _userLastActivity = new();

        public ChatHub(
            IChatService chatService,
            ILogger<ChatHub> logger,
            UserManager<User> userManager,
            INotificationService notificationService,
            ApplicationDbContext context)
        {
            _chatService = chatService;
            _logger = logger;
            _userManager = userManager;
            _notificationService = notificationService;
            _context = context;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier;
            if (userId != null)
            {
                await _chatService.UpdateUserStatusAsync(userId, UserStatus.Online);
                await Clients.All.SendAsync("UserStatusChanged", userId, UserStatus.Online.ToString());

                // Обновляем активность
                _userLastActivity[userId] = DateTime.UtcNow;
            }
            await base.OnConnectedAsync();
            _logger.LogInformation($"User {userId} connected");
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.UserIdentifier;
            if (userId != null)
            {
                await _chatService.UpdateUserStatusAsync(userId, UserStatus.Offline);
                await Clients.All.SendAsync("UserStatusChanged", userId, UserStatus.Offline.ToString());

                // Удаляем из активных чатов
                var activeChats = _activeUsersInChats.Where(x => x.Key.StartsWith($"{userId}_")).ToList();
                foreach (var chat in activeChats)
                {
                    _activeUsersInChats.Remove(chat.Key);
                }
            }
            await base.OnDisconnectedAsync(exception);
            _logger.LogInformation($"User {userId} disconnected");
        }

        // Метод для отправки сообщений - ОТПРАВЛЯЕТ ТОЛЬКО ДРУГИМ ПОЛЬЗОВАТЕЛЯМ
        public async Task SendMessage(int chatId, string content)
        {
            var userId = Context.UserIdentifier;
            if (userId == null) return;

            try
            {
                // Отправляем сообщение через ChatService
                var messageDto = new SendMessageDto
                {
                    ChatId = chatId,
                    Content = content,
                    MessageType = "Text"
                };

                var message = await _chatService.SendMessageAsync(userId, messageDto);

                // Получаем информацию о чате и пользователях
                var chat = await _context.Chats
                    .Include(c => c.ChatUsers)
                        .ThenInclude(cu => cu.User)
                    .Include(c => c.Messages.OrderByDescending(m => m.SentAt).Take(1))
                    .FirstOrDefaultAsync(c => c.Id == chatId);

                if (chat != null)
                {
                    var sender = await _userManager.FindByIdAsync(userId);
                    var senderName = sender?.DisplayName ?? sender?.Email?.Split('@')[0] ?? "Пользователь";

                    // Отправляем уведомления всем участникам чата кроме отправителя
                    foreach (var chatUser in chat.ChatUsers)
                    {
                        if (chatUser.UserId == userId) continue;

                        // Проверяем настройки уведомлений пользователя
                        var userSettings = await _context.NotificationSettings
                            .FirstOrDefaultAsync(ns => ns.UserId == chatUser.UserId);

                        if (userSettings == null || userSettings.EnableNotifications)
                        {
                            var messagePreview = content.Length > 100
                                ? content.Substring(0, 100) + "..."
                                : content;

                            var notificationData = JsonSerializer.Serialize(new
                            {
                                chatId = chatId,
                                messageId = message.Id,
                                senderId = userId,
                                senderName = senderName
                            });

                            // Отправляем уведомление через SignalR
                            await SendNotificationToUser(
                                chatUser.UserId,
                                $"Новое сообщение от {senderName}",
                                messagePreview,
                                "message",
                                notificationData
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SendMessage");
                throw new HubException($"Failed to send message: {ex.Message}");
            }
        }

        // Метод для отправки файлов
        public async Task SendFileMessage(int chatId, string fileMessageJson)
        {
            var userId = Context.UserIdentifier;
            if (userId == null) return;

            try
            {
                // Обновляем активность
                _userLastActivity[userId] = DateTime.UtcNow;

                var fileMessage = JsonSerializer.Deserialize<FileMessageDto>(fileMessageJson);
                if (fileMessage == null) return;

                var messageDto = new SendMessageDto
                {
                    ChatId = chatId,
                    Content = fileMessage.Content,
                    MessageType = fileMessage.MessageType,
                    FileUrl = fileMessage.FileUrl,
                    FileName = fileMessage.FileName,
                    FileSize = fileMessage.FileSize
                };

                var message = await _chatService.SendMessageAsync(userId, messageDto);

                var user = await _userManager.FindByIdAsync(userId);

                var signalrMessage = new
                {
                    Id = message.Id,
                    ChatId = message.ChatId,
                    Content = message.Content,
                    MessageType = message.MessageType,
                    FileUrl = message.FileUrl,
                    FileName = message.FileName,
                    FileSize = message.FileSize,
                    SenderId = userId,
                    Sender = new
                    {
                        Id = userId,
                        DisplayName = user?.DisplayName ?? user?.Email?.Split('@')[0] ?? "Пользователь",
                        Email = user?.Email,
                        AvatarUrl = user?.AvatarUrl
                    },
                    SentAt = message.SentAt,
                    IsMine = false
                };

                await Clients.OthersInGroup($"chat_{chatId}").SendAsync("ReceiveMessage", signalrMessage);

                // Отправляем уведомления
                await SendNotificationToChatUsers(chatId, userId,
                    fileMessage.MessageType == "Image" ? "Отправлено изображение" : "Отправлен файл",
                    message.Id);

                _logger.LogInformation($"File message sent to chat {chatId} from {userId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SendFileMessage");
                throw new HubException($"Failed to send file message: {ex.Message}");
            }
        }

        public async Task SendNotification(string userId, string title, string message, string type = "message")
        {
            try
            {
                Console.WriteLine($"🔔 Отправляю уведомление пользователю {userId}: {title}");

                var notification = new
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = title,
                    Message = message,
                    Type = type,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    IsRead = false
                };

                await Clients.User(userId).SendAsync("ReceiveNotification", notification);
                Console.WriteLine($"✅ Уведомление отправлено через SignalR");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка отправки уведомления: {ex.Message}");
            }
        }

        // Вспомогательный класс для файловых сообщений
        public class FileMessageDto
        {
            public string Content { get; set; } = string.Empty;
            public string MessageType { get; set; } = string.Empty;
            public string FileUrl { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
            public long FileSize { get; set; }
        }

        private async Task SendNotificationToChatUsers(int chatId, string senderId, string messageContent, int messageId)
        {
            try
            {
                // Получаем чат с пользователями
                var chat = await _context.Chats
                    .Include(c => c.ChatUsers)
                        .ThenInclude(cu => cu.User)
                    .FirstOrDefaultAsync(c => c.Id == chatId);

                if (chat == null) return;

                var sender = await _userManager.FindByIdAsync(senderId);
                var senderName = sender?.DisplayName ?? sender?.Email?.Split('@')[0] ?? "Пользователь";

                // Отправляем уведомления всем участникам чата кроме отправителя
                foreach (var chatUser in chat.ChatUsers)
                {
                    if (chatUser.UserId == senderId) continue;

                    var userKey = $"{chatUser.UserId}_{chatId}";

                    // Умные уведомления: не отправляем если пользователь активен в этом чате
                    var isUserActiveInChat = _activeUsersInChats.ContainsKey(userKey);

                    // Проверяем настройки уведомлений пользователя
                    var userSettings = await _context.NotificationSettings
                        .FirstOrDefaultAsync(ns => ns.UserId == chatUser.UserId);

                    var shouldSendNotification = !isUserActiveInChat &&
                                                (userSettings == null || userSettings.EnableNotifications);

                    if (shouldSendNotification)
                    {
                        // Создаем уведомление в БД
                        var notification = await _notificationService.CreateNotificationAsync(chatUser.UserId, new CreateNotificationDto
                        {
                            Title = $"Новое сообщение от {senderName}",
                            Message = messageContent.Length > 100
                                ? messageContent.Substring(0, 100) + "..."
                                : messageContent,
                            NotificationType = "message",
                            Data = JsonSerializer.Serialize(new
                            {
                                chatId,
                                senderId,
                                senderName,
                                messageId
                            })
                        });

                        // Отправляем уведомление через SignalR
                        await Clients.User(chatUser.UserId).SendAsync("ReceiveNotification", new
                        {
                            Id = notification.Id,
                            Title = $"Новое сообщение от {senderName}",
                            Message = messageContent,
                            ChatId = chatId,
                            SenderId = senderId,
                            Type = "message",
                            IsRead = false,
                            CreatedAt = DateTime.UtcNow
                        });

                        // Отправляем браузерное уведомление если разрешено
                        if (userSettings == null || userSettings.ShowBanner)
                        {
                            await Clients.User(chatUser.UserId).SendAsync("ShowBrowserNotification", new
                            {
                                Title = senderName,
                                Body = messageContent,
                                Icon = sender?.AvatarUrl ?? "/favicon.ico",
                                Data = new { chatId, messageId }
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending notifications");
            }
        }

        public async Task JoinChat(int chatId)
        {
            var userId = Context.UserIdentifier;
            if (userId == null) return;

            await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");

            // Отмечаем пользователя как активного в этом чате
            var userKey = $"{userId}_{chatId}";
            _activeUsersInChats[userKey] = chatId;
            _userLastActivity[userId] = DateTime.UtcNow;

            _logger.LogInformation($"User {userId} joined chat {chatId}");
        }

        public async Task LeaveChat(int chatId)
        {
            var userId = Context.UserIdentifier;
            if (userId == null) return;

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat_{chatId}");

            // Убираем пользователя из активных в этом чате
            var userKey = $"{userId}_{chatId}";
            _activeUsersInChats.Remove(userKey);

            _logger.LogInformation($"User {userId} left chat {chatId}");
        }

        public async Task UpdateUserActivity()
        {
            var userId = Context.UserIdentifier;
            if (userId != null)
            {
                _userLastActivity[userId] = DateTime.UtcNow;
            }
        }

        // ========== МЕТОД TYPING ==========
        public async Task Typing(int chatId, bool isTyping)
        {
            var userId = Context.UserIdentifier;
            await Clients.OthersInGroup($"chat_{chatId}").SendAsync("UserTyping", userId, chatId, isTyping);
            _logger.LogInformation($"User {userId} typing in chat {chatId}: {isTyping}");
        }

        public async Task SendNotificationToUser(string userId, string title, string message, string notificationType = "message", string data = null)
        {
            try
            {
                var notificationDto = new CreateNotificationDto
                {
                    Title = title,
                    Message = message,
                    NotificationType = notificationType,
                    Data = data
                };

                // Сохраняем уведомление в БД
                var notification = await _notificationService.CreateNotificationAsync(userId, notificationDto);

                // Отправляем уведомление через SignalR
                var signalrNotification = new
                {
                    Id = notification.Id,
                    Title = notification.Title,
                    Message = notification.Message,
                    Type = notification.NotificationType,
                    Data = notification.Data,
                    IsRead = false,
                    CreatedAt = notification.CreatedAt
                };

                await Clients.User(userId).SendAsync("ReceiveNotification", signalrNotification);

                _logger.LogInformation($"Уведомление отправлено пользователю {userId}: {title}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка отправки уведомления через SignalR");
            }
        }

        public async Task MarkMessageAsRead(int messageId, int chatId)
        {
            var userId = Context.UserIdentifier;
            if (userId != null)
            {
                var success = await _chatService.MarkMessageAsReadAsync(userId, messageId);
                if (success)
                {
                    await Clients.Group($"chat_{chatId}").SendAsync("MessageRead", messageId, userId);
                }
            }
        }

        // Тестовый метод
        public async Task<string> Echo(string message)
        {
            return $"Server says: {message} at {DateTime.Now:HH:mm:ss}";
        }

        public async Task UpdateStatus(string status)
        {
            var userId = Context.UserIdentifier;
            if (userId != null)
            {
                if (Enum.TryParse<UserStatus>(status, out var userStatus))
                {
                    await _chatService.UpdateUserStatusAsync(userId, userStatus);
                    await Clients.All.SendAsync("UserStatusChanged", userId, status);
                }
            }
        }
    }
}