using System.Linq;

namespace VotingSystem.Models
{
    public static class VotingDbSeeder
    {
        public static void Seed(VotingDbContext context)
        {
            if (!context.Users.Any())
            {
                context.Users.AddRange(
                    new User
                    {
                        Username = "admin",
                        PasswordHash = SecurityHelper.HashPassword("1234"),
                        Role = "Admin"
                    }
                );
            }


            context.SaveChanges();
        }
    }
}