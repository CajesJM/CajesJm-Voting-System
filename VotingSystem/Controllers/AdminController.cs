using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using VotingSystem.Hubs;
using VotingSystem.Models;

namespace VotingSystem.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly VotingDbContext _context;

        public AdminController(VotingDbContext context)
        {
            _context = context;
        }

        // 📊 Dashboard: List candidates with vote counts
        public IActionResult Dashboard()
        {
            var candidates = _context.Candidates.ToList();
            return View("Dashboard", candidates); 
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
                var hubContext = HttpContext.RequestServices.GetService<IHubContext<DashboardHub>>();
                await hubContext.Clients.All.SendAsync("ReceiveUpdate");

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
                var hubContext = HttpContext.RequestServices.GetService<IHubContext<DashboardHub>>();
                await hubContext.Clients.All.SendAsync("ReceiveUpdate");

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
                var hubContext = HttpContext.RequestServices.GetService<IHubContext<DashboardHub>>();
                await hubContext.Clients.All.SendAsync("ReceiveUpdate");
            }

            return RedirectToAction("Dashboard");
        }
        
    }
}