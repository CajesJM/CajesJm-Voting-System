using Microsoft.EntityFrameworkCore;
using VotingSystem.Models;

namespace VotingSystem.Models
{
    public class VotingDbContext : DbContext
    {
        public VotingDbContext(DbContextOptions<VotingDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Candidate> Candidates { get; set; }
        public DbSet<Vote> Votes { get; set; }
        public DbSet<VotingConfiguration> VotingConfigurations { get; set; }
        public DbSet<PositionSetting> PositionSettings { get; set; }
        public DbSet<Election> Elections { get; set; }
        public DbSet<ElectionResult> ElectionResults { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure table names
            modelBuilder.Entity<VotingConfiguration>().ToTable("votingconfigurations");
            modelBuilder.Entity<PositionSetting>().ToTable("positionsettings");
            modelBuilder.Entity<Election>().ToTable("elections");
            modelBuilder.Entity<ElectionResult>().ToTable("electionresults");

            // Configure unique constraints
            modelBuilder.Entity<PositionSetting>()
                .HasIndex(ps => ps.PositionName)
                .IsUnique();

          
            modelBuilder.Entity<Vote>()
                .HasOne(v => v.User)
                .WithMany()
                .HasForeignKey(v => v.UserId);



            modelBuilder.Entity<Vote>()
                .HasOne(v => v.Election)
                .WithMany(e => e.Votes)
                .HasForeignKey(v => v.ElectionId);

            modelBuilder.Entity<Candidate>()
                .HasOne(c => c.Election)
                .WithMany(e => e.Candidates)
                .HasForeignKey(c => c.ElectionId);

            modelBuilder.Entity<ElectionResult>()
                .HasOne(er => er.Election)
                .WithMany(e => e.Results)
                .HasForeignKey(er => er.ElectionId);
        }
    }
}