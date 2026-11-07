using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TrackerApi.Auth;
using TrackerApi.Data;
using TrackerApi.Services;

// Program.cs is the entry point (like main() in Java). "Top-level statements"
// let us write it without a class or a Main method.

var builder = WebApplication.CreateBuilder(args);

// Register services (like Spring's dependency injection container).
builder.Services
    .AddControllers(o => o.Filters.Add<TrackerExceptionFilter>()) // rule errors -> { "message": "..." }
    // Send enums as text ("InUse") instead of numbers (1) in JSON.
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddDbContext<TrackerDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

// Time limits from the "Tracker" section of appsettings.json.
builder.Services.Configure<TrackerOptions>(builder.Configuration.GetSection(TrackerOptions.Section));

// Every timestamp comes from the server's clock. (Tests swap in a fake clock.)
builder.Services.AddSingleton(TimeProvider.System);

// The business rules. "Scoped" = one instance per HTTP request.
builder.Services.AddScoped<ComputerStatusService>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<PassService>();
builder.Services.AddScoped<WaitlistService>();
builder.Services.AddScoped<ProblemService>();
builder.Services.AddScoped<StaffService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddHttpContextAccessor(); // lets AuditService see who is signed in

// Who may call what: staff cookies, the kiosk key, and anonymous phones (see Auth/AuthSetup.cs).
builder.Services.AddTrackerAuth(builder.Configuration);

// Expires passes and ends overdue sessions in the background.
builder.Services.AddHostedService<CleanupService>();

// Swagger gives us a web page to test the API without writing a frontend.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Allow a Vite/React dev server on port 5173 to call this API from the browser.
builder.Services.AddCors(options =>
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()));

var app = builder.Build();

// Create or update the database to the latest migration, and seed sample data.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TrackerDbContext>();
    DatabaseSetup.Migrate(db);

    // "dotnet run -- create-admin <username> <full name>" makes the first admin account.
    if (args.Length > 0 && args[0] == "create-admin")
    {
        await AdminCommand.RunAsync(scope.ServiceProvider.GetRequiredService<StaffService>(), args[1..]);
        return;
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Lets the test project start the API in memory (WebApplicationFactory<Program>).
public partial class Program { }
