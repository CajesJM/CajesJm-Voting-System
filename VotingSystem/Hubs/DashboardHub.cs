namespace VotingSystem.Hubs
{
    using Microsoft.AspNetCore.SignalR;

    public class DashboardHub : Hub
    {
        public async Task BroadcastUpdate()
        {
            await Clients.All.SendAsync("ReceiveUpdate");
        }
    }
}
