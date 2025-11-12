using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using VotingSystem.Models;

namespace VotingSystem.Controllers
{
    public class AccountController : Controller
    {
        private readonly VotingDbContext _context;

        public AccountController(VotingDbContext context)
        {
            _context = context;
        }

        // GET: Login page
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        // POST: Handle login
        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            var hashedPassword = SecurityHelper.HashPassword(model.Password);
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == model.Username && u.PasswordHash == hashedPassword);

            if (user == null)
            {
                ModelState.AddModelError("", "Invalid credentials");
                return View(model);
            }

            // Check if user is approved
            if (!user.IsApproved)
            {
                ModelState.AddModelError("", "Your account is pending admin approval. Please wait for approval.");
                return View(model);
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()) // Add user ID
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            return RedirectToAction("Dashboard", user.Role == "Admin" ? "Admin" : "User");
        }

        // Optional: Logout
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        // Register
        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (_context.Users.Any(u => u.Username == model.Username))
            {
                ModelState.AddModelError("", "Username already exists");
                return View(model);
            }

            var user = new User
            {
                Username = model.Username,
                PasswordHash = SecurityHelper.HashPassword(model.Password),
                Email = model.Email,
                Course = model.Course,
                RequestedRole = model.RequestedRole,
                Role = "Pending", // Set to Pending until approved
                IsApproved = false,
                CreatedAt = DateTime.Now
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return RedirectToAction("PendingApproval");
        }

        // Pending Approval Page
        public IActionResult PendingApproval()
        {
            return View();
        }
    }
}