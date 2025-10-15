using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using VotingSystem.Hubs;
using VotingSystem.Models;

namespace VotingSystem.Controllers
{
    [Authorize(Roles = "User")]
    public class UserController : Controller
    {
        private readonly VotingDbContext _context;

        public UserController(VotingDbContext context)
        {
            _context = context;
        }

      
        public IActionResult Dashboard()
        {
            var candidates = _context.Candidates.ToList();
            return View(candidates);
        }

        [HttpPost]
        public async Task<IActionResult> Vote(int candidateId)
        {
            var username = User.Identity?.Name;
            var user = _context.Users.FirstOrDefault(u => u.Username == username);

            if (user == null)
                return Unauthorized();

            // Check if user already voted
            var hasVoted = _context.Votes.Any(v => v.UserId == user.Id);
            if (hasVoted)
            {
                TempData["Message"] = "You have already voted.";
                return RedirectToAction("Dashboard");
            }

            // Record vote
            var vote = new Vote
            {
                UserId = user.Id,
                CandidateId = candidateId,
                Timestamp = DateTime.Now
            };

            _context.Votes.Add(vote);

            // Increment vote count
            var candidate = _context.Candidates.FirstOrDefault(c => c.Id == candidateId);
            if (candidate != null)
            {
                candidate.VoteCount += 1;
            }

            await _context.SaveChangesAsync();

            // 🔔 Broadcast update to all dashboards
            var hubContext = HttpContext.RequestServices.GetService<IHubContext<DashboardHub>>();
            if (hubContext != null)
            {
                await hubContext.Clients.All.SendAsync("ReceiveUpdate");
            }

            TempData["Message"] = $"Your vote for {candidate?.Name} has been recorded!";
            return RedirectToAction("Dashboard");
        }
    }
}