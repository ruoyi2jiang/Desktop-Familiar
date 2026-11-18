namespace DesktopFamiliar.Models;

public sealed record ChatTurn(string Prompt, string Reply, DateTimeOffset Time);

public sealed class ChatConversation
{
    public string Name { get; set; } = string.Empty;
    public List<ChatTurn> Turns { get; set; } = [];
    public List<string> Notes { get; set; } = [];
}
