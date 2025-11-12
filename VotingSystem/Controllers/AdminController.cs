using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VotingSystem.Hubs;
using VotingSystem.Models;

namespace VotingSystem.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly VotingDbContext _context;
        private readonly IHubContext<DashboardHub> _hubContext;

        public AdminController(VotingDbContext context, IHubContext<DashboardHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        // 📊 Dashboard: Show statistics and quick overview
        public IActionResult Dashboard()
        {
            try
            {
                var candidates = _context.Candidates.ToList();

                // Get voting configuration
                var votingConfig = _context.VotingConfigurations.FirstOrDefault();
                if (votingConfig == null)
                {
                    // Create default if doesn't exist
                    votingConfig = new VotingConfiguration { IsVotingOpen = false };
                    _context.VotingConfigurations.Add(votingConfig);
                    _context.SaveChanges();
                }

                // Get position settings
                var positionSettings = _context.PositionSettings?
                    .ToDictionary(ps => ps.PositionName, ps => ps.VotesAllowed)
                    ?? new Dictionary<string, int>();

                // If no position settings exist, create defaults from candidates
                if (!positionSettings.Any() && candidates.Any())
                {
                    var positions = candidates.Select(c => c.Position).Distinct();
                    foreach (var position in positions)
                    {
                        var setting = new PositionSetting
                        {
                            PositionName = position,
                            VotesAllowed = 1
                        };
                        _context.PositionSettings.Add(setting);
                    }
                    _context.SaveChanges();
                    positionSettings = _context.PositionSettings.ToDictionary(ps => ps.PositionName, ps => ps.VotesAllowed);
                }

                var stats = new
                {
                    TotalCandidates = candidates.Count,
                    TotalVotes = _context.Votes.Count(),
                    TotalUsers = _context.Users.Count(u => u.IsApproved),
                    PendingApprovals = _context.Users.Count(u => !u.IsApproved),
                    VotedUsers = _context.Users.Count(u => u.HasVoted && u.IsApproved),
                    VotePercentage = _context.Users.Count(u => u.IsApproved) > 0 ?
                        (_context.Users.Count(u => u.HasVoted && u.IsApproved) / (double)_context.Users.Count(u => u.IsApproved)) * 100 : 0
                };

                ViewBag.TotalVotes = stats.TotalVotes;
                ViewBag.PendingApprovals = stats.PendingApprovals;
                ViewBag.VotePercentage = stats.VotePercentage;
                ViewBag.VotingStatus = votingConfig.IsVotingOpen ? "Open" : "Closed";
                ViewBag.PositionSettings = positionSettings;

                return View("Dashboard", candidates);
            }
            catch (Exception ex)
            {
                // Fallback if database has issues
                ViewBag.TotalVotes = 0;
                ViewBag.PendingApprovals = 0;
                ViewBag.VotePercentage = 0;
                ViewBag.VotingStatus = "Closed";
                ViewBag.PositionSettings = new Dictionary<string, int>();

                return View("Dashboard", new List<Candidate>());
            }
        }

        // 🗳️ Voting Management Methods
        [HttpPost]
        public async Task<IActionResult> OpenVoting()
        {
            try
            {
                var votingConfig = await _context.VotingConfigurations.FirstOrDefaultAsync();
                if (votingConfig == null)
                {
                    votingConfig = new VotingConfiguration { IsVotingOpen = true };
                    _context.VotingConfigurations.Add(votingConfig);
                }
                else
                {
                    votingConfig.IsVotingOpen = true;
                    votingConfig.LastModified = DateTime.Now;
                    _context.VotingConfigurations.Update(votingConfig);
                }

                await _context.SaveChangesAsync();

                // 🔔 Broadcast update to all users
                await _hubContext.Clients.All.SendAsync("VotingStatusChanged", "Open");

                return Json(new { success = true, message = "Voting has been opened successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error opening voting: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CloseVoting()
        {
            try
            {
                var votingConfig = await _context.VotingConfigurations.FirstOrDefaultAsync();
                if (votingConfig == null)
                {
                    votingConfig = new VotingConfiguration { IsVotingOpen = false };
                    _context.VotingConfigurations.Add(votingConfig);
                }
                else
                {
                    votingConfig.IsVotingOpen = false;
                    votingConfig.LastModified = DateTime.Now;
                    _context.VotingConfigurations.Update(votingConfig);
                }

                await _context.SaveChangesAsync();

                // 🔔 Broadcast update to all users
                await _hubContext.Clients.All.SendAsync("VotingStatusChanged", "Closed");

                return Json(new { success = true, message = "Voting has been closed successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error closing voting: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SaveVotingConfig([FromBody] Dictionary<string, int> configs)
        {
            try
            {
                if (configs == null || !configs.Any())
                {
                    return Json(new { success = false, message = "No configuration data provided." });
                }

                foreach (var config in configs)
                {
                    var positionName = config.Key;
                    var votesAllowed = config.Value;

                    var existingSetting = await _context.PositionSettings
                        .FirstOrDefaultAsync(ps => ps.PositionName == positionName);

                    if (existingSetting != null)
                    {
                        existingSetting.VotesAllowed = votesAllowed;
                        _context.PositionSettings.Update(existingSetting);
                    }
                    else
                    {
                        var newSetting = new PositionSetting
                        {
                            PositionName = positionName,
                            VotesAllowed = votesAllowed
                        };
                        _context.PositionSettings.Add(newSetting);
                    }
                }

                await _context.SaveChangesAsync();

                // 🔔 Broadcast update to all users
                await _hubContext.Clients.All.SendAsync("ReceiveUpdate");

                return Json(new { success = true, message = "Voting configuration saved successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error saving configuration: {ex.Message}" });
            }
        }

        // 👥 Pending Users Approval
        public IActionResult PendingUsers()
        {
            var pendingUsers = _context.Users
                .Where(u => !u.IsApproved)
                .OrderBy(u => u.CreatedAt)
                .ToList();

            return View("PendingUsers", pendingUsers);
        }

        [HttpPost]
        public async Task<IActionResult> ApproveUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user != null)
            {
                user.IsApproved = true;
                user.Role = user.RequestedRole;
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"{user.Username} has been approved as {user.Role}.";
            }

            return RedirectToAction("PendingUsers");
        }

        [HttpPost]
        public async Task<IActionResult> RejectUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user != null)
            {
                _context.Users.Remove(user);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"{user.Username} has been rejected and removed.";
            }

            return RedirectToAction("PendingUsers");
        }

        // 📋 All Users Management
        public IActionResult AllUsers()
        {
            var users = _context.Users
                .OrderByDescending(u => u.IsApproved)
                .ThenBy(u => u.CreatedAt)
                .ToList();

            return View("AllUsers", users);
        }

        // 👤 Voter List with Filtering
        public IActionResult VoterList(string courseFilter = "")
        {
            var votersQuery = _context.Users.Where(u => u.IsApproved);

            if (!string.IsNullOrEmpty(courseFilter))
            {
                votersQuery = votersQuery.Where(u => u.Course == courseFilter);
            }

            var voters = votersQuery.ToList();
            var totalVoters = voters.Count;
            var votedCount = voters.Count(u => u.HasVoted);

            ViewBag.Courses = _context.Users.Where(u => !string.IsNullOrEmpty(u.Course)).Select(u => u.Course).Distinct().ToList();
            ViewBag.TotalVoters = totalVoters;
            ViewBag.VotedCount = votedCount;
            ViewBag.CourseFilter = courseFilter;

            return View("VoterList", voters);
        }

        // 📊 Voting Statistics
        public IActionResult VotingStatistics()
        {
            var totalUsers = _context.Users.Count(u => u.IsApproved);
            var votedUsers = _context.Users.Count(u => u.IsApproved && u.HasVoted);

            var votePercentage = totalUsers > 0 ? (votedUsers / (double)totalUsers) * 100 : 0;

            // Percentage by course
            var courseStats = _context.Users
                .Where(u => u.IsApproved && !string.IsNullOrEmpty(u.Course))
                .GroupBy(u => u.Course)
                .Select(g => new CourseStat
                {
                    CourseName = g.Key,
                    TotalStudents = g.Count(),
                    VotedStudents = g.Count(u => u.HasVoted),
                    Percentage = g.Count() > 0 ? (g.Count(u => u.HasVoted) / (double)g.Count()) * 100 : 0
                })
                .ToList();

            ViewBag.TotalUsers = totalUsers;
            ViewBag.VotedUsers = votedUsers;
            ViewBag.VotePercentage = Math.Round(votePercentage, 2);

            return View("VotingStatistics", courseStats);
        }

        // Candidate Management (Your existing methods)
        public IActionResult Candidates()
        {
            var candidates = _context.Candidates.ToList();
            return View("Candidates", candidates);
        }

        // Add Candidate (GET)
        public IActionResult Create()
        {
            return View("Create");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Candidate candidate)
        {
            if (ModelState.IsValid)
            {
                candidate.VoteCount = 0;
                _context.Candidates.Add(candidate);
                await _context.SaveChangesAsync();

                // 🔔 Broadcast update to all users
                await _hubContext.Clients.All.SendAsync("ReceiveUpdate");

                return RedirectToAction("Dashboard");
            }

            return View("Create", candidate);
        }

        public IActionResult Edit(int id)
        {
            var candidate = _context.Candidates.Find(id);
            if (candidate == null) return NotFound();
            return View("Edit", candidate);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Candidate candidate)
        {
            if (ModelState.IsValid)
            {
                _context.Candidates.Update(candidate);
                await _context.SaveChangesAsync();

                // 🔔 Broadcast update to all users
                await _hubContext.Clients.All.SendAsync("ReceiveUpdate");

                return RedirectToAction("Dashboard");
            }

            return View("Edit", candidate);
        }

        // ❌ Delete Candidate (GET confirmation)
        public IActionResult Delete(int id)
        {
            var candidate = _context.Candidates.Find(id);
            if (candidate == null) return NotFound();
            return View("Delete", candidate);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var candidate = await _context.Candidates.FindAsync(id);
            if (candidate != null)
            {
                _context.Candidates.Remove(candidate);
                await _context.SaveChangesAsync();

                // 🔔 Broadcast update to all users
                await _hubContext.Clients.All.SendAsync("ReceiveUpdate");
            }

            return RedirectToAction("Dashboard");
        }
        public async Task<IActionResult> CheckVotingStatus()
        {
            var votingConfig = await _context.VotingConfigurations.FirstOrDefaultAsync();

            ViewBag.CurrentStatus = votingConfig?.IsVotingOpen ?? false;
            ViewBag.ConfigId = votingConfig?.Id;
            ViewBag.ConfigExists = votingConfig != null;

            return View();
        }
    }
}