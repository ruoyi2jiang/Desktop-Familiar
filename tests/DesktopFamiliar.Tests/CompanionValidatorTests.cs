using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Validation;

namespace DesktopFamiliar.Tests;

public sealed class CompanionValidatorTests
{
    [Fact]
    public void BusyAssignmentIsNoLongerRequiredWhenIdleDefaultIsValid()
    {
        var path = Path.Combine(Path.GetTempPath(), $"DesktopFamiliar-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, [0]);
        try
        {
            var idle = new AnimationScene { AnimationPath = path };
            var companion = new CompanionDefinition
            {
                Name = "Test companion",
                Animations = [idle],
                IdleDefaultAnimationId = idle.Id
            };

            Assert.Empty(CompanionValidator.Validate(companion));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingOptionalStateAssignmentIsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"DesktopFamiliar-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, [0]);
        try
        {
            var idle = new AnimationScene { AnimationPath = path };
            var companion = new CompanionDefinition
            {
                Name = "Test companion",
                Animations = [idle],
                IdleDefaultAnimationId = idle.Id,
                ExitAnimationId = Guid.NewGuid()
            };

            Assert.Single(CompanionValidator.Validate(companion));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
