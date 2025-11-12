using System.ComponentModel.DataAnnotations;

namespace VotingSystem.Models
{
    public class PositionSetting
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string PositionName { get; set; }

        [Range(1, 10)]
        public int VotesAllowed { get; set; } = 1;
    }
}