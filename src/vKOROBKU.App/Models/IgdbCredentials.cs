namespace vKOROBKU.App.Models;

public sealed record IgdbCredentials(string ClientId, string ClientSecret)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
