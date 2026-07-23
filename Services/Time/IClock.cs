namespace DesktopFamiliar.Services.Time;

public interface IClock
{
    DateTime Now { get; }
}

public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();

    private SystemClock()
    {
    }

    public DateTime Now => DateTime.Now;
}
