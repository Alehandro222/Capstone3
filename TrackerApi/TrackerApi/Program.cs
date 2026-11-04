using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TrackerApi.Data;

// Program.cs is the entry point (like main() in Java). "Top-level statements"
// let us write it without a class or a Main method.

var builder = WebApplication.CreateBuilder(args);

// Register services (like Spring's dependency injection container).
builder.Services
    .AddControllers()
    // Send enums as text ("InUse") instead of numbers (1) in JSON.
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddDbContext<TrackerDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

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

// Create the SQLite database file and seed data on first run.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TrackerDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");
app.MapControllers();

app.Run();
