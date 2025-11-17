using static System.Collections.Specialized.BitVector32;

namespace VotingSystem.Models
{
    public class Candidate
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string PartyList { get; set; } = string.Empty;
        public string ProfilePicture { get; set; } = string.Empty;
        public int VoteCount { get; set; }

       
        public int ElectionId { get; set; } 

      
        public Election? Election { get; set; }

        public virtual ICollection<Vote> Votes { get; set; } = new List<Vote>();
    }
}