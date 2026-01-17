using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BattleshipGame.Pages;

public class IndexModel : PageModel
{
    private readonly IConfiguration _configuration;

    public IndexModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [BindProperty(SupportsGet = true)]
    public string? Room { get; set; }

    /// <summary>
    /// URL to link back to the portfolio site.
    /// Configure via appsettings.json "PortfolioUrl" or defaults to "#"
    /// </summary>
    public string PortfolioUrl => _configuration["PortfolioUrl"] ?? "#";

    public void OnGet()
    {
    }
}
