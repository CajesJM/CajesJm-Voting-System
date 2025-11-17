using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using VotingSystem.Hubs;
using VotingSystem.Models;
using Microsoft.Extensions.Logging;

namespace VotingSystem.Controllers
{
    [Authorize(Roles = "User")]
    public class UserController : Controller
    {
        private readonly VotingDbContext _context;
        private readonly IHubContext<DashboardHub> _hubContext;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<UserController> _logger; 

        public UserController(VotingDbContext context, IHubContext<DashboardHub> hubContext, IWebHostEnvironment environment, ILogger<UserController> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _environment = environment;
            _logger = logger; 
        }
        public IActionResult Dashboard()
        {
            try
            {
                var username = User.Identity.Name;
                var user = _context.Users.FirstOrDefault(u => u.Username == username);

                if (user != null)
                {
                    ViewBag.Username = user.Username;
                    ViewBag.UserEmail = user.Email ?? "Not provided";
                    ViewBag.UserCourse = user.Course ?? "Not specified";
                    ViewBag.UserRole = user.Role ?? "User";
                    ViewBag.HasVoted = user.HasVoted;
                    ViewBag.ProfilePicture = user.ProfilePicture ?? "";

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
                    ViewBag.ProfilePicture = "/images/default-avatar.png";
                    ViewBag.UserVotes = new List<Vote>();
                }

                var activeElection = _context.Elections.FirstOrDefault(e => e.IsActive);
                var candidatesByPosition = activeElection != null
                    ? _context.Candidates
                        .Where(c => c.ElectionId == activeElection.Id)
                        .Include(c => c.Votes)
                        .GroupBy(c => c.Position)
                        .ToDictionary(g => g.Key, g => g.ToList())
                    : new Dictionary<string, List<Candidate>>();

                ViewBag.CandidatesByPosition = candidatesByPosition;
                ViewBag.ActiveElection = activeElection?.Name ?? "No Active Election";

                var votingConfig = _context.VotingConfigurations.FirstOrDefault() ?? new VotingConfiguration();

                var currentPositions = candidatesByPosition.Keys.ToList();
                var positionSettings = _context.PositionSettings
                    .Where(ps => currentPositions.Contains(ps.PositionName))
                    .ToDictionary(ps => ps.PositionName, ps => ps.VotesAllowed);

                ViewBag.VotingStatus = votingConfig.IsVotingOpen ? "Open" : "Closed";
                ViewBag.IsVotingOpen = votingConfig.IsVotingOpen;
                ViewBag.PositionSettings = positionSettings;

                var electionHistory = _context.Elections
                    .Where(e => e.IsCompleted) 
                    .Include(e => e.Candidates) 
                    .OrderByDescending(e => e.EndDate)
                    .Take(10) 
                    .ToList();

                ViewBag.ElectionHistory = electionHistory;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading user dashboard");

                ViewBag.Username = User.Identity.Name ?? "User";
                ViewBag.UserEmail = "Not available";
                ViewBag.UserCourse = "Not available";
                ViewBag.UserRole = "User";
                ViewBag.HasVoted = false;
                ViewBag.ProfilePicture = "/images/default-avatar.png";
                ViewBag.UserVotes = new List<Vote>();
                ViewBag.CandidatesByPosition = new Dictionary<string, List<Candidate>>();
                ViewBag.ActiveElection = "No Active Election";
                ViewBag.VotingStatus = "Closed";
                ViewBag.IsVotingOpen = false;
                ViewBag.PositionSettings = new Dictionary<string, int>();
                ViewBag.ElectionHistory = new List<Election>();

                return View();
            }
        }


