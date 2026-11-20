using System.Text.Json.Serialization;

namespace DesktopFamiliar.Models;

public sealed record ChatTurn(string Prompt, string Reply, DateTimeOffset Time);

public sealed record CompanionPortrait(string? PicturePath, string? IdleFramePath,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ModelPath = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ModelClip = null)
{
    public static CompanionPortrait From(CompanionDefinition pet) => From(pet, pet.ActiveScenes.IdleDefaultAnimation);

    public static CompanionPortrait From(CompanionDefinition pet, AnimationScene? idle) =>
        pet.Appearance == CompanionAppearance.Model3D
            ? new(pet.ProfilePicturePath, null, idle == null ? null : pet.Model3D.FilePath, idle?.FirstModelClip)
            : new(pet.ProfilePicturePath, idle?.GetPreviewPath());
}

public enum ChatHistoryClearScope { Messages, Notes, MessagesAndNotes }

public sealed class ChatConversation
{
    public string Name { get; set; } = string.Empty;
    public CompanionPortrait? ArchivedPortrait { get; set; }
    public List<ChatTurn> Turns { get; set; } = [];
    public List<string> Notes { get; set; } = [];

    public ChatHistoryClearScope GetClearScope(bool alsoDeleteNotes)
    {
        if (Turns.Count == 0) return ChatHistoryClearScope.Notes;
        return Notes.Count > 0 && alsoDeleteNotes ? ChatHistoryClearScope.MessagesAndNotes : ChatHistoryClearScope.Messages;
    }
}
