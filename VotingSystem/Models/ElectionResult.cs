using System;

namespace VotingSystem.Models
{
    public class ElectionResult
    {
        public int Id { get; set; }
        public int ElectionId { get; set; }
        public string Position { get; set; } = string.Empty;
        public string WinnerName { get; set; } = string.Empty;
        public int WinnerVotes { get; set; }
        public int TotalVotes { get; set; }
        public decimal WinnerPercentage { get; set; }
        public string RunnerUpName { get; set; } = string.Empty;
        public int RunnerUpVotes { get; set; }
        public DateTime ResultDate { get; set; } = DateTime.Now;

       
        public Election? Election { get; set; }
    }
}