        [HttpPost]
        public async Task<IActionResult> UpdateUserProfile(string Username, string Email, IFormFile ProfilePicture)
        {
            try
            {
                var username = User.Identity.Name;
                var user = _context.Users.FirstOrDefault(u => u.Username == username);

                if (user == null)
                {
                    return Json(new { success = false, message = "User not found" });
                }

                if (string.IsNullOrWhiteSpace(Username) || Username.Length < 3)
                {
                    return Json(new { success = false, message = "Username must be at least 3 characters long" });
                }

                var existingUser = _context.Users.FirstOrDefault(u => u.Username == Username && u.Id != user.Id);
                if (existingUser != null)
                {
                    return Json(new { success = false, message = "Username is already taken" });
                }

                if (string.IsNullOrWhiteSpace(Email) || !Email.Contains("@"))
                {
                    return Json(new { success = false, message = "Please enter a valid email address" });
                }

              
                user.Username = Username.Trim();
                user.Email = Email.Trim();

              
                if (ProfilePicture != null && ProfilePicture.Length > 0)
                {
                  
                    var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                    var fileExtension = Path.GetExtension(ProfilePicture.FileName).ToLower();
                    if (!allowedExtensions.Contains(fileExtension))
                    {
                        return Json(new { success = false, message = "Invalid file type. Please upload JPG, PNG, GIF, or WebP images only." });
                    }

                    if (ProfilePicture.Length > 2 * 1024 * 1024)
                    {
                        return Json(new { success = false, message = "File size must be less than 2MB" });
                    }

               
                    var fileName = $"{Guid.NewGuid()}{fileExtension}";
                    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "profiles");

                 
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    var filePath = Path.Combine(uploadsFolder, fileName);

                 
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await ProfilePicture.CopyToAsync(stream);
                    }

                    user.ProfilePicture = $"/uploads/profiles/{fileName}";
                }

                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                if (username != user.Username)
                {
                    await HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        new ClaimsPrincipal(new ClaimsIdentity(
                            new[] { new Claim(ClaimTypes.Name, user.Username) },
                            CookieAuthenticationDefaults.AuthenticationScheme)),
                        new AuthenticationProperties { IsPersistent = true });
                }

