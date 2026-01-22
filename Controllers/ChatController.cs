using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Backend_chat.Services;
using Backend_chat.DTOs;

namespace Backend_chat.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly IChatService _chatService;

        public ChatController(IChatService chatService)
        {
            _chatService = chatService;
        }

        [HttpGet("chats")]
        public async Task<IActionResult> GetUserChats()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var chats = await _chatService.GetUserChatsAsync(userId);
            return Ok(chats);
        }

        [HttpGet("chat/{otherUserId}")]
        public async Task<IActionResult> GetOrCreatePrivateChat(string otherUserId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var chat = await _chatService.GetOrCreatePrivateChatAsync(userId, otherUserId);
            return Ok(chat);
        }

        [HttpGet("messages/{chatId}")]
        public async Task<IActionResult> GetChatMessages(int chatId, [FromQuery] int skip = 0, [FromQuery] int take = 50)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var messages = await _chatService.GetChatMessagesAsync(chatId, userId, skip, take);
            return Ok(messages);
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageDto messageDto)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var message = await _chatService.SendMessageAsync(userId, messageDto);
            return Ok(message);
        }

        [HttpPut("message/{messageId}")]
        public async Task<IActionResult> UpdateMessage(int messageId, [FromBody] UpdateMessageDto updateDto)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var success = await _chatService.UpdateMessageAsync(messageId, userId, updateDto);
            if (!success)
                return NotFound();

            return Ok(new { Success = true });
        }

        [HttpDelete("message/{messageId}")]
        public async Task<IActionResult> DeleteMessage(int messageId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var success = await _chatService.DeleteMessageAsync(messageId, userId);
            if (!success)
                return NotFound();

            return Ok(new { Success = true });
        }

        [HttpPost("message/{messageId}/read")]
        public async Task<IActionResult> MarkAsRead(int messageId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var success = await _chatService.MarkMessageAsReadAsync(userId, messageId);
            return Ok(new { Success = success });
        }

        [HttpGet("search/users")]
        public async Task<IActionResult> SearchUsers([FromQuery] string query)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var users = await _chatService.SearchUsersAsync(userId, query);
            return Ok(users);
        }
    }
}