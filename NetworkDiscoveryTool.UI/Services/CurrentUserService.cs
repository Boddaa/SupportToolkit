namespace NetworkDiscoveryTool.UI.Services;

public sealed class CurrentUserService
{
    public int? UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsLoggedIn => !string.IsNullOrEmpty(Username);
    public bool IsAdmin => string.Equals(Role, "Administrator", StringComparison.OrdinalIgnoreCase) || string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase);

    public event Action? UserChanged;

    public void SetUser(int? userId, string username, string role)
    {
        UserId = userId;
        Username = username;
        Role = role;
        UserChanged?.Invoke();
    }

    public void Clear()
    {
        UserId = null;
        Username = string.Empty;
        Role = string.Empty;
        UserChanged?.Invoke();
    }
}