                return Json(new
                {
                    success = true,
                    message = "Profile updated successfully",
                    profilePicture = user.ProfilePicture
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user profile");
                return Json(new { success = false, message = "An error occurred while updating your profile" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> VoteForPosition([FromBody] VoteRequest request)
        {
            try
            {
                // Check if voting is open
                var votingConfig = await _context.VotingConfigurations.FirstOrDefaultAsync();
                if (votingConfig == null || !votingConfig.IsVotingOpen)
                {
                    return Json(new { success = false, message = "Voting is currently closed. You cannot cast votes at this time." });
                }

                var username = User.Identity.Name;
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

                if (user == null)
                    return Json(new { success = false, message = "User not found." });

                if (user.HasVoted)
                    return Json(new { success = false, message = "You have already submitted your final vote." });

                // Get the candidate
                var candidate = await _context.Candidates.FindAsync(request.CandidateId);
                if (candidate == null)
                    return Json(new { success = false, message = "Candidate not found." });

                // Check if user has already voted for this specific candidate
                var existingVote = await _context.Votes
                    .FirstOrDefaultAsync(v => v.UserId == user.Id && v.CandidateId == request.CandidateId);

                if (existingVote != null)
                {
                    return Json(new
                    {
                        success = false,
                        message = $"You have already voted for {candidate.Name} in {candidate.Position}."
                    });
                }

                // Check position vote limits
                var positionSetting = await _context.PositionSettings
                    .FirstOrDefaultAsync(ps => ps.PositionName == candidate.Position);

                var votesAllowed = positionSetting?.VotesAllowed ?? 1;

                // Get current votes for this position
                var existingVotesForPosition = await _context.Votes
                    .Where(v => v.UserId == user.Id)
                    .Join(_context.Candidates,
                          v => v.CandidateId,
                          c => c.Id,
                          (v, c) => new { Vote = v, Candidate = c })
                    .Where(x => x.Candidate.Position == candidate.Position)
                    .Select(x => x.Vote)
                    .ToListAsync();

                // If user has reached the vote limit for this position, remove the oldest vote
                if (existingVotesForPosition.Count >= votesAllowed)
                {
                    var oldestVote = existingVotesForPosition.OrderBy(v => v.Timestamp).First();
                    _context.Votes.Remove(oldestVote);
                    await _context.SaveChangesAsync();
                }

                // Add new vote
                var vote = new Vote
                {
                    UserId = user.Id,
                    CandidateId = request.CandidateId,
                    Timestamp = DateTime.Now,
                    IsFinal = false,
                    ElectionId = candidate.ElectionId
                };

                _context.Votes.Add(vote);
                await _context.SaveChangesAsync();

                // 🔔 Broadcast update
                await _hubContext.Clients.All.SendAsync("ReceiveUpdate");

                return Json(new
                {
                    success = true,
                    message = $"Vote for {candidate.Name} ({candidate.Position}) recorded!",
                    position = candidate.Position,
                    candidateName = candidate.Name,
                    candidateId = candidate.Id
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"An error occurred: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SubmitBallot()
        {
            try
            {
                // Check if voting is open
                var votingConfig = await _context.VotingConfigurations.FirstOrDefaultAsync();
                if (votingConfig == null || !votingConfig.IsVotingOpen)
                {
                    return BadRequest(new { message = "Voting is currently closed. You cannot submit your ballot at this time." });
                }

                var username = User.Identity.Name;
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

                if (user == null)
                    return Unauthorized(new { message = "User not found." });

                if (user.HasVoted)
                    return BadRequest(new { message = "You have already submitted your ballot." });

                // Get user's current votes
                var userVotes = await _context.Votes
                    .Where(v => v.UserId == user.Id)
                    .Include(v => v.Candidate)
                    .ToListAsync();

                var activeElection = await _context.Elections.FirstOrDefaultAsync(e => e.IsActive);
                if (activeElection == null)
                    return BadRequest(new { message = "No active election found." });

                // CRITICAL FIX: Get ONLY positions that have candidates in the CURRENT active election
                var positionsWithCandidates = await _context.Candidates
                    .Where(c => c.ElectionId == activeElection.Id)
                    .Select(c => c.Position)
                    .Distinct()
                    .ToListAsync();

                Console.WriteLine($"=== DEBUG: Active election: {activeElection.Name}");
                Console.WriteLine($"=== DEBUG: Positions with candidates: {string.Join(", ", positionsWithCandidates)}");

                // If no positions with candidates, allow submission immediately
                if (!positionsWithCandidates.Any())
                {
                    user.HasVoted = true;
                    user.LastVotedElectionDate = DateTime.Now;
                    await _context.SaveChangesAsync();
                    await _hubContext.Clients.All.SendAsync("ReceiveUpdate");
                    return Ok(new { message = "Your ballot has been submitted successfully! No positions were available for voting." });
                }

                var votedPositions = userVotes
                    .Where(v => v.Candidate != null)
                    .Select(v => v.Candidate.Position)
                    .Distinct()
                    .ToList();

                Console.WriteLine($"=== DEBUG: User voted positions: {string.Join(", ", votedPositions)}");

                // CRITICAL FIX: Only check for positions that actually have candidates
                var missingPositions = positionsWithCandidates.Except(votedPositions).ToList();

                Console.WriteLine($"=== DEBUG: Missing positions: {string.Join(", ", missingPositions)}");

                if (missingPositions.Any())
                {
                    return BadRequest(new
                    {
                        message = $"Please vote for all available positions. Missing: {string.Join(", ", missingPositions)}",
                        missingPositions = missingPositions
                    });
                }

                // Check vote limits for each position
                var positionViolations = new List<string>();
                foreach (var position in votedPositions)
                {
                    var positionVotes = userVotes.Count(v => v.Candidate?.Position == position);
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

                // Finalize votes
                foreach (var vote in userVotes)
                {
                    vote.IsFinal = true;
                    if (vote.Candidate != null)
                    {
                        vote.Candidate.VoteCount += 1;
                    }
                }

                user.HasVoted = true;
                user.LastVotedElectionDate = DateTime.Now;
                await _context.SaveChangesAsync();

                await _hubContext.Clients.All.SendAsync("ReceiveUpdate");

                return Ok(new { message = "Your ballot has been submitted successfully! Thank you for voting." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ballot submission error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return StatusCode(500, new { message = $"An error occurred: {ex.Message}" });
            }
        }
        [HttpPost]
        public async Task<IActionResult> ClearVote([FromBody] ClearVoteRequest request)
        {
            try
            {
                var username = User.Identity.Name;
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

                if (user == null)
                    return Json(new { success = false, message = "User not found" });

                // If candidateId is provided and valid, remove only that candidate vote
                if (request.CandidateId.HasValue && request.CandidateId.Value > 0)
                {
                    var voteToRemove = await _context.Votes
                        .FirstOrDefaultAsync(v => v.UserId == user.Id && v.CandidateId == request.CandidateId.Value);

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
                    // Remove all votes for the position
                    var votesToRemove = await _context.Votes
                        .Where(v => v.UserId == user.Id)
                        .Join(_context.Candidates,
                              v => v.CandidateId,
                              c => c.Id,
                              (v, c) => new { Vote = v, Candidate = c })
                        .Where(x => x.Candidate.Position == request.Position)
                        .Select(x => x.Vote)
                        .ToListAsync();

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

        [HttpGet]
        public IActionResult GetVotingStatus()
        {
            var votingConfig = _context.VotingConfigurations.FirstOrDefault();
            var isOpen = votingConfig?.IsVotingOpen ?? false;

            return Json(new { isVotingOpen = isOpen });
        }

        // Get real-time vote counts for live updates
        [HttpGet]
        public async Task<IActionResult> GetLiveResults()
        {
            try
            {
                var activeElection = await _context.Elections.FirstOrDefaultAsync(e => e.IsActive);
                if (activeElection == null)
                    return Json(new { success = false, message = "No active election" });

                var results = await _context.Candidates
                    .Where(c => c.ElectionId == activeElection.Id)
                    .GroupBy(c => c.Position)
                    .ToDictionaryAsync(
                        g => g.Key,
                        g => g.Select(c => new
                        {
                            c.Id,
                            c.Name,
                            c.VoteCount,
                            c.PartyList
                        }).OrderByDescending(x => x.VoteCount).ToList()
                    );

                return Json(new { success = true, results });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Get user's voting receipt
        [HttpGet]
        public async Task<IActionResult> GetVotingReceipt()
        {
            try
            {
                var username = User.Identity.Name;
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

                if (user == null)
                    return Json(new { success = false, message = "User not found" });

                if (!user.HasVoted)
                    return Json(new { success = false, message = "No voting record found" });

                var votes = await _context.Votes
                    .Where(v => v.UserId == user.Id && v.IsFinal)
                    .Include(v => v.Candidate)
                    .Include(v => v.Election)
                    .Select(v => new
                    {
                        CandidateName = v.Candidate.Name,
                        Position = v.Candidate.Position,
                        Party = v.Candidate.PartyList,
                        ElectionName = v.Election.Name,
                        VoteTime = v.Timestamp
                    })
                    .ToListAsync();

                var activeElection = await _context.Elections.FirstOrDefaultAsync(e => e.IsActive);

                return Json(new
                {
                    success = true,
                    votes,
                    electionName = activeElection?.Name ?? "Unknown Election",
                    userName = user.Username,
                    userEmail = user.Email,
                    userCourse = user.Course,
                    voteDate = DateTime.Now.ToString("MMMM dd, yyyy 'at' hh:mm tt")
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetVoteProgress()
        {
            try
            {
                var username = User.Identity.Name;
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

                if (user == null)
                    return Json(new { success = false, message = "User not found" });

                var activeElection = await _context.Elections.FirstOrDefaultAsync(e => e.IsActive);
                if (activeElection == null)
                    return Json(new { success = false, message = "No active election" });

                // FIX: Only get positions that have candidates in active election
                var allPositions = await _context.Candidates
                    .Where(c => c.ElectionId == activeElection.Id)
                    .Select(c => c.Position)
                    .Distinct()
                    .ToListAsync();

                var userVotes = await _context.Votes
                    .Where(v => v.UserId == user.Id)
                    .Include(v => v.Candidate)
                    .ToListAsync();

                var votedPositions = userVotes
                    .Where(v => v.Candidate != null)
                    .Select(v => v.Candidate.Position)
                    .Distinct()
                    .ToList();

                var progress = new
                {
                    totalPositions = allPositions.Count,
                    votedPositions = votedPositions.Count,
                    remainingPositions = allPositions.Except(votedPositions).ToList(),
                    hasVoted = user.HasVoted,
                    votesByPosition = userVotes
                        .Where(v => v.Candidate != null)
                        .GroupBy(v => v.Candidate.Position)
                        .ToDictionary(g => g.Key, g => g.Count())
                };

                return Json(new { success = true, progress });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Get user profile data
        [HttpGet]
        public async Task<IActionResult> GetUserProfile()
        {
            try
            {
                var username = User.Identity.Name;
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

                if (user == null)
                    return Json(new { success = false, message = "User not found" });

                return Json(new
                {
                    success = true,
                    user = new
                    {
                        user.Id,
                        user.Username,
                        user.Email,
                        user.Course,
                        user.Role,
                        user.IsApproved,
                        user.HasVoted,
                        user.CreatedAt,
                        user.LastVotedElectionDate,
                        user.ProfilePicture
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateUserProfile([FromForm] UpdateProfileRequest request)
        {
            try
            {
                var username = User.Identity.Name;
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

                if (user == null)
                    return Json(new { success = false, message = "User not found" });
                // Validate username
                if (!string.IsNullOrEmpty(request.Username))
                {
                    // Check if username is already taken by another user
                    if (request.Username != username)
                    {
                        var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == request.Username && u.Id != user.Id);
                        if (existingUser != null)
                        {
                            return Json(new { success = false, message = "Username is already taken. Please choose a different one." });
                        }
                    }

                    // Validate username format
                    if (request.Username.Length < 3)
                    {
                        return Json(new { success = false, message = "Username must be at least 3 characters long." });
                    }

                    if (request.Username.Length > 20)
                    {
                        return Json(new { success = false, message = "Username cannot be longer than 20 characters." });
                    }
                }
                // Validate email format
                if (!string.IsNullOrEmpty(request.Email) && !IsValidEmail(request.Email))
                {
                    return Json(new { success = false, message = "Please enter a valid email address." });
                }

                // Update basic info - handle null values
                user.Email = !string.IsNullOrEmpty(request.Email) ? request.Email : user.Email;
                user.Username = !string.IsNullOrEmpty(request.Username) ? request.Username : user.Username;

                // Handle profile picture upload
                if (request.ProfilePicture != null && request.ProfilePicture.Length > 0)
                {
                    var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                    var fileExtension = Path.GetExtension(request.ProfilePicture.FileName).ToLowerInvariant();

                    if (!allowedExtensions.Contains(fileExtension))
                    {
                        return Json(new { success = false, message = "Only image files (JPG, PNG, GIF, WEBP) are allowed." });
                    }

                    if (request.ProfilePicture.Length > 2 * 1024 * 1024) // 2MB
                    {
                        return Json(new { success = false, message = "File size must be less than 2MB." });
                    }

                    try
                    {
                        // Create uploads directory if it doesn't exist
                        var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "users");
                        if (!Directory.Exists(uploadsFolder))
                        {
                            Directory.CreateDirectory(uploadsFolder);
                        }

                        // Generate unique filename
                        var uniqueFileName = $"user_{user.Id}_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid()}{fileExtension}";
                        var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                        // Save the file
                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await request.ProfilePicture.CopyToAsync(fileStream);
                        }

                        // Delete old profile picture if it exists and isn't default
                        if (!string.IsNullOrEmpty(user.ProfilePicture) &&
                            !user.ProfilePicture.Contains("default-avatar") &&
                            !user.ProfilePicture.Contains("/images/"))
                        {
                            var oldFilePath = Path.Combine(_environment.WebRootPath, user.ProfilePicture.TrimStart('/'));
                            if (System.IO.File.Exists(oldFilePath))
                            {
                                System.IO.File.Delete(oldFilePath);
                            }
                        }

                        // Update profile picture path
                        user.ProfilePicture = "/uploads/users/" + uniqueFileName;
                    }
                    catch (Exception ex)
                    {
                        return Json(new { success = false, message = $"Error saving profile picture: {ex.Message}" });
                    }
                }

                // Save changes to database
                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = "Profile updated successfully!",
                    profilePicture = user.ProfilePicture,
                    email = user.Email,
                    username = user.Username
                });
            }
            catch (DbUpdateException dbEx)
            {
                // Log the database exception
                return Json(new { success = false, message = "Database error while updating profile. Please try again." });
            }
            catch (Exception ex)
            {
                // Log the full exception for debugging
                Console.WriteLine($"Profile update error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return Json(new { success = false, message = $"Error updating profile: {ex.Message}" });
            }
        }

        // Add email validation helper method
        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }
        [HttpGet]
        public async Task<IActionResult> DebugElectionData()
        {
            try
            {
                var activeElection = await _context.Elections.FirstOrDefaultAsync(e => e.IsActive);

                var debugInfo = new
                {
                    ActiveElection = activeElection?.Name ?? "NO ACTIVE ELECTION",
                    ActiveElectionId = activeElection?.Id,

                    // All positions with candidates in active election
                    PositionsWithCandidates = await _context.Candidates
                        .Where(c => c.ElectionId == activeElection.Id)
                        .Select(c => new { c.Position, c.Name })
                        .GroupBy(x => x.Position)
                        .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.Name).ToList()),

                    // All position settings (this might include old positions)
                    AllPositionSettings = await _context.PositionSettings
                        .Select(ps => new { ps.PositionName, ps.VotesAllowed })
                        .ToListAsync()
                };

                return Json(new { success = true, data = debugInfo });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
        [HttpGet]
        public async Task<IActionResult> GetUserVotes()
        {
            try
            {
                var username = User.Identity.Name;
                Console.WriteLine($"=== GetUserVotes called for user: {username}");

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

                if (user == null)
                {
                    Console.WriteLine("=== User not found ===");
                    return Json(new { success = false, message = "User not found" });
                }

                Console.WriteLine($"=== User found: {user.Username}, HasVoted: {user.HasVoted}");

                var votes = await _context.Votes
                    .Where(v => v.UserId == user.Id && !v.IsFinal) 
                    .Include(v => v.Candidate)
                    .Select(v => new
                    {
                        CandidateId = v.CandidateId,
                        Name = v.Candidate.Name,
                        Position = v.Candidate.Position
                    })
                    .ToListAsync();

                Console.WriteLine($"=== Found {votes.Count} non-final votes for user ===");
                foreach (var vote in votes)
                {
                    Console.WriteLine($"=== Vote: {vote.Name} for {vote.Position} (ID: {vote.CandidateId})");
                }

                return Json(new { success = true, votes });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== GetUserVotes error: {ex.Message} ===");
                return Json(new { success = false, message = ex.Message });
            }
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

    public class UpdateProfileRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public IFormFile? ProfilePicture { get; set; }
    }
}