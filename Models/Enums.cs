using System.Text.Json.Serialization;

namespace DesktopFamiliar.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScenePlaybackMode
{
    PlayOnce,
    LoopUntilDialogEnds
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AnimationFrameLoopMode
{
    EntireAnimation,
    SelectedRange,
    SingleFrame
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CompanionActivityState
{
    Idle,
    Reminder
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CalendarEventRecurrence
{
    OneTime,
    Weekly
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CalendarEventCategory
{
    Anniversary,
    Business,
    Life,
    Other
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppTheme
{
    Crimson,
    SeaBlue,
    StellaDark,
    Custom
}
