namespace VotingSystem.Hubs
{
    using Microsoft.AspNetCore.SignalR;

    public class DashboardHub : Hub
    {
        public async Task BroadcastUpdate()
        {
            await Clients.All.SendAsync("ReceiveUpdate");
        }

        public async Task SendVotingStatusUpdate(string status)
        {
            await Clients.All.SendAsync("VotingStatusChanged", status);
        }

        // Optional: Method to notify about configuration changes
        public async Task SendConfigurationUpdate()
        {
            await Clients.All.SendAsync("ConfigurationUpdated");
        }

        // Optional: Method for user-specific updates
        public async Task NotifyUser(string userId, string message)
        {
            await Clients.User(userId).SendAsync("UserNotification", message);
        }
    }
}