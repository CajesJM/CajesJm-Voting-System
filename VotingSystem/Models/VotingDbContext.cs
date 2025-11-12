using Microsoft.EntityFrameworkCore;
using VotingSystem.Models;

namespace VotingSystem.Models
{
    public class VotingDbContext : DbContext
    {
        public VotingDbContext(DbContextOptions<VotingDbContext> options) : base(options)
        {
        }

        // Your existing DbSets
        public DbSet<User> Users { get; set; }
        public DbSet<Candidate> Candidates { get; set; }
        public DbSet<Vote> Votes { get; set; }

        // Add these new DbSets
        public DbSet<VotingConfiguration> VotingConfigurations { get; set; }
        public DbSet<PositionSetting> PositionSettings { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure table names to match your existing naming convention
            modelBuilder.Entity<VotingConfiguration>().ToTable("votingconfigurations");
            modelBuilder.Entity<PositionSetting>().ToTable("positionsettings");

            // Configure unique constraint for PositionSetting
            modelBuilder.Entity<PositionSetting>()
                .HasIndex(ps => ps.PositionName)
                .IsUnique();
        }
    }
}