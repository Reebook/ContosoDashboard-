using System.Security.Claims;

namespace ContosoDashboard.Services;

public sealed record ActorContext(int UserId, string Name, string Email, string Role, string Department)
{
    public static bool TryFromPrincipal(ClaimsPrincipal principal, out ActorContext? actor, out string message)
    {
        actor = null;
        message = "Your sign-in is missing required identity information. Contact your administrator.";
        var idValue = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("userId");
        var name = principal.FindFirstValue(ClaimTypes.Name) ?? principal.FindFirstValue("name");
        var email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email");
        var role = principal.FindFirstValue(ClaimTypes.Role) ?? principal.FindFirstValue("role");
        var department = principal.FindFirstValue("department");
        if (!principal.Identity?.IsAuthenticated ?? true) return false;
        if (!int.TryParse(idValue, out var userId) || userId <= 0 ||
            string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(department)) return false;
        actor = new ActorContext(userId, name, email, role, department);
        message = string.Empty;
        return true;
    }
}
