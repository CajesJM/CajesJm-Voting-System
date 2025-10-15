using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VotingSystem.Hubs;
using VotingSystem.Models;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();


// ? Configure Entity Framework with MySQL
builder.Services.AddDbContext<VotingDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("VotingDbContext"),
        new MySqlServerVersion(new Version(8, 0, 0))
    )
);
builder.Services.AddAuthentication("Cookies")
    .AddCookie("Cookies", options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
    });

var app = builder.Build();

// ?? Seed sample data on startup
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<VotingDbContext>();
    context.Database.EnsureCreated(); // Creates DB if it doesn't exist
    VotingDbSeeder.Seed(context);     // Seeds sample users and candidates
}

// ?? Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.MapHub<DashboardHub>("/dashboardHub");

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");
app.Run();