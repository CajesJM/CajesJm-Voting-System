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
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<AdminController> _logger;

        public AdminController(VotingDbContext context,
                             IHubContext<DashboardHub> hubContext,
                             IWebHostEnvironment environment,
                             ILogger<AdminController> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _environment = environment;
            _logger = logger;
        }

        // 📊 Dashboard: Show statistics and quick overview - FIXED
        public IActionResult Dashboard()
        {
            try
            {
                var username = User.Identity.Name;
                ViewBag.Username = username;

                // Get active election and its candidates
                var activeElection = _context.Elections.FirstOrDefault(e => e.IsActive);
                var candidates = activeElection != null
                    ? _context.Candidates.Where(c => c.ElectionId == activeElection.Id).ToList()
                    : new List<Candidate>();

                var votingConfig = _context.VotingConfigurations.FirstOrDefault();
                if (votingConfig == null)
                {
                    votingConfig = new VotingConfiguration { IsVotingOpen = false };
                    _context.VotingConfigurations.Add(votingConfig);
                    _context.SaveChanges();
                }

                var positionSettings = _context.PositionSettings?
                    .ToDictionary(ps => ps.PositionName, ps => ps.VotesAllowed)
                    ?? new Dictionary<string, int>();

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

                // FIXED: Count total votes correctly
                var totalVotesCast = activeElection != null
                    ? _context.Votes.Count(v => v.ElectionId == activeElection.Id)
                    : 0;

                // FIXED: Count unique users who voted in active election
                var votedUsersInActiveElection = activeElection != null
                    ? _context.Votes
                          .Where(v => v.ElectionId == activeElection.Id)
                          .Select(v => v.UserId)
                          .Distinct()
                          .Count()
                    : 0;

                var totalApprovedUsers = _context.Users.Count(u => u.IsApproved);

                var stats = new
                {
                    TotalCandidates = candidates.Count,
                    TotalVotes = totalVotesCast, // This now correctly counts all individual votes
                    TotalUsers = totalApprovedUsers,
                    PendingApprovals = _context.Users.Count(u => !u.IsApproved),
                    VotedUsers = votedUsersInActiveElection,
                    VotePercentage = totalApprovedUsers > 0 ?
                        (votedUsersInActiveElection / (double)totalApprovedUsers) * 100 : 0
                };

                ViewBag.TotalVotes = stats.TotalVotes;
                ViewBag.PendingApprovals = stats.PendingApprovals;
                ViewBag.VotePercentage = stats.VotePercentage;
                ViewBag.VotingStatus = votingConfig.IsVotingOpen ? "Open" : "Closed";
                ViewBag.PositionSettings = positionSettings;
                ViewBag.ActiveElection = activeElection?.Name ?? "No active election";
                ViewBag.ActiveElectionId = activeElection?.Id;

                // DEBUG: Log vote information for troubleshooting
                _logger.LogInformation("Dashboard Stats - TotalVotes: {TotalVotes}, VotedUsers: {VotedUsers}, ActiveElection: {ActiveElection}",
                    stats.TotalVotes, stats.VotedUsers, activeElection?.Name ?? "None");

                return View("Dashboard", candidates);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading dashboard");

                ViewBag.TotalVotes = 0;
                ViewBag.PendingApprovals = 0;
                ViewBag.VotePercentage = 0;
                ViewBag.VotingStatus = "Closed";
                ViewBag.PositionSettings = new Dictionary<string, int>();
                ViewBag.ActiveElection = "No active election";

                return View("Dashboard", new List<Candidate>());
            }
        }
        [HttpGet]
        public IActionResult CheckVoteData()
        {
            try
            {
                var activeElection = _context.Elections.FirstOrDefault(e => e.IsActive);

                if (activeElection == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "No active election found",
                        totalVotes = 0,
                        hasActiveElection = false
                    });
                }

                // Get vote records without using VotedAt property
                var voteRecords = _context.Votes
                    .Where(v => v.ElectionId == activeElection.Id)
                    .Select(v => new {
                        v.Id,
                        v.UserId,
                        v.CandidateId,
                        v.ElectionId
                        // Remove VotedAt if it doesn't exist in your model
                    })
                    .ToList();

                var voteData = new
                {
                    success = true,
                    activeElection = new
                    {
                        id = activeElection.Id,
                        name = activeElection.Name,
                        isActive = activeElection.IsActive
                    },
                    totalVotes = _context.Votes.Count(v => v.ElectionId == activeElection.Id),
                    uniqueVoters = _context.Votes.Where(v => v.ElectionId == activeElection.Id).Select(v => v.UserId).Distinct().Count(),
                    voteRecords = voteRecords,
                    candidates = _context.Candidates
                        .Where(c => c.ElectionId == activeElection.Id)
                        .Select(c => new { c.Id, c.Name, c.Position, c.VoteCount })
                        .ToList()
                };

                return Json(voteData);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message,
                    totalVotes = 0
                });
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user != null)
            {
                _context.Users.Remove(user);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"{user.Username} has been deleted successfully.";
            }
            else
            {
                TempData["ErrorMessage"] = "User not found.";
            }

            return RedirectToAction("VoterList");
        }

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
            var allUsersQuery = _context.Users.Where(u => u.IsApproved);

            if (!string.IsNullOrEmpty(courseFilter))
            {
                allUsersQuery = allUsersQuery.Where(u => u.Course == courseFilter);
            }

            var allUsers = allUsersQuery.ToList();

            var regularVoters = allUsers.Where(u => u.Role != "Admin").ToList();
            var totalVoters = regularVoters.Count;
            var votedCount = regularVoters.Count(u => u.HasVoted);
            var pendingCount = _context.Users.Count(u => !u.IsApproved);
            var adminCount = allUsers.Count(u => u.Role == "Admin");

            ViewBag.Courses = _context.Users
                .Where(u => u.IsApproved && !string.IsNullOrEmpty(u.Course))
                .Select(u => u.Course)
                .Distinct()
                .ToList();

            ViewBag.TotalVoters = totalVoters;
            ViewBag.VotedCount = votedCount;
            ViewBag.PendingCount = pendingCount;
            ViewBag.AdminCount = adminCount;
            ViewBag.TotalDisplayUsers = allUsers.Count;
            ViewBag.CourseFilter = courseFilter;

            return View("VoterList", allUsers);
        }

        // 📊 Voting Statistics
        public IActionResult VotingStatistics()
        {
            // Count only users with "User" role (exclude admins)
            var totalUsers = _context.Users.Count(u => u.IsApproved && u.Role == "User");
            var votedUsers = _context.Users.Count(u => u.IsApproved && u.Role == "User" && u.HasVoted);

            var votePercentage = totalUsers > 0 ? (votedUsers / (double)totalUsers) * 100 : 0;

            // Percentage by course (only count User role)
            var courseStats = _context.Users
                .Where(u => u.IsApproved && u.Role == "User" && !string.IsNullOrEmpty(u.Course))
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

        // Candidate Management - UPDATED
        public IActionResult Candidates()
        {
            // Show candidates for active election only
            var activeElection = _context.Elections.FirstOrDefault(e => e.IsActive);
            var candidates = activeElection != null
                ? _context.Candidates.Where(c => c.ElectionId == activeElection.Id).ToList()
                : new List<Candidate>();

            return View("Candidates", candidates);
        }

        [HttpGet]
        public IActionResult Create()
        {
            try
            {
                _logger.LogInformation("=== CREATE GET ACTION CALLED ===");

                var activeElection = _context.Elections.FirstOrDefault(e => e.IsActive);
                _logger.LogInformation("Active election check: {ElectionExists}", activeElection != null);

                if (activeElection == null)
                {
                    _logger.LogWarning("No active election - redirecting to Dashboard");
                    TempData["ErrorMessage"] = "No active election found. Please create and start an election first.";
                    return RedirectToAction("Dashboard");
                }

                ViewBag.ActiveElectionName = activeElection.Name;
                ViewBag.ActiveElectionId = activeElection.Id;

                _logger.LogInformation("Rendering Create view for election: {ElectionName}", activeElection.Name);
                return View("Create", new Candidate());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Create GET action");
                TempData["ErrorMessage"] = "Error loading form. Please try again.";
                return RedirectToAction("Dashboard");
            }
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Candidate candidate, IFormFile profilePicture)
        {
            // ✅ PRESERVE: Get active election data for ViewBag
            var activeElection = await _context.Elections.FirstOrDefaultAsync(e => e.IsActive);
            if (activeElection != null)
            {
                ViewBag.ActiveElectionName = activeElection.Name;
                ViewBag.ActiveElectionId = activeElection.Id;
            }

            if (ModelState.IsValid)
            {
                if (activeElection == null)
                {
                    ModelState.AddModelError("", "No active election found. Please start an election first.");
                    return View("Create", candidate);
                }

                candidate.ElectionId = activeElection.Id;
                candidate.VoteCount = 0;

                // Handle profile picture upload
                if (profilePicture != null && profilePicture.Length > 0)
                {
                    // Validate file type
                    var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
                    var fileExtension = Path.GetExtension(profilePicture.FileName).ToLowerInvariant();

                    if (!allowedExtensions.Contains(fileExtension))
                    {
                        ModelState.AddModelError("profilePicture", "Only image files (JPG, PNG, GIF) are allowed.");
                        return View("Create", candidate);
                    }

                    // Validate file size (max 2MB)
                    if (profilePicture.Length > 2 * 1024 * 1024)
                    {
                        ModelState.AddModelError("profilePicture", "File size must be less than 2MB.");
                        return View("Create", candidate);
                    }

                    try
                    {
                        // Create uploads directory if it doesn't exist
                        var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "profiles");
                        if (!Directory.Exists(uploadsFolder))
                        {
                            Directory.CreateDirectory(uploadsFolder);
                        }

                        // Generate unique filename
                        var uniqueFileName = Guid.NewGuid().ToString() + fileExtension;
                        var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                        // Save the file
                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await profilePicture.CopyToAsync(fileStream);
                        }

                        // Save the file path to candidate
                        candidate.ProfilePicture = "/uploads/profiles/" + uniqueFileName;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error uploading profile picture for candidate {CandidateName}", candidate.Name);
                        ModelState.AddModelError("profilePicture", "Error uploading file. Please try again.");
                        return View("Create", candidate);
                    }
                }
                else
                {
                    candidate.ProfilePicture = "/images/default-avatar.png";
                }

                _context.Candidates.Add(candidate);
                await _context.SaveChangesAsync();

                await _hubContext.Clients.All.SendAsync("ReceiveUpdate");

                TempData["SuccessMessage"] = $"Candidate '{candidate.Name}' added successfully to {activeElection.Name}!";
                return RedirectToAction("Dashboard");
            }

            // ✅ FIXED: Return the Create view with preserved ViewBag data
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
        public async Task<IActionResult> EditCandidate(int id, Candidate candidate, IFormFile profilePicture)
        {
            if (id != candidate.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var existingCandidate = await _context.Candidates.FindAsync(id);
                    if (existingCandidate == null)
                    {
                        return NotFound();
                    }

                    // Handle profile picture upload
                    if (profilePicture != null && profilePicture.Length > 0)
                    {
                        // Validate file type
                        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
                        var fileExtension = Path.GetExtension(profilePicture.FileName).ToLowerInvariant();

                        if (!allowedExtensions.Contains(fileExtension))
                        {
                            ModelState.AddModelError("profilePicture", "Only image files (JPG, PNG, GIF) are allowed.");
                            return View("Edit", candidate);
                        }

                        // Validate file size (max 2MB)
                        if (profilePicture.Length > 2 * 1024 * 1024)
                        {
                            ModelState.AddModelError("profilePicture", "File size must be less than 2MB.");
                            return View("Edit", candidate);
                        }

                        try
                        {
                            // Get web root path
                            var webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                            var uploadsFolder = Path.Combine(webRootPath, "uploads", "profiles");

                            if (!Directory.Exists(uploadsFolder))
                            {
                                Directory.CreateDirectory(uploadsFolder);
                            }

                            // Generate unique filename
                            var uniqueFileName = Guid.NewGuid().ToString() + fileExtension;
                            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                            // Save the file
                            using (var fileStream = new FileStream(filePath, FileMode.Create))
                            {
                                await profilePicture.CopyToAsync(fileStream);
                            }

                            // Delete old profile picture if it exists and isn't default
                            if (!string.IsNullOrEmpty(existingCandidate.ProfilePicture) &&
                                !existingCandidate.ProfilePicture.Contains("default-avatar"))
                            {
                                var oldFilePath = Path.Combine(webRootPath, existingCandidate.ProfilePicture.TrimStart('/'));
                                if (System.IO.File.Exists(oldFilePath))
                                {
                                    System.IO.File.Delete(oldFilePath);
                                }
                            }

                            // Update profile picture path
                            existingCandidate.ProfilePicture = "/uploads/profiles/" + uniqueFileName;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error uploading file: {ex.Message}");
                        }
                    }

                    // Update other properties
                    existingCandidate.Name = candidate.Name;
                    existingCandidate.Position = candidate.Position;
                    existingCandidate.Description = candidate.Description;
                    existingCandidate.PartyList = candidate.PartyList;

                    _context.Candidates.Update(existingCandidate);
                    await _context.SaveChangesAsync();

                    await _hubContext.Clients.All.SendAsync("ReceiveUpdate");

                    return RedirectToAction("Dashboard");
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", "Error updating candidate: " + ex.Message);
                }
            }

            return View("Edit", candidate);
        }

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

        // Election Management
        public IActionResult ElectionManagement()
        {
            try
            {
                var elections = _context.Elections
                    .Include(e => e.Candidates) // Include candidates to show counts
                    .OrderByDescending(e => e.StartDate)
                    .ToList();
                return View(elections);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading election management");
                TempData["ErrorMessage"] = "Error loading election management";
                return View(new List<Election>());
            }
        }

        [HttpGet]
        public IActionResult CreateElection()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateElection(Election election)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    _context.Elections.Add(election);
                    await _context.SaveChangesAsync();

                    var newElectionId = election.Id;

                    TempData["SuccessMessage"] = $"Election created successfully! ID: {newElectionId}";
                    return RedirectToAction("ElectionManagement");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating election");
                    ModelState.AddModelError("", "Error creating election: " + ex.Message);
                }
            }
            return View(election);
        }

        [HttpPost]
        public async Task<IActionResult> StartNewElection([FromBody] StartElectionRequest request)
        {
            try
            {
                // If request is null, try to get electionId from form data
                if (request == null)
                {
                    var formElectionId = Request.Form["electionId"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(formElectionId) && int.TryParse(formElectionId, out int formId))
                    {
                        request = new StartElectionRequest { ElectionId = formId };
                    }
                }

                if (request == null || request.ElectionId <= 0)
                {
                    _logger.LogWarning("Invalid election ID received");
                    return Json(new { success = false, message = "Invalid election ID" });
                }

                var electionId = request.ElectionId;

                var election = await _context.Elections
                    .Where(e => e.Id == electionId)
                    .FirstOrDefaultAsync();

                if (election == null)
                {
                    return Json(new { success = false, message = $"Election with ID {electionId} not found." });
                }

                // End current active election if any
                var currentActiveElection = await _context.Elections.FirstOrDefaultAsync(e => e.IsActive);
                if (currentActiveElection != null)
                {
                    currentActiveElection.IsActive = false;
                    currentActiveElection.IsCompleted = true;
                    currentActiveElection.EndDate = DateTime.Now;
                    await CalculateElectionResults(currentActiveElection.Id);
                }

                // Start the new election
                election.IsActive = true;
                election.StartDate = DateTime.Now;

                // Reset user voting status
                var users = await _context.Users.Where(u => u.HasVoted).ToListAsync();
                foreach (var user in users)
                {
                    user.HasVoted = false;
                    user.LastVotedElectionDate = DateTime.Now;
                }

                await _context.SaveChangesAsync();
                await _hubContext.Clients.All.SendAsync("ReceiveUpdate");

                return Json(new
                {
                    success = true,
                    message = $"Election '{election.Name}' started successfully!",
                    candidateCount = await _context.Candidates.CountAsync(c => c.ElectionId == electionId)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting election");
                return Json(new { success = false, message = $"Error starting election: {ex.Message}" });
            }
        }

        public class StartElectionRequest
        {
            public int ElectionId { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> EndElection([FromBody] EndElectionRequest request)
        {
            try
            {
                Console.WriteLine($"=== EndElection Called ===");
                Console.WriteLine($"Received ElectionId: {request?.ElectionId}");

                if (request == null || request.ElectionId <= 0)
                {
                    return Json(new { success = false, message = "Invalid election ID" });
                }

                var electionId = request.ElectionId;

                var election = await _context.Elections
                    .Include(e => e.Candidates)
                    .FirstOrDefaultAsync(e => e.Id == electionId);

                if (election == null)
                {
                    _logger.LogWarning("Election with ID {ElectionId} not found", electionId);
                    return Json(new { success = false, message = "Election not found." });
                }

                Console.WriteLine($"✅ Found election: {election.Name}");

                // Mark election as completed
                election.IsActive = false;
                election.IsCompleted = true;
                election.EndDate = DateTime.Now;

                // Calculate and save election results
                await CalculateElectionResults(electionId);

                await _context.SaveChangesAsync();

                // Close voting for this election
                await CloseVoting();

                _logger.LogInformation("Election {ElectionName} (ID: {ElectionId}) ended successfully", election.Name, election.Id);

                return Json(new
                {
                    success = true,
                    message = $"Election '{election.Name}' ended successfully! Results have been calculated."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ending election with ID {ElectionId}", request?.ElectionId);
                return Json(new { success = false, message = $"Error ending election: {ex.Message}" });
            }
        }

        // Add this class definition near your other request classes
        public class EndElectionRequest
        {
            public int ElectionId { get; set; }
        }

        private async Task CalculateElectionResults(int electionId)
        {
            try
            {
                var election = await _context.Elections
                    .Include(e => e.Candidates)
                    .FirstOrDefaultAsync(e => e.Id == electionId);

                if (election == null) return;

                // Clear existing results for this election
                var existingResults = _context.ElectionResults.Where(er => er.ElectionId == electionId);
                _context.ElectionResults.RemoveRange(existingResults);

                var positions = election.Candidates.Select(c => c.Position).Distinct();

                foreach (var position in positions)
                {
                    var candidates = election.Candidates
                        .Where(c => c.Position == position)
                        .OrderByDescending(c => c.VoteCount)
                        .ToList();

                    var totalVotes = candidates.Sum(c => c.VoteCount);
                    var winner = candidates.FirstOrDefault();

                    if (winner != null)
                    {
                        var result = new ElectionResult
                        {
                            ElectionId = electionId,
                            Position = position,
                            WinnerName = winner.Name,
                            WinnerVotes = winner.VoteCount,
                            TotalVotes = totalVotes,
                            WinnerPercentage = totalVotes > 0 ? (winner.VoteCount * 100.0m) / totalVotes : 0,
                            RunnerUpName = candidates.Count > 1 ? candidates[1].Name : "None",
                            RunnerUpVotes = candidates.Count > 1 ? candidates[1].VoteCount : 0,
                            ResultDate = DateTime.Now
                        };

                        _context.ElectionResults.Add(result);
                    }
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating election results");
                throw;
            }
        }

     
        public IActionResult ElectionHistory()
        {
            try
            {
                var pastElections = _context.Elections
                    .Where(e => e.IsCompleted)
                    .Include(e => e.Results)
                    .Include(e => e.Candidates)
                    .OrderByDescending(e => e.EndDate)
                    .ToList();

                return View(pastElections);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading election history");
                TempData["ErrorMessage"] = "Error loading election history";
                return View(new List<Election>());
            }
        }

        // Download Election Report
        public async Task<IActionResult> DownloadElectionReport(int electionId)
        {
            try
            {
                var election = await _context.Elections
                    .Include(e => e.Results)
                    .Include(e => e.Candidates)
                    .FirstOrDefaultAsync(e => e.Id == electionId);

                if (election == null)
                {
                    TempData["ErrorMessage"] = "Election not found";
                    return RedirectToAction("ElectionHistory");
                }

                // Generate text report
                var reportContent = GenerateTextReport(election);
                var bytes = System.Text.Encoding.UTF8.GetBytes(reportContent);

                return File(bytes, "text/plain",
                    $"{election.Name.Replace(" ", "_")}_Report_{DateTime.Now:yyyyMMdd}.txt");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating election report");
                TempData["ErrorMessage"] = "Error generating report";
                return RedirectToAction("ElectionHistory");
            }
        }

        private string GenerateTextReport(Election election)
        {
            var report = new System.Text.StringBuilder();

            report.AppendLine($"ELECTION REPORT: {election.Name}");
            report.AppendLine($"Period: {election.StartDate:MMMM dd, yyyy} to {election.EndDate:MMMM dd, yyyy}");
            report.AppendLine($"Status: {(election.IsCompleted ? "COMPLETED" : "ACTIVE")}");
            report.AppendLine();
            report.AppendLine($"Total Candidates: {election.Candidates.Count}");
            report.AppendLine($"Total Votes Cast: {election.Candidates.Sum(c => c.VoteCount)}");
            report.AppendLine();
            report.AppendLine("RESULTS BY POSITION:");
            report.AppendLine("====================");

            foreach (var result in election.Results.OrderBy(r => r.Position))
            {
                report.AppendLine();
                report.AppendLine($"Position: {result.Position}");
                report.AppendLine($"Winner: {result.WinnerName}");
                report.AppendLine($"Votes: {result.WinnerVotes} ({result.WinnerPercentage:0.0}%)");
                report.AppendLine($"Runner-up: {result.RunnerUpName} ({result.RunnerUpVotes} votes)");
                report.AppendLine($"Total Votes for Position: {result.TotalVotes}");
            }

            report.AppendLine();
            report.AppendLine("CANDIDATE DETAILS:");
            report.AppendLine("==================");

            foreach (var position in election.Candidates.Select(c => c.Position).Distinct())
            {
                report.AppendLine();
                report.AppendLine($"{position}:");
                var positionCandidates = election.Candidates
                    .Where(c => c.Position == position)
                    .OrderByDescending(c => c.VoteCount);

                foreach (var candidate in positionCandidates)
                {
                    var totalPositionVotes = election.Candidates
                        .Where(c => c.Position == position)
                        .Sum(c => c.VoteCount);
                    var percentage = totalPositionVotes > 0 ? (candidate.VoteCount * 100.0) / totalPositionVotes : 0;
                    report.AppendLine($"  - {candidate.Name}: {candidate.VoteCount} votes ({percentage:0.0}%)");
                }
            }

            report.AppendLine();
            report.AppendLine($"Report generated on: {DateTime.Now:MMMM dd, yyyy 'at' hh:mm tt}");

            return report.ToString();
        }

        [HttpPost]
        public async Task<IActionResult> DebugElectionResults(int electionId)
        {
            try
            {
                var election = await _context.Elections
                    .Include(e => e.Candidates)
                    .Include(e => e.Results)
                    .FirstOrDefaultAsync(e => e.Id == electionId);

                if (election == null)
                {
                    return Json(new { success = false, message = "Election not found" });
                }

                var debugInfo = new
                {
                    ElectionId = election.Id,
                    ElectionName = election.Name,
                    IsActive = election.IsActive,
                    IsCompleted = election.IsCompleted,
                    EndDate = election.EndDate,
                    CandidateCount = election.Candidates.Count,
                    ResultsCount = election.Results.Count,
                    Candidates = election.Candidates.Select(c => new
                    {
                        c.Id,
                        c.Name,
                        c.Position,
                        c.VoteCount,
                        c.ElectionId
                    }).ToList(),
                    Results = election.Results.Select(r => new
                    {
                        r.Position,
                        r.WinnerName,
                        r.WinnerVotes
                    }).ToList()
                };

                return Json(new { success = true, data = debugInfo });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteElection([FromBody] DeleteElectionRequest request)
        {
            try
            {
                _logger.LogInformation("DeleteElection called with ElectionId: {ElectionId}", request?.ElectionId);

                if (request == null || request.ElectionId <= 0)
                {
                    return Json(new { success = false, message = "Invalid request data" });
                }

                var electionId = request.ElectionId;

                var election = await _context.Elections
                    .Include(e => e.Candidates)
                    .Include(e => e.Votes)
                    .Include(e => e.Results)
                    .FirstOrDefaultAsync(e => e.Id == electionId);

                if (election == null)
                {
                    return Json(new { success = false, message = $"Election with ID {electionId} not found" });
                }

                // Check if this is the active election
                if (election.IsActive)
                {
                    return Json(new { success = false, message = "Cannot delete an active election. Please end the election first." });
                }

             
                _context.Candidates.RemoveRange(election.Candidates);
                _context.Votes.RemoveRange(election.Votes);
                _context.ElectionResults.RemoveRange(election.Results);

                
                _context.Elections.Remove(election);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Election {ElectionName} (ID: {ElectionId}) deleted successfully", election.Name, election.Id);

                return Json(new { success = true, message = $"Election '{election.Name}' deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting election with ID {ElectionId}", request?.ElectionId);
                return Json(new { success = false, message = $"Error deleting election: {ex.Message}" });
            }
        }

        public class DeleteElectionRequest
        {
            public int ElectionId { get; set; }
        }
        [HttpPost]
        public IActionResult TestDeleteEndpoint([FromBody] TestRequest request)
        {
            _logger.LogInformation("Test endpoint called with: {@Request}", request);
            return Json(new
            {
                success = true,
                message = "Test endpoint working",
                receivedId = request?.ElectionId
            });
        }

        public class TestRequest
        {
            public int ElectionId { get; set; }
        }
        public IActionResult ManageElectionCandidates(int electionId)
        {
            TempData["ScrollToCandidates"] = true;
            TempData["SelectedElectionId"] = electionId;

            return RedirectToAction("Dashboard");
        }
       

    }
}