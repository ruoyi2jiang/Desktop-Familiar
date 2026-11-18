namespace DesktopFamiliar.Models;

public sealed record ChatTurn(string Prompt, string Reply, DateTimeOffset Time);

public sealed record ArchivedPetPortrait(string? PicturePath, string? IdleFramePath);

public enum ChatHistoryClearScope { Messages, Notes, MessagesAndNotes }

public sealed class ChatConversation
{
    public string Name { get; set; } = string.Empty;
    public ArchivedPetPortrait? ArchivedPortrait { get; set; }
    public List<ChatTurn> Turns { get; set; } = [];
    public List<string> Notes { get; set; } = [];

    public ChatHistoryClearScope GetClearScope(bool alsoDeleteNotes)
    {
        if (Turns.Count == 0) return ChatHistoryClearScope.Notes;
        return Notes.Count > 0 && alsoDeleteNotes ? ChatHistoryClearScope.MessagesAndNotes : ChatHistoryClearScope.Messages;
    }
}
