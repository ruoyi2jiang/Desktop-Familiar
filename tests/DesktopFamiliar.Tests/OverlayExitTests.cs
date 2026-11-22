using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopFamiliar.AddonSdk;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Views;

namespace DesktopFamiliar.Tests;

[CollectionDefinition("Overlay application", DisableParallelization = true)]
public sealed class OverlayApplicationCollection { }

[Collection("Overlay application")]
public sealed class OverlayExitTests
{
    // A real WPF dispatcher drives both the idle timer and the animation completion event.
    [Fact]
    public void FarewellSurvivesAnIdleDeadlineAndASecondExitCanSkipIt()
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var pending = new List<DispatcherOperation>();
            DispatcherHookEventHandler capture = (_, e) => pending.Add(e.Operation);
            // Use the real window resources without starting the user's tray app or opening their library.
            dispatcher.Hooks.OperationPosted += capture;
            var app = new App();
            dispatcher.Hooks.OperationPosted -= capture;
            foreach (var operation in pending) operation.Abort();
            var root = Directory.CreateTempSubdirectory("df-exit-").FullName;
            try
            {
                app.InitializeComponent();
                app.Dispatcher.InvokeAsync(async () =>
                {
                    try { await ExerciseExit(root); }
                    catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
                    finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
                });
                Dispatcher.Run();
            }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
            finally { Directory.Delete(root, true); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "The overlay dispatcher failed to exit.");
        failure?.Throw();
    }

    private static async Task ExerciseExit(string root)
    {
        ThemeService.Apply(AppTheme.Crimson);
        var image = Path.Combine(root, "frame.png");
        using (var file = File.Create(image))
        {
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4)));
            png.Save(file);
        }
        AnimationScene Scene(string name, double seconds) => new() { Name = name,
            PngFrames = [new() { FilePath = image, DurationSeconds = seconds }], PlaybackMode = ScenePlaybackMode.PlayOnce };
        var idle = Scene("Idle", .1);
        var activity = Scene("Activity", .1);
        var exit = Scene("Farewell", AppSettings.MinimumIdleDelaySeconds + 1);
        var pet = new CompanionDefinition { Name = "Test pet", Animations = [idle, activity, exit],
            IdleDefaultAnimationId = idle.Id, IdleRandomAnimationIds = [activity.Id], ExitAnimationId = exit.Id };
        var manager = new CompanionManager(new CompanionStorage(root));
        manager.Data.Companions.Add(pet);
        manager.Data.Settings.CurrentCompanionId = pet.Id;
        manager.Data.Settings.IdleDelaySeconds = AppSettings.MinimumIdleDelaySeconds;

        CompanionOverlayWindow Open()
        {
            var overlay = new CompanionOverlayWindow(manager, new AddonViewportActionRegistry(), (_, _, _) => Task.CompletedTask)
                { Opacity = 0, ShowActivated = false };
            overlay.ShowCompanion();
            return overlay;
        }

        var first = Open();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.CloseForExit(() => finished.SetResult());
        Assert.True(first.IsVisible);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(AppSettings.MinimumIdleDelaySeconds + 4));
        Assert.False(first.IsVisible);

        var second = Open();
        int completions = 0;
        second.CloseForExit(() => completions++);
        Assert.True(second.IsVisible);
        second.CloseForExit();
        second.CloseForExit();
        Assert.False(second.IsVisible);
        Assert.Equal(1, completions);
    }
}
