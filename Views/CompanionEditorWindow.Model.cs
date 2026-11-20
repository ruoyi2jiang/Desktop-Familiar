using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Animation;
using Microsoft.Win32;
using DesktopFamiliar.Views.Controls;
using System.Diagnostics;
using System.Windows.Threading;

namespace DesktopFamiliar.Views;

public partial class CompanionEditorWindow
{
    private sealed record ClipOption(string Label, ModelAnimationSource? Source);
    private enum PreviewPhase { Scene, WaitingForCaptions, Idle }
    private List<ModelAnimationSource> _sources = [];
    private PreviewPhase _previewPhase;
    private bool _updatingPlayhead;
    private bool _releasePreviewAtCaptionEnd;
    private bool _scenePreviewPending;
    private readonly DispatcherTimer _captionTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly Stopwatch _captionClock = new();
    private List<ModelDialogueTiming.Caption> _captions = [];

    private string? _pendingModel;
    private bool _loadingModel;
    private bool _appearanceLoaded;
    private bool IsModel => _companion?.Appearance == CompanionAppearance.Model3D;
    private CompanionScenes EditingScenes => _companion.ActiveScenes;

    private bool CommitSceneEdits()
    {
        if (!CommitFrameEdits() || !DialogGrid.CommitEdit(DataGridEditingUnit.Cell, true)
            || !DialogGrid.CommitEdit(DataGridEditingUnit.Row, true)) return false;
        if (IsModel && (Validation.GetHasError(ModelDelay) || SceneList.SelectedItem is AnimationScene scene &&
            !ModelDialogueTiming.HasValidCues(scene)))
        {
            SetValidationHint(LocalizationService.Get("Model3D.InvalidCues"));
            return false;
        }
        if (IsModel && SceneList.SelectedItem is AnimationScene &&
            !TryReadRange(out _))
        {
            SetValidationHint(LocalizationService.Get("Model3D.InvalidRange"));
            return false;
        }
        return true;
    }

