namespace Services;

public class UserProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    public UserProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string GetUserId()
    {
        var userId = _httpContextAccessor.HttpContext.User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return userId;
    }
}