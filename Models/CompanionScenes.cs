using System.Text.Json.Serialization;

namespace DesktopFamiliar.Models;

public abstract class CompanionScenes
{
    public List<AnimationScene> Animations { get; set; } = [];
    public Guid? StartAnimationId { get; set; }
    public Guid? ReminderAnimationId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ChatReplyAnimationId { get; set; }
    // Missing keys match scene names automatically. An explicit null disables that expression.
    public Dictionary<string, Guid?> ChatExpressions { get; set; } = new();
    public Guid? SelfBirthdayAnimation1Id { get; set; }
    public Guid? SelfBirthdayAnimation2Id { get; set; }
    public Guid? UserBirthdayAnimation1Id { get; set; }
    public Guid? UserBirthdayAnimation2Id { get; set; }
    public Guid? ExitAnimationId { get; set; }
    public Guid? IdleDefaultAnimationId { get; set; }
    public List<Guid> IdleRandomAnimationIds { get; set; } = [];
    public CompanionInteractionStateAssignments InteractionStates { get; set; } = new();

    [JsonIgnore]
    public AnimationScene? StartAnimation => FindAnimation(StartAnimationId);

    [JsonIgnore]
    public AnimationScene? ReminderAnimation => FindAnimation(ReminderAnimationId);

    [JsonIgnore]
    public AnimationScene? SelfBirthdayAnimation1 => FindAnimation(SelfBirthdayAnimation1Id);

    [JsonIgnore]
    public AnimationScene? SelfBirthdayAnimation2 => FindAnimation(SelfBirthdayAnimation2Id);

    [JsonIgnore]
    public AnimationScene? UserBirthdayAnimation1 => FindAnimation(UserBirthdayAnimation1Id);

    [JsonIgnore]
    public AnimationScene? UserBirthdayAnimation2 => FindAnimation(UserBirthdayAnimation2Id);

    [JsonIgnore]
    public AnimationScene? ExitAnimation => FindAnimation(ExitAnimationId);

    [JsonIgnore]
    public AnimationScene? IdleDefaultAnimation => FindAnimation(IdleDefaultAnimationId);

    public AnimationScene? GetInteractionAnimation(CompanionInteractionState state) =>
        FindAnimation(InteractionStates?.GetAnimationId(state));

    public AnimationScene? FindAnimation(Guid? id) => id is Guid animationId
        ? Animations.FirstOrDefault(scene => scene.Id == animationId)
        : null;

    internal T CopyScenesTo<T>(T target) where T : CompanionScenes
    {
        target.Animations = Animations.Select(scene => scene.Clone()).ToList();
        target.StartAnimationId = StartAnimationId;
        target.ReminderAnimationId = ReminderAnimationId;
        target.ChatReplyAnimationId = ChatReplyAnimationId;
        target.ChatExpressions = new(ChatExpressions);
        target.SelfBirthdayAnimation1Id = SelfBirthdayAnimation1Id;
        target.SelfBirthdayAnimation2Id = SelfBirthdayAnimation2Id;
        target.UserBirthdayAnimation1Id = UserBirthdayAnimation1Id;
        target.UserBirthdayAnimation2Id = UserBirthdayAnimation2Id;
        target.ExitAnimationId = ExitAnimationId;
        target.IdleDefaultAnimationId = IdleDefaultAnimationId;
        target.IdleRandomAnimationIds = [.. IdleRandomAnimationIds];
        target.InteractionStates = InteractionStates?.Clone() ?? new();
        return target;
    }
}
