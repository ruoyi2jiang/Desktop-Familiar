namespace DesktopFamiliar.Models;

/// <summary>A percentage-based viewport rectangle that can trigger a scene.</summary>
public sealed class CompanionClickArea
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public double LeftPercent { get; set; }
    public double TopPercent { get; set; }
    public double WidthPercent { get; set; } = 100;
    public double HeightPercent { get; set; } = 100;
    public Guid? AnimationId { get; set; }

    public CompanionClickArea Clone() => new()
    {
        Id = Id,
        Name = Name,
        LeftPercent = LeftPercent,
        TopPercent = TopPercent,
        WidthPercent = WidthPercent,
        HeightPercent = HeightPercent,
        AnimationId = AnimationId
    };
}
