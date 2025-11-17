using System.Linq;

namespace VotingSystem.Models
{
    public static class VotingDbSeeder
    {
        public static void Seed(VotingDbContext context)
        {
            // Seed default election if none exists
            if (!context.Elections.Any())
            {
                context.Elections.AddRange(
                    new Election
                    {
                        Name = "General Election 2024",
                        Description = "Annual General Election for Student Council",
                        StartDate = System.DateTime.Now.AddDays(-7),
                        EndDate = System.DateTime.Now.AddDays(30),
                        IsActive = true,
                        IsCompleted = false
                    }
                );
            }

            // Seed admin user if none exists
            if (!context.Users.Any())
            {
                context.Users.AddRange(
                    new User
                    {
                        Username = "JM",
                        PasswordHash = SecurityHelper.HashPassword("1212"),
                        Role = "Admin",
                        Email = "admin@voting.com",
                        Course = "Administration",
                        RequestedRole = "Admin",
                        IsApproved = true,
                        HasVoted = false
                    }
                );
            }

            // Seed voting configuration if none exists
            if (!context.VotingConfigurations.Any())
            {
                context.VotingConfigurations.AddRange(
                    new VotingConfiguration
                    {
                        IsVotingOpen = false
                    }
                );
            }

            context.SaveChanges();
        }
    }
}