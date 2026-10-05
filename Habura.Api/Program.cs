using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;
using Habura.Api.Data;
using Habura.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Configure JWT authentication
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection.GetValue<string>("Key") ?? "replace-this-with-a-long-secret-key";
var jwtIssuer = jwtSection.GetValue<string>("Issuer") ?? "HaburaApi";
var jwtAudience = jwtSection.GetValue<string>("Audience") ?? "HaburaClient";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true
        };
    });

builder.Services.AddAuthorization();

// Configure EF Core + SQLite
var connection = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=habura.db";
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
builder.Services.AddScoped<IUserService, UserService>();

var app = builder.Build();

// record start time for uptime calculations
var startedAt = DateTimeOffset.UtcNow;

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

// Public endpoint to request a JWT for testing purposes.
// In production, replace with real user validation.
app.MapPost("/token", async (UserCredentials creds, IUserService users) =>
{
    if (creds is null)
        return Results.BadRequest();

    var user = await users.ValidateCredentialsAsync(creds.Username, creds.Password);
    if (user is null)
        return Results.Unauthorized();

    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.Username),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
    };

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
    var credsSigning = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: claims,
        expires: DateTime.UtcNow.AddHours(1),
        signingCredentials: credsSigning);

    var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

    return Results.Ok(new { access_token = tokenString });
});

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.RequireAuthorization()
.WithName("GetWeatherForecast");

// Health endpoint - returns uptime, environment and basic DB status
app.MapGet("/health", async (IServiceProvider services) =>
{
    var env = app.Environment.EnvironmentName;
    var now = DateTimeOffset.UtcNow;
    var uptime = now - startedAt;

    var health = new
    {
        status = "Healthy",
        startedAt = startedAt,
        uptime = new { totalSeconds = (long)uptime.TotalSeconds, human = uptime.ToString() },
        environment = env,
        framework = RuntimeInformation.FrameworkDescription,
        database = new { canConnect = false, users = 0 },
        hardware = new
        {
            process = new { },
            disks = Array.Empty<object>()
        }
    };

    try
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetService<AppDbContext>();
        if (db is not null)
        {
            var canConnect = await db.Database.CanConnectAsync();
            var users = 0;
            if (canConnect)
            {
                users = await db.Users.CountAsync();
            }

            // process memory
            var process = Process.GetCurrentProcess();
            var processMemory = new
            {
                workingSet = process.WorkingSet64,
                privateBytes = process.PrivateMemorySize64,
                gcTotalMemory = GC.GetTotalMemory(false)
            };

            // disk info (cross-platform)
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => new
                {
                    name = d.Name,
                    totalBytes = d.TotalSize,
                    availableBytes = d.AvailableFreeSpace
                });

            return Results.Ok(new
            {
                status = "Healthy",
                startedAt,
                uptime = new { totalSeconds = (long)uptime.TotalSeconds, human = uptime.ToString() },
                environment = env,
                framework = RuntimeInformation.FrameworkDescription,
                database = new { canConnect, users },
                hardware = new {
                    process = processMemory,
                    disks = drives
                }
            });
        }
    }
    catch
    {
        // ignore and return base health
    }

    return Results.Ok(health);
}).AllowAnonymous();

// Ensure database exists and seed a test user if none exists
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
    var existing = db.Users.Any();
    if (!existing)
    {
        // create a default test user: username=test password=password
        userService.CreateUserAsync("test", "password").GetAwaiter().GetResult();
    }
}

app.Run();

record UserCredentials(string Username, string Password);

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
