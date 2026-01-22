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

        // Для отслеживания активности пользователей
        private static readonly Dictionary<string, string> _userConnections = new();
        private static readonly Dictionary<string, UserStatus> _userStatuses = new();
        private static readonly Dictionary<string, DateTime> _userLastActivity = new();
        private static readonly Dictionary<string, List<int>> _userActiveChats = new();

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
                _logger.LogInformation($"User {userId} connected with connection {Context.ConnectionId}");

                // Сохраняем соединение
                _userConnections[Context.ConnectionId] = userId;

                // Получаем пользователя из БД
                var user = await _userManager.FindByIdAsync(userId);
                if (user != null)
                {
                    // Обновляем статус в БД
                    user.Status = UserStatus.Online;
                    user.LastSeen = DateTime.UtcNow;
                    await _userManager.UpdateAsync(user);

                    // Сохраняем статус в памяти
                    _userStatuses[userId] = UserStatus.Online;
                    _userLastActivity[userId] = DateTime.UtcNow;

                    // Уведомляем всех о новом статусе
                    await Clients.All.SendAsync("UserStatusChanged", userId, "Online");

                    _logger.LogInformation($"User {userId} status updated to Online");
                }
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.UserIdentifier;
            if (userId != null)
            {
                _logger.LogInformation($"User {userId} disconnected, connection: {Context.ConnectionId}");

                // Удаляем соединение
                _userConnections.Remove(Context.ConnectionId);

                // Проверяем, есть ли другие активные соединения у этого пользователя
                var hasOtherConnections = _userConnections.Values.Any(v => v == userId);

                if (!hasOtherConnections)
                {
                    // Если это последнее соединение - меняем статус на Offline
                    var user = await _userManager.FindByIdAsync(userId);
                    if (user != null)
                    {
                        user.Status = UserStatus.Offline;
                        await _userManager.UpdateAsync(user);

                        _userStatuses[userId] = UserStatus.Offline;

                        // Уведомляем всех
                        await Clients.All.SendAsync("UserStatusChanged", userId, "Offline");

                        _logger.LogInformation($"User {userId} status updated to Offline (no active connections)");
                    }

                    // Очищаем активные чаты пользователя
                    if (_userActiveChats.ContainsKey(userId))
                    {
                        _userActiveChats.Remove(userId);
                    }
                }
                else
                {
                    _logger.LogInformation($"User {userId} still has other active connections");
                }
            }
            await base.OnDisconnectedAsync(exception);
        }

        // Метод для обновления статуса вручную
        public async Task UpdateStatus(string status)
        {
            var userId = Context.UserIdentifier;
            if (userId != null)
            {
                _logger.LogInformation($"User {userId} updating status to {status}");

                if (Enum.TryParse<UserStatus>(status, out var userStatus))
                {
                    // Обновляем статус в БД
                    var user = await _userManager.FindByIdAsync(userId);
                    if (user != null)
                    {
                        user.Status = userStatus;
                        user.LastSeen = DateTime.UtcNow;
                        await _userManager.UpdateAsync(user);

                        // Сохраняем статус в памяти
                        _userStatuses[userId] = userStatus;
                        _userLastActivity[userId] = DateTime.UtcNow;

                        // Уведомляем всех
                        await Clients.All.SendAsync("UserStatusChanged", userId, status);

                        _logger.LogInformation($"User {userId} status changed to {status}");
                    }
                }
            }
        }

        // Метод для обновления активности (вызывается при любом действии пользователя)
        public async Task UpdateUserActivity()
        {
            var userId = Context.UserIdentifier;
            if (userId != null)
            {
                _userLastActivity[userId] = DateTime.UtcNow;

                // Если статус был Away - меняем на Online
                if (_userStatuses.TryGetValue(userId, out var currentStatus))
                {
                    if (currentStatus == UserStatus.Away)
                    {
                        await UpdateStatus("Online");
                    }
                }
            }
        }

        // Метод для отправки сообщений
        public async Task SendMessage(int chatId, string content)
        {
            var userId = Context.UserIdentifier;
            if (userId == null) return;

            try
            {
                // Обновляем активность
                _userLastActivity[userId] = DateTime.UtcNow;

                // Отправляем сообщение через ChatService
                var messageDto = new SendMessageDto
                {
                    ChatId = chatId,
                    Content = content,
                    MessageType = "Text"
                };

                var message = await _chatService.SendMessageAsync(userId, messageDto);

                // Получаем информацию о чате
                var chat = await _context.Chats
                    .Include(c => c.ChatUsers)
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

                _logger.LogInformation($"Message sent to chat {chatId} from {userId}");
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
                _logger.LogInformation($"Sending notification to user {userId}: {title}");

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
                _logger.LogInformation($"Notification sent via SignalR");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending notification: {ex.Message}");
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
                var chat = await _context.Chats
                    .Include(c => c.ChatUsers)
                        .ThenInclude(cu => cu.User)
                    .FirstOrDefaultAsync(c => c.Id == chatId);

                if (chat == null) return;

                var sender = await _userManager.FindByIdAsync(senderId);
                var senderName = sender?.DisplayName ?? sender?.Email?.Split('@')[0] ?? "Пользователь";

                foreach (var chatUser in chat.ChatUsers)
                {
                    if (chatUser.UserId == senderId) continue;

                    var userKey = $"{chatUser.UserId}_{chatId}";

                    // Проверяем настройки уведомлений пользователя
                    var userSettings = await _context.NotificationSettings
                        .FirstOrDefaultAsync(ns => ns.UserId == chatUser.UserId);

                    var shouldSendNotification = userSettings == null || userSettings.EnableNotifications;

                    if (shouldSendNotification)
                    {
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

            // Добавляем чат в список активных для пользователя
            if (!_userActiveChats.ContainsKey(userId))
            {
                _userActiveChats[userId] = new List<int>();
            }

            if (!_userActiveChats[userId].Contains(chatId))
            {
                _userActiveChats[userId].Add(chatId);
            }

            _userLastActivity[userId] = DateTime.UtcNow;

            _logger.LogInformation($"User {userId} joined chat {chatId}");
        }

        public async Task LeaveChat(int chatId)
        {
            var userId = Context.UserIdentifier;
            if (userId == null) return;

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat_{chatId}");

            // Убираем чат из списка активных
            if (_userActiveChats.ContainsKey(userId))
            {
                _userActiveChats[userId].Remove(chatId);
                if (_userActiveChats[userId].Count == 0)
                {
                    _userActiveChats.Remove(userId);
                }
            }

            _logger.LogInformation($"User {userId} left chat {chatId}");
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

                var notification = await _notificationService.CreateNotificationAsync(userId, notificationDto);

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

                _logger.LogInformation($"Notification sent to user {userId}: {title}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending notification via SignalR");
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

        // Получение статусов пользователей
        public async Task<Dictionary<string, string>> GetUserStatuses(List<string> userIds)
        {
            var result = new Dictionary<string, string>();

            foreach (var userId in userIds)
            {
                if (_userStatuses.TryGetValue(userId, out var status))
                {
                    result[userId] = status.ToString();
                }
                else
                {
                    // Если статуса нет в памяти, берем из базы данных
                    var user = await _userManager.FindByIdAsync(userId);
                    if (user != null)
                    {
                        result[userId] = user.Status.ToString();
                        _userStatuses[userId] = user.Status;
                    }
                    else
                    {
                        result[userId] = "Offline";
                    }
                }
            }

            return result;
        }

        // Проверка активности пользователя
        public async Task CheckUserActivity()
        {
            var userId = Context.UserIdentifier;
            if (userId != null && _userLastActivity.ContainsKey(userId))
            {
                var timeSinceLastActivity = DateTime.UtcNow - _userLastActivity[userId];

                // Если пользователь неактивен более 5 минут и не в статусе Away/DND
                if (timeSinceLastActivity.TotalMinutes >= 5 &&
                    _userStatuses.TryGetValue(userId, out var currentStatus) &&
                    currentStatus != UserStatus.DoNotDisturb &&
                    currentStatus != UserStatus.Away)
                {
                    await UpdateStatus("Away");
                }
            }
        }
    }
}