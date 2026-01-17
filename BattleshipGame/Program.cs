using System.Text.Json.Serialization;
using BattleshipGame.Hubs;
using BattleshipGame.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// Configure database path for persistence - try multiple locations
string? dbPath = null;
try
{
    var possiblePaths = new List<string>
    {
        Path.Combine(builder.Environment.ContentRootPath, "App_Data")
    };

    // Only add these if they return valid paths
    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    if (!string.IsNullOrEmpty(localAppData))
    {
        possiblePaths.Add(Path.Combine(localAppData, "BattleshipGame"));
    }

    var tempPath = Path.GetTempPath();
    if (!string.IsNullOrEmpty(tempPath))
    {
        possiblePaths.Add(tempPath);
    }

    foreach (var basePath in possiblePaths)
    {
        try
        {
            Directory.CreateDirectory(basePath);
            var testFile = Path.Combine(basePath, ".write_test");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            dbPath = Path.Combine(basePath, "battleship.db");
            break;
        }
        catch
        {
            // Try next path
        }
    }
}
catch
{
    // If anything fails, dbPath stays null and we'll use in-memory mode
}

builder.Services.AddSingleton<GamePersistenceService>(sp => new GamePersistenceService(dbPath));
builder.Services.AddSingleton<GameService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();
app.MapHub<GameHub>("/gameHub");

app.Run();
