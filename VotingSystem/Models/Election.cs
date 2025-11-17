using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Generic;

namespace VotingSystem.Models
{
    public class Election
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsActive { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation properties
        public ICollection<Candidate> Candidates { get; set; } = new List<Candidate>();
        public ICollection<Vote> Votes { get; set; } = new List<Vote>();
        public ICollection<ElectionResult> Results { get; set; } = new List<ElectionResult>();
    }
}