    private void Appearance_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_opening || _companion is null || AppearanceCombo.SelectedIndex < 0 || _loadingModel) return;
        if (_appearanceLoaded)
        {
            if (!CommitSceneEdits())
            {
                _loadingModel = true;
                AppearanceCombo.SelectedIndex = (int)_companion.Appearance;
                _loadingModel = false;
                return;
            }
            StoreAssignments();
        }
        ApplyAppearance();
    }

    private void ApplyAppearance(Dictionary<string, double>? preparedClips = null)
    {
        _appearanceLoaded = true;
        _scenePreviewPending = false;
        _companion.Appearance = (CompanionAppearance)AppearanceCombo.SelectedIndex;
        StopPreview();
        ModelPreview.Clear();
        CompanionPreviewModel.Clear();
        ClickAreaModel.Clear();
        ImageSceneSource.Visibility = PlaybackCombo.Visibility = SpriteModeLabel.Visibility = IsModel ? Visibility.Collapsed : Visibility.Visible;
        ShowModelParts.Visibility = CueHelp.Visibility = CueSecondsColumn.Visibility = ModelSceneSource.Visibility = ModelSettings.Visibility = ClickAreaModel.Visibility =
            IsModel ? Visibility.Visible : Visibility.Collapsed;
        ScenesTitle.Text = LocalizationService.Get(IsModel ? "Model3D.Scenes" : "Editor.Animations.Title");
        SceneNameLabel.Text = LocalizationService.Get(IsModel ? "Model3D.SceneName" : "Editor.RegistryName");
        if (IsModel)
        {
            if (preparedClips != null) PopulateModelOptions(preparedClips);
            else LoadModelOptions();
        }
        RefreshSceneSelectors(EditingScenes.Animations.FirstOrDefault()?.Id, switched: true);
    }

    private void ChooseModel_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = LocalizationService.Get("Model3D.Filter") };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var clips = GlbModelFile.ReadClips(picker.FileName);
            var model = _companion.Model3D;
            _pendingModel = model.FilePath = picker.FileName;
            if (model.Animations.Count == 0)
            {
                var idle = clips.Keys.FirstOrDefault(clip => clip.Equals("idle", StringComparison.OrdinalIgnoreCase)) ?? clips.Keys.FirstOrDefault();
                var greeting = clips.Keys.FirstOrDefault(clip => clip.Equals("greeting", StringComparison.OrdinalIgnoreCase));
                model.CreateInitialScenes(idle, greeting);
            }
            else StoreAssignments();
            PopulateModelOptions(clips);
            RefreshSceneSelectors(model.Animations.FirstOrDefault()?.Id, switched: true);
            SetValidationHint(string.Empty);
        }
        catch (Exception error) when (GlbModelFile.IsLoadError(error))
        {
            ReportModelError(error);
        }
    }

    private void LoadModelOptions()
    {
        Dictionary<string, double> clips = [];
        if (_companion.Model3D.FilePath != null)
        {
            try { clips = GlbModelFile.ReadClips(_companion.Model3D.FilePath); }
            catch (Exception error) when (GlbModelFile.IsLoadError(error)) { ReportModelError(error); }
        }
        PopulateModelOptions(clips);
    }

    private void PopulateModelOptions(Dictionary<string, double> clips)
    {
        ModelFileLabel.Text = Path.GetFileName(_companion.Model3D.FilePath) ?? LocalizationService.Get("Model3D.Empty");
        _sources = ModelAnimationCatalog.List(clips);
    }

    private bool ShowParts => ShowModelParts.IsChecked == true;

    private void ShowModelParts_Click(object sender, RoutedEventArgs e)
    {
        if (!CommitSceneEdits()) { ShowModelParts.IsChecked = !ShowParts; return; }
        StoreAssignments();
        RefreshSceneSelectors((SceneList.SelectedItem as AnimationScene)?.Id, switched: true);
    }

    private IEnumerable<AnimationScene> VisibleScenes() => ModelAnimationCatalog.VisibleScenes(EditingScenes, ShowParts);

    private void ShowSelectedClip(AnimationScene? scene)
    {
        _previewPhase = PreviewPhase.Scene;
        StopModelCaptions();
        _loadingModel = true;
        try
        {
            var selected = _sources.FirstOrDefault(item => scene != null && item.Clips.SequenceEqual(scene.SourceClips));
            // A saved combined source may have been imported from a model using different naming conventions.
            if (selected == null && scene?.ModelParts is { Count: > 0 } parts)
            {
                var duration = parts.Sum(name => _sources.FirstOrDefault(item => !item.Combined && item.Name == name)?.Duration ?? 0);
                selected = new(scene.Name, parts.ToArray(), duration, 0, duration);
            }
            var partsOfFamilies = _sources.Where(item => item.Combined).SelectMany(item => item.Clips).ToHashSet();
            var options = new List<ClipOption> { new(LocalizationService.Get("Model3D.Rest"), null) };
            foreach (var item in _sources.Concat(selected != null && !_sources.Contains(selected) ? [selected] : []))
            {
                if (!ShowParts && !item.Combined && partsOfFamilies.Contains(item.Name) && item != selected) continue;
                var label = item.Combined ? LocalizationService.Format("Model3D.Full", item.Name) : item.Name;
                options.Add(new(LocalizationService.Format("Model3D.ClipLength", label, item.Duration), item));
            }
            SceneClip.ItemsSource = options;
            SceneClip.SelectedItem = options.FirstOrDefault(option => option.Source == selected);
            var settings = scene?.ModelLoop;
            var durationSeconds = selected?.Duration ?? 0;
            ModelMode.SelectedIndex = (int)(settings?.Mode ?? (scene?.PlaybackMode == ScenePlaybackMode.LoopUntilDialogEnds
                ? ModelLoopMode.UntilDialogueEnds : ModelLoopMode.Once));
            RangeStart.Text = FormatTime(settings?.StartSeconds ?? 0);
            RangeEnd.Text = FormatTime(settings?.EndSeconds ?? durationSeconds);
            LengthBox.Text = FormatTime(settings?.DurationSeconds ?? 5);
            FixedLength.Visibility = ModelMode.SelectedIndex == (int)ModelLoopMode.FixedDuration ? Visibility.Visible : Visibility.Collapsed;
            Scrubber.Maximum = durationSeconds;
            Scrubber.Value = 0;
            UpdateMarkers(settings?.StartSeconds ?? 0, settings?.EndSeconds ?? durationSeconds);
            UpdatePlayhead(0);
        }
        finally { _loadingModel = false; }
        _scenePreviewPending = !ModelPreview.IsVisible;
        if (!_scenePreviewPending) PlayModelPreview(ModelPreview, scene, loop: false);
    }

    private void ModelPreview_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || !_scenePreviewPending || !IsModel) return;
        _scenePreviewPending = false;
        PlayModelPreview(ModelPreview, SceneList.SelectedItem as AnimationScene, loop: false);
    }

    private static string FormatTime(double value) => value.ToString("0.######", System.Globalization.CultureInfo.CurrentCulture);

    private void ModelSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingModel || !IsModel || SceneList.SelectedItem is not AnimationScene scene || SceneClip.SelectedItem is not ClipOption choice) return;
        var source = choice.Source;
        scene.ModelParts = source?.Combined == true ? source.Clips.ToList() : null;
        scene.ModelClip = source?.Combined == false ? source.Name : null;
        scene.PartOfSceneId = null;
        scene.ModelLoop = source?.Combined == true ? new() { StartSeconds = source.LoopStart, EndSeconds = source.LoopEnd } : null;
        foreach (var part in EditingScenes.Animations)
        {
            if (part.PartOfSceneId == scene.Id) part.PartOfSceneId = null;
            if (part != scene && part.PartOfSceneId == null && source?.Combined == true
                && part.Name == part.ModelClip && source.Clips.Contains(part.Name)) part.PartOfSceneId = scene.Id;
        }
        StoreAssignments();
        RefreshSceneSelectors(scene.Id, switched: true);
    }

    private void ModelModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingModel || !IsModel) return;
        FixedLength.Visibility = ModelMode.SelectedIndex == (int)ModelLoopMode.FixedDuration ? Visibility.Visible : Visibility.Collapsed;
        StoreModelRange();
    }

    private void ModelTimingChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loadingModel && IsModel) StoreModelRange();
    }

    private bool TryReadRange(out ModelLoopSettings settings)
    {
        settings = new();
        if (!double.TryParse(RangeStart.Text, out var start) || !double.TryParse(RangeEnd.Text, out var end)
            || !double.TryParse(LengthBox.Text, out var length)) return false;
        var total = Scrubber.Maximum;
        settings = new() { Mode = (ModelLoopMode)ModelMode.SelectedIndex, StartSeconds = start,
            EndSeconds = total == 0 || Math.Abs(total - end) < 0.000001 ? null : end, DurationSeconds = length };
        return ModelClipTimeline.ValidSettings(settings, total);
    }

    private void StoreModelRange()
    {
        if (SceneList.SelectedItem is not AnimationScene scene || !TryReadRange(out var settings)) return;
        scene.ModelLoop = settings;
        UpdateMarkers(settings.StartSeconds, settings.EndSeconds ?? Scrubber.Maximum);
    }

    private void UpdateMarkers(double start, double end)
    {
        Scrubber.SelectionStart = start;
        Scrubber.SelectionEnd = end;
    }

    private void MarkStart_Click(object sender, RoutedEventArgs e) => RangeStart.Text = FormatTime(Scrubber.Value);
    private void MarkEnd_Click(object sender, RoutedEventArgs e) => RangeEnd.Text = FormatTime(Scrubber.Value);

    private void UpdatePlayhead(double seconds)
    {
        if (_previewPhase == PreviewPhase.Idle) return;
        _updatingPlayhead = true;
        try
        {
            Scrubber.Value = seconds;
            PlayheadText.Text = LocalizationService.Format("Model3D.Playhead", seconds, Scrubber.Maximum);
        }
        finally { _updatingPlayhead = false; }
    }

    private void ScrubberChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingModel || _updatingPlayhead || !IsModel) return;
        StopModelCaptions();
        var wasIdle = _previewPhase == PreviewPhase.Idle;
        _previewPhase = PreviewPhase.Scene;
        if (wasIdle && !PlayModelPreview(ModelPreview, SceneList.SelectedItem as AnimationScene, loop: false)) return;
        ModelPreview.SeekSource(e.NewValue);
    }

    private void ReplayModelScene_Click(object sender, RoutedEventArgs e)
    {
        if (SceneList.SelectedItem is not AnimationScene scene || !CommitSceneEdits()) return;
        _previewPhase = PreviewPhase.Scene;
        StopModelCaptions();
        _captions = ModelDialogueTiming.PreviewCaptions(scene, text => string.IsNullOrWhiteSpace(UserNicknameBox.Text)
            ? text : text.Replace("{{user}}", UserNicknameBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        _releasePreviewAtCaptionEnd = _captions.Count > 0 && scene.ModelLoop?.Mode == ModelLoopMode.UntilDialogueEnds;
        if (!PlayModelPreview(ModelPreview, scene, loop: _releasePreviewAtCaptionEnd)) return;
        _captionClock.Restart();
        UpdateModelCaption();
        if (_captions.Count > 0) _captionTimer.Start();
    }

    private void UpdateModelCaption()
    {
        var time = _captionClock.Elapsed.TotalSeconds;
        if (!ModelPreview.IsVisible || _captions.Count == 0 || time >= _captions[^1].End)
        {
            // A static pose has no loop boundary to deliver a completion event.
            if (_releasePreviewAtCaptionEnd && !ModelPreview.FinishLoop())
                _previewPhase = PreviewPhase.WaitingForCaptions;
            StopModelCaptions();
            if (_previewPhase == PreviewPhase.WaitingForCaptions) PreviewIdle();
            return;
        }
        ModelPreviewDialogue.Text = _captions.FirstOrDefault(caption => time >= caption.Start && time < caption.End).Text ?? string.Empty;
    }

    private void StopModelCaptions()
    {
        _releasePreviewAtCaptionEnd = false;
        _captionTimer.Stop();
        _captionClock.Reset();
        _captions.Clear();
        ModelPreviewDialogue.Text = string.Empty;
    }

    private void ModelPreviewCompleted(object? sender, EventArgs e)
    {
        if (_captions.Count > 0 && !PreviewIsEntrance)
            _previewPhase = PreviewPhase.WaitingForCaptions;
        else
            PreviewIdle();
    }

    private bool PreviewIsEntrance => SceneList.SelectedItem is AnimationScene scene
        && scene.Id == GetAssignmentId(StartCombo, EditingScenes.StartAnimationId);

    private void PreviewIdle()
    {
        var idle = EditingScenes.FindAnimation(GetAssignmentId(IdleDefaultCombo, EditingScenes.IdleDefaultAnimationId));
        if (idle == null || SceneList.SelectedItem is not AnimationScene) return;
        var role = PreviewIsEntrance ? AnimationSceneRole.Start : AnimationSceneRole.IdleRandom;
        _previewPhase = PreviewPhase.Idle;
        PlayModelPreview(ModelPreview, idle, loop: true, ScenePlaybackPolicy.ModelBlendSeconds(role, AnimationSceneRole.IdleDefault));
    }

    private void ClickAreaModel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && _appearanceLoaded) RefreshClickAreaPreview();
    }

    private void ClickAreaImage_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!_appearanceLoaded || IsModel) return;
        if (e.NewValue is true) RefreshClickAreaPreview();
        else _clickAreaPreviewPlayer.Stop(clearFrame: true);
    }

    private bool PlayModelPreview(CompanionModelView view, AnimationScene? scene, bool loop = true, float blendSeconds = 0)
    {
        if (scene is null || _companion.Model3D.FilePath is not { } path)
        {
            view.Clear();
            return false;
        }
        try { view.PlayScene(path, scene, loop, blendSeconds); return true; }
        catch (Exception error) when (GlbModelFile.IsLoadError(error))
        {
            view.Clear();
            ReportModelError(error);
            return false;
        }
    }

    private void ReportModelError(Exception error)
    {
        ModelPreview.Clear();
        AppLogger.Warning("The model preview could not be loaded.", error);
        SetValidationHint(LocalizationService.Get("Model3D.Invalid"));
    }
}
