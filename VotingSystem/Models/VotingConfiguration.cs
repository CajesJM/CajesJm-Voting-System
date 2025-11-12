using System.ComponentModel.DataAnnotations;

namespace VotingSystem.Models
{
    public class VotingConfiguration
    {
        [Key]
        public int Id { get; set; }
        public bool IsVotingOpen { get; set; } = false;
        public DateTime? LastModified { get; set; } = DateTime.Now;
    }
}