namespace VotingSystem.Models
{
    public class Vote
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; }
        public int CandidateId { get; set; }
        public Candidate Candidate { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsFinal { get; set; } = false;

 
    }
}
