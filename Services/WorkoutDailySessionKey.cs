using System.Security.Cryptography;
using System.Text;

namespace Services;

internal static class WorkoutDailySessionKey
{
    public static string Create(string userId, DateOnly date)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var source = Encoding.UTF8.GetBytes($"{userId}\n{date:yyyy-MM-dd}");
        return Convert.ToHexString(SHA256.HashData(source));
    }
}
