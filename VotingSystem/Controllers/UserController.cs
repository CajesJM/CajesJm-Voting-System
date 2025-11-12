using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using VotingSystem.Hubs;
using VotingSystem.Models;

namespace VotingSystem.Controllers
{
    [Authorize(Roles = "User")]
    public class UserController : Controller
    {
        private readonly VotingDbContext _context;
        private readonly IHubContext<DashboardHub> _hubContext;

        public UserController(VotingDbContext context, IHubContext<DashboardHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        public IActionResult Dashboard()
        {
            var username = User.Identity?.Name;
            var user = _context.Users.FirstOrDefault(u => u.Username == username);

            if (user != null)
            {
                ViewBag.Username = user.Username;
                ViewBag.UserEmail = user.Email ?? "Not provided";
                ViewBag.UserCourse = user.Course ?? "Not specified";
                ViewBag.UserRole = user.Role ?? "User";
                ViewBag.HasVoted = user.HasVoted;

                // Get user's current votes for the ballot preview
                var userVotes = _context.Votes
                    .Where(v => v.UserId == user.Id)
                    .Include(v => v.Candidate)
                    .ToList();
                ViewBag.UserVotes = userVotes;
            }
            else
            {
                ViewBag.Username = username ?? "Unknown User";
                ViewBag.UserEmail = "Not available";
                ViewBag.UserCourse = "Not available";
                ViewBag.UserRole = "User";
                ViewBag.HasVoted = false;
                ViewBag.UserVotes = new List<Vote>();
            }

            // Group candidates by position
            var candidatesByPosition = _context.Candidates
                .GroupBy(c => c.Position)
                .ToDictionary(g => g.Key, g => g.ToList());

            ViewBag.CandidatesByPosition = candidatesByPosition;

            // Get voting status and position settings
            var votingConfig = _context.VotingConfigurations.FirstOrDefault() ?? new VotingConfiguration();
            var positionSettings = _context.PositionSettings.ToDictionary(ps => ps.PositionName, ps => ps.VotesAllowed);

            ViewBag.VotingStatus = votingConfig.IsVotingOpen ? "Open" : "Closed";
            ViewBag.PositionSettings = positionSettings;
            ViewBag.IsVotingOpen = votingConfig.IsVotingOpen;

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> VoteForPosition([FromBody] VoteRequest request)
        {
            // Check if voting is open
            var votingConfig = await _context.VotingConfigurations.FirstOrDefaultAsync();
            if (votingConfig == null || !votingConfig.IsVotingOpen)
            {
                return BadRequest(new { message = "Voting is currently closed. You cannot cast votes at this time." });
            }

            var username = User.Identity?.Name;
            var user = _context.Users.FirstOrDefault(u => u.Username == username);

            if (user == null)
                return Unauthorized(new { message = "User not found." });

            if (user.HasVoted)
                return BadRequest(new { message = "You have already submitted your final vote." });

            // Get the candidate
            var candidate = await _context.Candidates.FindAsync(request.CandidateId);
            if (candidate == null)
                return BadRequest(new { message = "Candidate not found." });

            // Check position vote limits
            var positionSetting = await _context.PositionSettings
                .FirstOrDefaultAsync(ps => ps.PositionName == candidate.Position);

            var votesAllowed = positionSetting?.VotesAllowed ?? 1;

            // Get current votes for this position
            var existingVotes = _context.Votes
                .Where(v => v.UserId == user.Id && v.Candidate.Position == candidate.Position)
                .Include(v => v.Candidate)
                .ToList();

            // If user has reached the vote limit for this position, remove the oldest vote
            if (existingVotes.Count >= votesAllowed)
            {
                var oldestVote = existingVotes.OrderBy(v => v.Timestamp).First();
                _context.Votes.Remove(oldestVote);
            }

            // Add new vote
            var vote = new Vote
            {
                UserId = user.Id,
                CandidateId = request.CandidateId,
                Timestamp = DateTime.Now,
                IsFinal = false // Not finalized until user submits ballot
            };

            _context.Votes.Add(vote);
            await _context.SaveChangesAsync();

            // 🔔 Broadcast update
            await _hubContext.Clients.All.SendAsync("ReceiveUpdate");

            return Ok(new
            {
                message = $"Vote for {candidate.Name} ({candidate.Position}) recorded!",
                position = candidate.Position,
                candidateName = candidate.Name
            });
        }

        [HttpPost]
        public async Task<IActionResult> SubmitBallot()
        {
            // Check if voting is open
            var votingConfig = await _context.VotingConfigurations.FirstOrDefaultAsync();
            if (votingConfig == null || !votingConfig.IsVotingOpen)
            {
                return BadRequest(new { message = "Voting is currently closed. You cannot submit your ballot at this time." });
            }

            var username = User.Identity?.Name;
            var user = _context.Users.FirstOrDefault(u => u.Username == username);

            if (user == null)
                return Unauthorized(new { message = "User not found." });

            if (user.HasVoted)
                return BadRequest(new { message = "You have already submitted your ballot." });

            // Check if user has voted for all positions
            var userVotes = _context.Votes
                .Where(v => v.UserId == user.Id)
                .Include(v => v.Candidate)
                .ToList();

            var allPositions = _context.Candidates.Select(c => c.Position).Distinct().ToList();
            var votedPositions = userVotes.Select(v => v.Candidate.Position).Distinct().ToList();
            var missingPositions = allPositions.Except(votedPositions).ToList();

            if (missingPositions.Any())
            {
                return BadRequest(new
                {
                    message = $"Please vote for all positions. Missing: {string.Join(", ", missingPositions)}",
                    missingPositions = missingPositions
                });
            }

            // Check if user has exceeded vote limits for any position
            var positionViolations = new List<string>();
            foreach (var position in votedPositions)
            {
                var positionVotes = userVotes.Count(v => v.Candidate.Position == position);
                var positionSetting = await _context.PositionSettings
                    .FirstOrDefaultAsync(ps => ps.PositionName == position);

                var votesAllowed = positionSetting?.VotesAllowed ?? 1;

                if (positionVotes > votesAllowed)
                {
                    positionViolations.Add($"{position} (max {votesAllowed} vote(s))");
                }
            }

            if (positionViolations.Any())
            {
                return BadRequest(new
                {
                    message = $"You have voted for too many candidates in: {string.Join(", ", positionViolations)}",
                    violations = positionViolations
                });
            }

            // Finalize all votes
            foreach (var vote in userVotes)
            {
                vote.IsFinal = true;
                // Increment candidate vote count
                var candidate = await _context.Candidates.FindAsync(vote.CandidateId);
                if (candidate != null)
                {
                    candidate.VoteCount += 1;
                }
            }

            user.HasVoted = true;
            await _context.SaveChangesAsync();

            // 🔔 Broadcast update
            await _hubContext.Clients.All.SendAsync("ReceiveUpdate");

            return Ok(new { message = "Your ballot has been submitted successfully! Thank you for voting." });
        }

        [HttpPost]
        public async Task<IActionResult> ClearVote([FromBody] ClearVoteRequest request)
        {
            try
            {
                var username = User.Identity?.Name;
                var user = _context.Users.FirstOrDefault(u => u.Username == username);

                if (user == null)
                    return Json(new { success = false, message = "User not found" });

                // If candidateId is provided and valid, remove only that candidate vote
                if (request.CandidateId.HasValue && request.CandidateId.Value > 0)
                {
                    var voteToRemove = _context.Votes
                        .FirstOrDefault(v => v.UserId == user.Id && v.CandidateId == request.CandidateId.Value);

                    if (voteToRemove != null)
                    {
                        _context.Votes.Remove(voteToRemove);
                        await _context.SaveChangesAsync();

                        // 🔔 Broadcast update
                        await _hubContext.Clients.All.SendAsync("ReceiveUpdate");

                        return Json(new { success = true, message = "Vote removed successfully" });
                    }
                }
                // Remove all votes for the position
                else if (!string.IsNullOrEmpty(request.Position))
                {
                    var votesToRemove = _context.Votes
                        .Where(v => v.UserId == user.Id && v.Candidate.Position == request.Position)
                        .ToList();

                    if (votesToRemove.Any())
                    {
                        _context.Votes.RemoveRange(votesToRemove);
                        await _context.SaveChangesAsync();

                        // 🔔 Broadcast update
                        await _hubContext.Clients.All.SendAsync("ReceiveUpdate");

                        return Json(new { success = true, message = "All votes cleared for position" });
                    }
                }

                return Json(new { success = false, message = "No votes found to remove" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error removing vote: " + ex.Message });
            }
        }
        // New method to get voting status
        [HttpGet]
        public IActionResult GetVotingStatus()
        {
            var votingConfig = _context.VotingConfigurations.FirstOrDefault();
            var isOpen = votingConfig?.IsVotingOpen ?? false;

            return Json(new { isVotingOpen = isOpen });
        }
    }

    public class VoteRequest
    {
        public int CandidateId { get; set; }
    }

    public class ClearVoteRequest
    {
        public string Position { get; set; }
        public int? CandidateId { get; set; }
    }
}