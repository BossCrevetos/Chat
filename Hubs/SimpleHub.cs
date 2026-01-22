using Microsoft.AspNetCore.SignalR;

namespace Backend_chat.Hubs
{
    public class SimpleHub : Hub
    {
        public async Task<string> Echo(string message)
        {
            Console.WriteLine($"Echo called: {message}");
            return $"Server says: {message}";
        }

        public async Task SendTestMessage(string text)
        {
            Console.WriteLine($"SendTestMessage: {text}");
            await Clients.All.SendAsync("ReceiveTest", Context.ConnectionId, text);
        }
    }
}