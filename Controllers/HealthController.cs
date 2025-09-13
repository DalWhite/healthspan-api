
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("[controller]")]
public class HealthController : ControllerBase
{
    /// <summary>
    /// Health check endpoint that responds to HEAD requests
    /// This is used by Uptime Robot to keep the Azure App Service alive
    /// </summary>
    [HttpHead]
    [HttpGet]
    public IActionResult Check()
    {
        // For HEAD requests, no content will be returned,
        // only the 200 OK status code
        return Ok();
    }

    /// <summary>
    /// More detailed health information (optional)
    /// </summary>
    [HttpGet("details")]
    public IActionResult Details()
    {
        var healthInfo = new
        {
            Status = "Healthy",
            Timestamp = DateTime.UtcNow,
            Version = "0.0.1"
        };

        return Ok(healthInfo);
    }
}