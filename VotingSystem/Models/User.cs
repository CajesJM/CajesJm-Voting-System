namespace VotingSystem.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string PasswordHash { get; set; }
        public string Role { get; set; } = "Pending";
        public string Email { get; set; }
        public string Course { get; set; }
        public string RequestedRole { get; set; } = "User";
        public bool IsApproved { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool HasVoted { get; set; } = false;
    }
}