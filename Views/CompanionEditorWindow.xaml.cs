using Microsoft.Win32;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rectangle = System.Windows.Shapes.Rectangle;
using Shape = System.Windows.Shapes.Shape;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Animation;
using DesktopFamiliar.Services.Validation;

namespace DesktopFamiliar.Views;

public partial class CompanionEditorWindow : Window
{
    private sealed record BubbleChoice(string Label, string? Path);
    private sealed record FrameChoice(int Index, string Label);
    private sealed class SceneAssignmentChoice(Guid? id, string displayName)
    {
        public Guid? Id { get; } = id;
        public string DisplayName { get; set; } = displayName;
    }

    private readonly CompanionManager _manager;
    private readonly CompanionStorage _storage;
    private readonly CompanionDefinition _companion;
    private readonly bool _isNew;
    private readonly FrameAnimationPlayer _previewPlayer;
    private readonly FrameAnimationPlayer _companionPreviewPlayer;
    private readonly FrameAnimationPlayer _clickAreaPreviewPlayer;
    private bool _loadingBubbleChoices;
    private bool _updatingFramePlayback;
    private bool _updatingClickAreaEditor;
    private Point? _clickAreaSelectionStart;
    private Rectangle? _clickAreaSelectionRectangle;

    public CompanionEditorWindow(CompanionManager manager, CompanionStorage storage, CompanionDefinition? source)
    {
        InitializeComponent();
        _previewPlayer = new FrameAnimationPlayer(frame =>
        {
            FramePreviewImage.Source = frame;
            FramePreviewHint.Visibility = frame is null ? Visibility.Visible : Visibility.Collapsed;
        });
        _companionPreviewPlayer = new FrameAnimationPlayer(frame =>
        {
            CompanionPreviewImage.Source = frame;
            CompanionPreviewHint.Visibility = frame is null ? Visibility.Visible : Visibility.Collapsed;
        });
        _clickAreaPreviewPlayer = new FrameAnimationPlayer(frame =>
        {
            ClickAreaPreviewImage.Source = frame;
            ClickAreaPreviewHint.Visibility = frame is null ? Visibility.Visible : Visibility.Collapsed;
        });
        _manager = manager;
        _storage = storage;
        _isNew = source is null;
        _companion = source?.Clone() ?? new CompanionDefinition();
        BannerTitle.Text = _isNew
            ? LocalizationService.Get("Editor.Banner.Create")
            : LocalizationService.Format("Editor.Banner.Configure", source!.Name);
        SaveButton.Content = LocalizationService.Get(_isNew ? "Editor.Save.Create" : "Editor.Save.Update");

        FontCombo.ItemsSource = Fonts.SystemFontFamilies.Select(font => font.Source).OrderBy(name => name).ToList();
        FontCombo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(FontCombo_TextChanged));
        NameBox.Text = _companion.Name;
        if (DateTime.TryParse(_companion.Birthday, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var birthday)
            || DateTime.TryParse(_companion.Birthday, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out birthday))
            BirthdayPicker.SelectedDate = birthday;
        UserNicknameBox.Text = _companion.UserNickname;
        FontCombo.Text = _companion.FontFamilyName;
        ImportedFontLabel.Text = string.IsNullOrWhiteSpace(_companion.ImportedFontPath)
            ? string.Empty
            : LocalizationService.Format("Editor.ImportedFile", Path.GetFileName(_companion.ImportedFontPath));
        UpdateColorPreview();
        LoadBubbleChoices();
        UpdateChatPreview();
        RefreshSceneSelectors();
        if (_companion.Animations.Count > 0)
            SceneList.SelectedIndex = 0;
        Closed += (_, _) =>
        {
            _previewPlayer.Dispose();
            _companionPreviewPlayer.Dispose();
            _clickAreaPreviewPlayer.Dispose();
        };
    }

    private void BirthdayPicker_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DatePicker picker)
            return;

        Dispatcher.BeginInvoke(new Action(() => picker.IsDropDownOpen = true));
    }

    private void LoadBubbleChoices()
    {
        _loadingBubbleChoices = true;
        var choices = new List<BubbleChoice>
        {
            new(LocalizationService.Get("Editor.ChatBubbleDefaultChoice"), null)
        };
        choices.AddRange(_storage.GetImportedBubbleImages(_manager.Data)
            .Select(path => new BubbleChoice(Path.GetFileName(path), path)));
        if (!string.IsNullOrWhiteSpace(_companion.BubbleImagePath) && choices.All(choice => !string.Equals(choice.Path, _companion.BubbleImagePath, StringComparison.OrdinalIgnoreCase)))
            choices.Add(new BubbleChoice(Path.GetFileName(_companion.BubbleImagePath), _companion.BubbleImagePath));
        BubbleCombo.ItemsSource = choices;
        BubbleCombo.SelectedItem = choices.FirstOrDefault(choice => string.Equals(choice.Path, _companion.BubbleImagePath, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
        _loadingBubbleChoices = false;
        UpdateChatPreview();
    }

    private void RefreshSceneSelectors(Guid? selectedId = null)
    {
        selectedId ??= (SceneList.SelectedItem as AnimationScene)?.Id;
        SceneList.ItemsSource = null;
        SceneList.ItemsSource = _companion.Animations;
        SceneList.SelectedItem = _companion.Animations.FirstOrDefault(scene => scene.Id == selectedId);

        RefreshAssignmentCombo(StartCombo, GetAssignmentId(StartCombo, _companion.StartAnimationId));
        RefreshAssignmentCombo(ReminderCombo, GetAssignmentId(ReminderCombo, _companion.ReminderAnimationId));
        RefreshAssignmentCombo(IdleDefaultCombo, GetAssignmentId(IdleDefaultCombo, _companion.IdleDefaultAnimationId));
        RefreshAssignmentCombo(ExitCombo, GetAssignmentId(ExitCombo, _companion.ExitAnimationId));
        RefreshAssignmentCombo(DragLeftCombo, GetAssignmentId(DragLeftCombo, _companion.InteractionStates.DragLeftAnimationId));
        RefreshAssignmentCombo(DragRightCombo, GetAssignmentId(DragRightCombo, _companion.InteractionStates.DragRightAnimationId));
        RefreshAssignmentCombo(DragHoldCombo, GetAssignmentId(DragHoldCombo, _companion.InteractionStates.DragHoldAnimationId));
        RefreshAssignmentCombo(SelfBirthday1Combo, GetAssignmentId(SelfBirthday1Combo, _companion.SelfBirthdayAnimation1Id));
        RefreshAssignmentCombo(SelfBirthday2Combo, GetAssignmentId(SelfBirthday2Combo, _companion.SelfBirthdayAnimation2Id));
        RefreshAssignmentCombo(UserBirthday1Combo, GetAssignmentId(UserBirthday1Combo, _companion.UserBirthdayAnimation1Id));
        RefreshAssignmentCombo(UserBirthday2Combo, GetAssignmentId(UserBirthday2Combo, _companion.UserBirthdayAnimation2Id));

        IdleRandomChecksPanel.Children.Clear();
        foreach (var scene in _companion.Animations)
        {
            var check = new CheckBox
            {
                Content = scene.DisplayName,
                Tag = scene.Id,
                IsChecked = _companion.IdleRandomAnimationIds.Contains(scene.Id),
                Margin = new Thickness(0, 4, 18, 8),
                VerticalAlignment = VerticalAlignment.Center
            };
            IdleRandomChecksPanel.Children.Add(check);
        }
        RefreshClickAreaEditor();
        RefreshCompanionPreview();
    }

    private void RefreshAssignmentCombo(ComboBox comboBox, Guid? selectedId)
    {
        var choices = new List<SceneAssignmentChoice>
        {
            new(null, LocalizationService.Get("Editor.StateAssignment.None"))
        };
        choices.AddRange(_companion.Animations.Select(scene =>
            new SceneAssignmentChoice(scene.Id, scene.DisplayName)));
        comboBox.ItemsSource = choices;
        comboBox.SelectedItem = choices.FirstOrDefault(choice => choice.Id == selectedId)
            ?? choices[0];
    }

    private static Guid? GetAssignmentId(ComboBox comboBox, Guid? fallback) =>
        comboBox.SelectedItem is SceneAssignmentChoice choice ? choice.Id : fallback;

    private void SceneList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        StopPreview();
        var scene = SceneList.SelectedItem as AnimationScene;
        SceneEditor.DataContext = scene;
        SceneEditor.Visibility = scene is null ? Visibility.Collapsed : Visibility.Visible;
        SceneEmpty.Visibility = scene is null ? Visibility.Visible : Visibility.Collapsed;
        DialogGrid.ItemsSource = scene?.DialogLines;
        UpdateAnimationSourceUi(scene);
    }

    private void UpdateAnimationSourceUi(AnimationScene? scene, int selectedFrame = -1)
    {
        AnimationFrameSettingsPolicy.ResetIfUnsupported(scene);
        var frameChoices = CreateFrameChoices(scene);
        NormalizeFramePlaybackSettings(scene, frameChoices.Count);
        SourceSummaryText.Text = scene?.SourceSummary ?? string.Empty;
        FrameGrid.ItemsSource = null;
        FrameGrid.ItemsSource = scene?.PngFrames;
        var hasSequence = scene?.PngFrames.Count > 0;
        FrameSequencePanel.Visibility = hasSequence ? Visibility.Visible : Visibility.Collapsed;
        FrameSequenceTitle.Text = scene is null
            ? LocalizationService.Get("Editor.PngSequence.Title")
            : LocalizationService.Format("Editor.PngSequence.TitleWithCount", scene.PngFrames.Count);
        PreviewButton.IsEnabled = scene?.PngFrames.Count >= 1;
        if (selectedFrame >= 0 && scene is not null && selectedFrame < scene.PngFrames.Count)
            FrameGrid.SelectedIndex = selectedFrame;
        RefreshFramePlaybackOptions(scene, frameChoices);
        ShowSelectedFramePreview();
        RefreshCompanionPreview();
    }

    private static void NormalizeFramePlaybackSettings(AnimationScene? scene, int frameCount)
    {
        if (scene is null || frameCount == 0)
        {
            AnimationFrameSettingsPolicy.ResetIfUnsupported(scene);
            return;
        }

        if (!Enum.IsDefined(scene.FrameLoopMode))
            scene.FrameLoopMode = AnimationFrameLoopMode.EntireAnimation;
        scene.LoopStartFrameIndex = Math.Clamp(scene.LoopStartFrameIndex, 0, frameCount - 1);
        scene.LoopEndFrameIndex = Math.Clamp(scene.LoopEndFrameIndex, 0, frameCount - 1);
        if (scene.FrameLoopMode == AnimationFrameLoopMode.SingleFrame)
            scene.LoopEndFrameIndex = scene.LoopStartFrameIndex;
        else if (scene.LoopStartFrameIndex > scene.LoopEndFrameIndex)
            scene.LoopEndFrameIndex = scene.LoopStartFrameIndex;
        scene.DialogStartFrameIndex = Math.Clamp(
            scene.DialogStartFrameIndex,
            0,
            scene.GetLastFrameReachableBeforeLoopRepeats(frameCount));
    }

    private IReadOnlyList<FrameChoice> CreateFrameChoices(AnimationScene? scene)
    {
        if (scene is null)
            return [];

        return Enumerable.Range(0, scene.PngFrames.Count)
            .Select(index => new FrameChoice(
                index,
                LocalizationService.Format("Editor.PngSequence.FrameNumber", index + 1)))
            .ToList();
    }

    private void RefreshFramePlaybackOptions(AnimationScene? scene, IReadOnlyList<FrameChoice> frameChoices)
    {
        _updatingFramePlayback = true;
        try
        {
            LoopStartCombo.ItemsSource = frameChoices;
            LoopEndCombo.ItemsSource = frameChoices;
            var lastReachableFrame = scene?.GetLastFrameReachableBeforeLoopRepeats(frameChoices.Count) ?? 0;
            DialogStartFrameCombo.ItemsSource = frameChoices
                .Where(choice => choice.Index <= lastReachableFrame)
                .ToList();
            FrameLoopModeCombo.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateTarget();
            LoopStartCombo.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateTarget();
            LoopEndCombo.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateTarget();
            DialogStartFrameCombo.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateTarget();
            var hasPngFrames = AnimationFrameSettingsPolicy.SupportsIndividualFrames(scene);
            FrameLoopPanel.Visibility = hasPngFrames
                ? Visibility.Visible
                : Visibility.Collapsed;
            FrameLoopOptions.IsEnabled = frameChoices.Count > 0;
            var mode = scene?.FrameLoopMode ?? AnimationFrameLoopMode.EntireAnimation;
            LoopStartCombo.Visibility = mode == AnimationFrameLoopMode.EntireAnimation ? Visibility.Collapsed : Visibility.Visible;
            LoopStartLabel.Visibility = LoopStartCombo.Visibility;
            LoopStartLabel.Text = LocalizationService.Get(mode == AnimationFrameLoopMode.SingleFrame
                ? "Editor.PngSequence.FrameLoop.Frame"
                : "Editor.PngSequence.FrameLoop.Start");
            LoopEndPanel.Visibility = mode == AnimationFrameLoopMode.SelectedRange
                ? Visibility.Visible
                : Visibility.Collapsed;
            DialogStartFrameCombo.IsEnabled = frameChoices.Count > 0;
            DialogStartFramePanel.Visibility = hasPngFrames
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        finally
        {
            _updatingFramePlayback = false;
        }
    }

    private void FrameLoopMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingFramePlayback || SceneList.SelectedItem is not AnimationScene scene)
            return;
        if (FrameLoopModeCombo.SelectedValue is AnimationFrameLoopMode mode)
            scene.FrameLoopMode = mode;
        var frameChoices = CreateFrameChoices(scene);
        NormalizeFramePlaybackSettings(scene, frameChoices.Count);
        RefreshFramePlaybackOptions(scene, frameChoices);
    }

    private void LoopBoundary_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingFramePlayback || SceneList.SelectedItem is not AnimationScene scene)
            return;
        if (LoopStartCombo.SelectedValue is int start)
            scene.LoopStartFrameIndex = start;
        if (LoopEndCombo.SelectedValue is int end)
            scene.LoopEndFrameIndex = end;
        var frameChoices = CreateFrameChoices(scene);
        NormalizeFramePlaybackSettings(scene, frameChoices.Count);
        RefreshFramePlaybackOptions(scene, frameChoices);
    }

    private void StateAssignment_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshCompanionPreview();
        RefreshClickAreaPreview();
    }

    private void RefreshCompanionPreview()
    {
        _companionPreviewPlayer.Stop(clearFrame: true);
        var scene = _companion.FindAnimation(GetAssignmentId(IdleDefaultCombo, _companion.IdleDefaultAnimationId))
            ?? _companion.FindAnimation(GetAssignmentId(StartCombo, _companion.StartAnimationId))
            ?? _companion.Animations.FirstOrDefault();
        if (scene is null)
            return;

        try
        {
            _companionPreviewPlayer.Play(scene, loop: true);
        }
        catch (Exception exception)
        {
            // A source can be incomplete while it is being edited; the hint remains visible.
            AppLogger.Warning("The in-editor companion preview could not be played.", exception);
            _companionPreviewPlayer.Stop(clearFrame: true);
        }
    }

    private void RefreshClickAreaEditor(Guid? selectedId = null)
    {
        _updatingClickAreaEditor = true;
        try
        {
            selectedId ??= (ClickAreaList.SelectedItem as CompanionClickArea)?.Id;
            ClickAreaList.ItemsSource = null;
            ClickAreaList.ItemsSource = _companion.InteractionStates.ClickAreas;
            ClickAreaAnimationCombo.ItemsSource = CreateSceneAssignmentChoices();
            ClickAreaList.SelectedItem = _companion.InteractionStates.ClickAreas
                .FirstOrDefault(area => area.Id == selectedId)
                ?? _companion.InteractionStates.ClickAreas.FirstOrDefault();
            UpdateClickAreaSelectionFields();
            RenderClickAreas();
        }
        finally
        {
            _updatingClickAreaEditor = false;
        }
        RefreshClickAreaPreview();
    }

    private IReadOnlyList<SceneAssignmentChoice> CreateSceneAssignmentChoices()
    {
        var choices = new List<SceneAssignmentChoice>
        {
            new(null, LocalizationService.Get("Editor.StateAssignment.None"))
        };
        choices.AddRange(_companion.Animations.Select(scene => new SceneAssignmentChoice(scene.Id, scene.DisplayName)));
        return choices;
    }

    private void RefreshClickAreaPreview()
    {
        _clickAreaPreviewPlayer.Stop(clearFrame: true);
        var scene = _companion.FindAnimation(GetAssignmentId(IdleDefaultCombo, _companion.IdleDefaultAnimationId));
        if (scene is null)
            return;

        try
        {
            _clickAreaPreviewPlayer.Play(scene, loop: true);
        }
        catch (Exception exception)
        {
            AppLogger.Warning("The click-area idle preview could not be played.", exception);
            _clickAreaPreviewPlayer.Stop(clearFrame: true);
        }
    }

    private void ClickAreaList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingClickAreaEditor)
            return;
        UpdateClickAreaSelectionFields();
        RenderClickAreas();
    }

    private void UpdateClickAreaSelectionFields()
    {
        var area = ClickAreaList.SelectedItem as CompanionClickArea;
        ClickAreaNameBox.IsEnabled = area is not null;
        ClickAreaAnimationCombo.IsEnabled = area is not null;
        ClickAreaNameBox.Text = area?.Name ?? string.Empty;
        ClickAreaAnimationCombo.SelectedItem = ClickAreaAnimationCombo.Items
            .OfType<SceneAssignmentChoice>()
            .FirstOrDefault(choice => choice.Id == area?.AnimationId)
            ?? ClickAreaAnimationCombo.Items.OfType<SceneAssignmentChoice>().FirstOrDefault();
    }

    private void ClickAreaNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingClickAreaEditor || ClickAreaList.SelectedItem is not CompanionClickArea area)
            return;
        area.Name = ClickAreaNameBox.Text.Trim();
        ClickAreaList.Items.Refresh();
        RenderClickAreas();
    }

    private void ClickAreaAnimation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingClickAreaEditor || ClickAreaList.SelectedItem is not CompanionClickArea area)
            return;
        area.AnimationId = GetAssignmentId(ClickAreaAnimationCombo, null);
    }

    private void RemoveClickArea_Click(object sender, RoutedEventArgs e)
    {
        if (ClickAreaList.SelectedItem is not CompanionClickArea area)
            return;
        _companion.InteractionStates.ClickAreas.Remove(area);
        RefreshClickAreaEditor();
    }

    private void ClickAreaPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _clickAreaSelectionStart = e.GetPosition(ClickAreaSelectionCanvas);
        _clickAreaSelectionRectangle = new Rectangle
        {
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };
        _clickAreaSelectionRectangle.SetResourceReference(Shape.StrokeProperty, ThemeTokens.PrimaryAccent);
        ClickAreaSelectionCanvas.Children.Add(_clickAreaSelectionRectangle);
        ClickAreaSelectionCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void ClickAreaPreview_MouseMove(object sender, MouseEventArgs e)
    {
        if (_clickAreaSelectionStart is not Point start || _clickAreaSelectionRectangle is null)
            return;
        UpdateSelectionRectangle(start, e.GetPosition(ClickAreaSelectionCanvas), _clickAreaSelectionRectangle);
        e.Handled = true;
    }

    private void ClickAreaPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_clickAreaSelectionStart is not Point start || _clickAreaSelectionRectangle is null)
            return;

        var end = e.GetPosition(ClickAreaSelectionCanvas);
        var width = Math.Abs(end.X - start.X);
        var height = Math.Abs(end.Y - start.Y);
        ClickAreaSelectionCanvas.Children.Remove(_clickAreaSelectionRectangle);
        ClickAreaSelectionCanvas.ReleaseMouseCapture();
        _clickAreaSelectionRectangle = null;
        _clickAreaSelectionStart = null;
        if (width < 6 || height < 6 || ClickAreaSelectionCanvas.ActualWidth <= 0 || ClickAreaSelectionCanvas.ActualHeight <= 0)
            return;

        var area = new CompanionClickArea
        {
            Name = LocalizationService.Format("Editor.ClickAreas.DefaultName", _companion.InteractionStates.ClickAreas.Count + 1),
            LeftPercent = Math.Min(start.X, end.X) / ClickAreaSelectionCanvas.ActualWidth * 100d,
            TopPercent = Math.Min(start.Y, end.Y) / ClickAreaSelectionCanvas.ActualHeight * 100d,
            WidthPercent = width / ClickAreaSelectionCanvas.ActualWidth * 100d,
            HeightPercent = height / ClickAreaSelectionCanvas.ActualHeight * 100d
        };
        _companion.InteractionStates.ClickAreas.Add(area);
        RefreshClickAreaEditor(area.Id);
        e.Handled = true;
    }

    private void ClickAreaPreview_SizeChanged(object sender, SizeChangedEventArgs e) => RenderClickAreas();

    private void RenderClickAreas()
    {
        ClickAreaSelectionCanvas.Children.Clear();
        if (ClickAreaSelectionCanvas.ActualWidth <= 0 || ClickAreaSelectionCanvas.ActualHeight <= 0)
            return;

        foreach (var area in _companion.InteractionStates.ClickAreas)
        {
            var rectangle = new Rectangle
            {
                Width = area.WidthPercent / 100d * ClickAreaSelectionCanvas.ActualWidth,
                Height = area.HeightPercent / 100d * ClickAreaSelectionCanvas.ActualHeight,
                StrokeThickness = 2,
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };
            rectangle.SetResourceReference(Shape.StrokeProperty, ThemeTokens.PrimaryAccent);
            Canvas.SetLeft(rectangle, area.LeftPercent / 100d * ClickAreaSelectionCanvas.ActualWidth);
            Canvas.SetTop(rectangle, area.TopPercent / 100d * ClickAreaSelectionCanvas.ActualHeight);
            ClickAreaSelectionCanvas.Children.Add(rectangle);
        }
    }

    private static void UpdateSelectionRectangle(Point start, Point end, Rectangle rectangle)
    {
        Canvas.SetLeft(rectangle, Math.Min(start.X, end.X));
        Canvas.SetTop(rectangle, Math.Min(start.Y, end.Y));
        rectangle.Width = Math.Abs(end.X - start.X);
        rectangle.Height = Math.Abs(end.Y - start.Y);
    }

    private void AddScene_Click(object sender, RoutedEventArgs e)
    {
        var scene = new AnimationScene
        {
            Name = LocalizationService.Format("Editor.Animation.DefaultNumberedName", _companion.Animations.Count + 1)
        };
        _companion.Animations.Add(scene);
        RefreshSceneSelectors(scene.Id);
    }

    private void DeleteScene_Click(object sender, RoutedEventArgs e)
    {
        if (SceneList.SelectedItem is not AnimationScene scene)
            return;
        var result = MessageBox.Show(
            LocalizationService.Format("Editor.DeleteAnimation.Message", scene.DisplayName),
            LocalizationService.Get("Editor.DeleteAnimation.Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
            return;
        _companion.Animations.Remove(scene);
        _companion.IdleRandomAnimationIds.Remove(scene.Id);
        if (_companion.StartAnimationId == scene.Id)
            _companion.StartAnimationId = null;
        if (_companion.ReminderAnimationId == scene.Id)
            _companion.ReminderAnimationId = null;
        if (_companion.IdleDefaultAnimationId == scene.Id)
            _companion.IdleDefaultAnimationId = null;
        if (_companion.SelfBirthdayAnimation1Id == scene.Id)
            _companion.SelfBirthdayAnimation1Id = null;
        if (_companion.SelfBirthdayAnimation2Id == scene.Id)
            _companion.SelfBirthdayAnimation2Id = null;
        if (_companion.UserBirthdayAnimation1Id == scene.Id)
            _companion.UserBirthdayAnimation1Id = null;
        if (_companion.UserBirthdayAnimation2Id == scene.Id)
            _companion.UserBirthdayAnimation2Id = null;
        if (_companion.ExitAnimationId == scene.Id)
            _companion.ExitAnimationId = null;
        _companion.InteractionStates.ClearAnimationId(scene.Id);
        RefreshSceneSelectors(_companion.Animations.FirstOrDefault()?.Id);
    }

    private void SceneNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || SceneList.SelectedItem is not AnimationScene scene)
            return;
        Dispatcher.BeginInvoke(() =>
        {
            SceneList.Items.Refresh();
            foreach (var comboBox in GetAssignmentCombos())
            {
                if (comboBox.Items.OfType<SceneAssignmentChoice>()
                    .FirstOrDefault(choice => choice.Id == scene.Id) is { } choice)
                {
                    choice.DisplayName = scene.DisplayName;
                }
                comboBox.Items.Refresh();
            }
            if (ClickAreaAnimationCombo.Items.OfType<SceneAssignmentChoice>()
                .FirstOrDefault(choice => choice.Id == scene.Id) is { } clickAreaChoice)
            {
                clickAreaChoice.DisplayName = scene.DisplayName;
                ClickAreaAnimationCombo.Items.Refresh();
            }
            var check = IdleRandomChecksPanel.Children.OfType<CheckBox>().FirstOrDefault(item => (Guid)item.Tag == scene.Id);
            if (check is not null)
                check.Content = scene.DisplayName;
            RefreshCompanionPreview();
        });
    }

    private void FontCombo_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_companion.ImportedFontPath))
            return;
        if (string.Equals(FontCombo.Text.Trim(), _companion.FontFamilyName, StringComparison.OrdinalIgnoreCase))
            return;
        _companion.ImportedFontPath = null;
        ImportedFontLabel.Text = string.Empty;
        UpdateChatPreview();
    }

    private void FontCombo_TextChanged(object sender, TextChangedEventArgs e) => UpdateChatPreview();

    private void ImportAnimation_Click(object sender, RoutedEventArgs e)
    {
        if (SceneList.SelectedItem is not AnimationScene scene)
            return;
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Get("FileDialog.Animation.Title"),
            Filter = LocalizationService.Get("FileDialog.Animation.Filter")
        };
        if (dialog.ShowDialog(this) != true)
            return;
        if (scene.PngFrames.Count > 0)
        {
            var switchSource = MessageBox.Show(
                LocalizationService.Get("Editor.ChangeSource.ToFile"),
                LocalizationService.Get("Editor.ChangeSource.Title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (switchSource != MessageBoxResult.Yes)
                return;
        }
        try
        {
            StopPreview();
            scene.AnimationPath = _storage.ImportAsset(_companion.Id, dialog.FileName, "Animations");
            scene.PngFrames.Clear();
            AnimationFrameSettingsPolicy.Reset(scene);
            UpdateAnimationSourceUi(scene);
        }
        catch (Exception exception)
        {
            AppLogger.Error("A GIF or single-image animation could not be imported.", exception);
            MessageBox.Show(exception.Message, LocalizationService.Get("Editor.ImportFailed.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportPngFrames_Click(object sender, RoutedEventArgs e)
    {
        if (SceneList.SelectedItem is not AnimationScene scene)
            return;
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Get("FileDialog.PngFrames.Title"),
            Filter = LocalizationService.Get("FileDialog.PngFrames.Filter"),
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var selectedFiles = dialog.FileNames
            .OrderBy(NaturalSortKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!TryReadFrameDuration(out var duration))
            return;
        var switchingFromFileSource = !string.IsNullOrWhiteSpace(scene.AnimationPath)
            && scene.PngFrames.Count == 0;
        if (switchingFromFileSource)
        {
            var switchSource = MessageBox.Show(
                LocalizationService.Get("Editor.ChangeSource.ToPng"),
                LocalizationService.Get("Editor.ChangeSource.Title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (switchSource != MessageBoxResult.Yes)
                return;
        }

        try
        {
            ValidateMatchingPngDimensions(scene.PngFrames.Select(frame => frame.FilePath).Concat(selectedFiles));
            var importedFrames = selectedFiles.Select(file => new PngAnimationFrame
            {
                FilePath = _storage.ImportAsset(_companion.Id, file, "PngSequences"),
                DurationSeconds = duration
            }).ToList();
            StopPreview();
            var firstNewIndex = scene.PngFrames.Count;
            scene.AnimationPath = string.Empty;
            if (switchingFromFileSource)
                AnimationFrameSettingsPolicy.Reset(scene);
            scene.PngFrames.AddRange(importedFrames);
            UpdateAnimationSourceUi(scene, firstNewIndex);
        }
        catch (Exception exception)
        {
            AppLogger.Error("A PNG animation sequence could not be imported.", exception);
            MessageBox.Show(
                LocalizationService.Format("Editor.PngSequence.ImportFailed", exception.Message),
                LocalizationService.Get("Editor.ImportFailed.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string NaturalSortKey(string path) => Regex.Replace(
        Path.GetFileName(path),
        @"\d+",
        match => match.Value.PadLeft(20, '0'));

    private static void ValidateMatchingPngDimensions(IEnumerable<string> paths)
    {
        (int Width, int Height)? expected = null;
        string? expectedName = null;
        foreach (var path in paths)
        {
            if (!Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(LocalizationService.Format(
                    "Editor.PngSequence.NotPng",
                    Path.GetFileName(path)));
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
                throw new InvalidDataException(LocalizationService.Format(
                    "Editor.PngSequence.NoReadableFrame",
                    Path.GetFileName(path)));
            var size = (decoder.Frames[0].PixelWidth, decoder.Frames[0].PixelHeight);
            if (expected is null)
            {
                expected = size;
                expectedName = Path.GetFileName(path);
                continue;
            }
            if (size != expected.Value)
            {
                throw new InvalidDataException(LocalizationService.Format(
                    "Editor.PngSequence.DimensionMismatch",
                    expectedName,
                    expected.Value.Width,
                    expected.Value.Height,
                    Path.GetFileName(path),
                    size.PixelWidth,
                    size.PixelHeight));
            }
        }
    }

    private bool TryReadFrameDuration(out double duration)
    {
        if (TryParseFrameDuration(FrameDurationBox.Text, out duration))
            return true;
        ShowInvalidFrameDuration();
        return false;
    }

    private static void ShowInvalidFrameDuration() => MessageBox.Show(
        LocalizationService.Get("Editor.FrameDuration.Invalid"),
        LocalizationService.Get("Editor.FrameDuration.InvalidTitle"),
        MessageBoxButton.OK,
        MessageBoxImage.Warning);

    private static bool TryParseFrameDuration(string text, out double duration)
    {
        var parsed = double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out duration)
            || double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
        return parsed && duration is >= 0.01 and <= 60;
    }

    private void ApplyFrameDuration_Click(object sender, RoutedEventArgs e)
    {
        if (SceneList.SelectedItem is not AnimationScene scene || !TryReadFrameDuration(out var duration))
            return;
        if (!CommitFrameEdits())
            return;
        StopPreview();
        foreach (var frame in scene.PngFrames)
            frame.DurationSeconds = duration;
        FrameGrid.Items.Refresh();
    }

    private void MoveFrameUp_Click(object sender, RoutedEventArgs e) => MoveSelectedFrame(-1);

    private void MoveFrameDown_Click(object sender, RoutedEventArgs e) => MoveSelectedFrame(1);

    private void MoveSelectedFrame(int offset)
    {
        if (SceneList.SelectedItem is not AnimationScene scene || FrameGrid.SelectedItem is not PngAnimationFrame frame)
            return;
        if (!CommitFrameEdits())
            return;
        var currentIndex = scene.PngFrames.IndexOf(frame);
        var newIndex = currentIndex + offset;
        if (newIndex < 0 || newIndex >= scene.PngFrames.Count)
            return;
        StopPreview();
        scene.PngFrames.RemoveAt(currentIndex);
        scene.PngFrames.Insert(newIndex, frame);
        UpdateAnimationSourceUi(scene, newIndex);
    }

    private void RemoveFrame_Click(object sender, RoutedEventArgs e)
    {
        if (SceneList.SelectedItem is not AnimationScene scene || FrameGrid.SelectedItem is not PngAnimationFrame frame)
            return;
        if (!CommitFrameEdits())
            return;
        var index = scene.PngFrames.IndexOf(frame);
        StopPreview();
        scene.PngFrames.Remove(frame);
        UpdateAnimationSourceUi(scene, Math.Min(index, scene.PngFrames.Count - 1));
    }

    private void ClearSequence_Click(object sender, RoutedEventArgs e)
    {
        if (SceneList.SelectedItem is not AnimationScene scene || scene.PngFrames.Count == 0)
            return;
        var result = MessageBox.Show(
            LocalizationService.Get("Editor.PngSequence.ClearMessage"),
            LocalizationService.Get("Editor.PngSequence.ClearTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
            return;
        StopPreview();
        scene.PngFrames.Clear();
        UpdateAnimationSourceUi(scene);
    }

    private void PlayPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_previewPlayer.IsRunning)
        {
            StopPreview();
            return;
        }
        if (SceneList.SelectedItem is not AnimationScene scene)
            return;
        if (!CommitFrameEdits())
            return;
        try
        {
            _previewPlayer.Play(scene, loop: true);
            PreviewButton.Content = LocalizationService.Get("Editor.PngSequence.StopPreview");
        }
        catch (Exception exception)
        {
            StopPreview();
            AppLogger.Error("The animation preview could not be played.", exception);
            MessageBox.Show(exception.Message, LocalizationService.Get("Editor.PreviewFailed.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopPreview()
    {
        _previewPlayer.Stop(clearFrame: true);
        PreviewButton.Content = LocalizationService.Get("Editor.PngSequence.PlayPreview");
        ShowSelectedFramePreview();
    }

    private bool CommitFrameEdits()
    {
        if (!FrameGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true))
            return false;
        return FrameGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
    }

    private void FrameGrid_LoadingRow(object sender, DataGridRowEventArgs e) => e.Row.Header = (e.Row.GetIndex() + 1).ToString(CultureInfo.InvariantCulture);

    private void DataGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not DataGrid grid || grid.ActualWidth <= 0 || grid.ActualHeight <= 0)
            return;

        // A WPF Border does not clip a child to its CornerRadius. Clip the
        // entire DataGrid surface so its header, rows, and scrollbar all share
        // the same four rounded corners as the surrounding border.
        grid.Clip = new RectangleGeometry(new Rect(0, 0, grid.ActualWidth, grid.ActualHeight), 9, 9);
    }

    private void FrameGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_previewPlayer.IsRunning)
            StopPreview();
        else
            ShowSelectedFramePreview();
    }

    private void ShowSelectedFramePreview()
    {
        if (FrameGrid.SelectedItem is not PngAnimationFrame frame || string.IsNullOrWhiteSpace(frame.FilePath))
        {
            FramePreviewImage.Source = null;
            FramePreviewHint.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            using var stream = new FileStream(frame.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var bitmap = BitmapFrame.Create(decoder.Frames[0]);
            bitmap.Freeze();
            FramePreviewImage.Source = bitmap;
            FramePreviewHint.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Could not load the selected PNG frame preview: {frame.FilePath}", exception);
            FramePreviewImage.Source = null;
            FramePreviewHint.Visibility = Visibility.Visible;
        }
    }

    private void FrameGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.Column.DisplayIndex == 1 && e.EditingElement is TextBox textBox)
        {
            if (!TryParseFrameDuration(textBox.Text, out var duration))
            {
                e.Cancel = true;
                ShowInvalidFrameDuration();
                return;
            }
            if (e.Row.Item is PngAnimationFrame frame)
                frame.DurationSeconds = duration;
        }
        if (_previewPlayer.IsRunning)
            Dispatcher.BeginInvoke(StopPreview);
    }

    private void AddDialog_Click(object sender, RoutedEventArgs e)
    {
        if (SceneList.SelectedItem is not AnimationScene scene)
            return;
        scene.DialogLines.Add(new DialogLine { Text = LocalizationService.Get("Editor.Dialog.DefaultLine") });
        DialogGrid.Items.Refresh();
        DialogGrid.SelectedIndex = scene.DialogLines.Count - 1;
    }

    private void RemoveDialog_Click(object sender, RoutedEventArgs e)
    {
        if (SceneList.SelectedItem is not AnimationScene scene || DialogGrid.SelectedItem is not DialogLine line)
            return;
        scene.DialogLines.Remove(line);
        DialogGrid.Items.Refresh();
    }

    private void AttachVoice_Click(object sender, RoutedEventArgs e)
    {
        if (DialogGrid.SelectedItem is not DialogLine line)
        {
            MessageBox.Show(
                LocalizationService.Get("Editor.Dialog.SelectFirst"),
                LocalizationService.Get("Editor.Dialog.AttachVoiceTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Get("FileDialog.Voice.Title"),
            Filter = LocalizationService.Get("FileDialog.Voice.Filter")
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            line.VoicePath = _storage.ImportAsset(_companion.Id, dialog.FileName, "Voice");
            DialogGrid.Items.Refresh();
        }
        catch (Exception exception)
        {
            AppLogger.Error("A voice line could not be imported.", exception);
            MessageBox.Show(exception.Message, LocalizationService.Get("Editor.ImportFailed.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportFont_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Get("FileDialog.Font.Title"),
            Filter = LocalizationService.Get("FileDialog.Font.Filter")
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            var imported = _storage.ImportAsset(_companion.Id, dialog.FileName, "Fonts");
            var family = Fonts.GetFontFamilies(new Uri(imported, UriKind.Absolute)).FirstOrDefault();
            _companion.ImportedFontPath = imported;
            _companion.FontFamilyName = family?.Source ?? Path.GetFileNameWithoutExtension(imported);
            FontCombo.Text = _companion.FontFamilyName;
            ImportedFontLabel.Text = LocalizationService.Format("Editor.ImportedFile", Path.GetFileName(imported));
            UpdateChatPreview();
        }
        catch (Exception exception)
        {
            AppLogger.Error("A font could not be imported.", exception);
            MessageBox.Show(
                LocalizationService.Format("Editor.Font.ImportFailed", exception.Message),
                LocalizationService.Get("Editor.ImportFailed.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ChooseColor_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ColorWheelWindow(_companion.FontColor) { Owner = this };
        if (picker.ShowDialog() != true)
            return;
        _companion.FontColor = picker.SelectedColor.ToString();
        UpdateColorPreview();
    }

    private void UpdateColorPreview()
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(_companion.FontColor);
            ColorPreview.Background = new SolidColorBrush(color);
            ColorValue.Text = color.ToString();
        }
        catch (Exception exception)
        {
            AppLogger.Warning("The saved font color could not be shown in the editor.", exception);
            ColorPreview.Background = (Brush)FindResource(ThemeTokens.PrimaryBackground);
            ColorValue.Text = "#FFFFFFFF";
        }
        UpdateChatPreview();
    }

    private void UpdateChatPreview()
    {
        ChatPreviewText.Text = LocalizationService.Get("Editor.ChatPreview.Sample");
        ChatPreviewText.Foreground = GetPreviewTextBrush();
        ChatPreviewText.FontFamily = ResolvePreviewFont();

        ChatPreviewImage.Source = null;
        ChatPreviewImage.Visibility = Visibility.Collapsed;
        ChatPreviewBubble.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, ThemeTokens.OverlaySurface);
        ChatPreviewBubble.Padding = new Thickness(14, 10, 14, 10);

        if (string.IsNullOrWhiteSpace(_companion.BubbleImagePath) || !File.Exists(_companion.BubbleImagePath))
            return;

        try
        {
            using var stream = new FileStream(_companion.BubbleImagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
                return;

            var image = BitmapFrame.Create(decoder.Frames[0]);
            image.Freeze();
            ChatPreviewImage.Source = image;
            ChatPreviewImage.Visibility = Visibility.Visible;
            ChatPreviewBubble.Background = Brushes.Transparent;
            ChatPreviewBubble.Padding = new Thickness(20, 14, 20, 14);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"The chat-bubble preview '{_companion.BubbleImagePath}' could not be loaded.", exception);
        }
    }

    private Brush GetPreviewTextBrush()
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(_companion.FontColor);
            return new SolidColorBrush(color);
        }
        catch (Exception exception)
        {
            AppLogger.Warning("The chat preview used the fallback font color.", exception);
            return (Brush)FindResource(ThemeTokens.TextOnOverlay);
        }
    }

    private FontFamily ResolvePreviewFont()
    {
        var fontName = string.IsNullOrWhiteSpace(FontCombo.Text) ? "Segoe UI" : FontCombo.Text.Trim();
        if (!string.IsNullOrWhiteSpace(_companion.ImportedFontPath)
            && string.Equals(fontName, _companion.FontFamilyName, StringComparison.OrdinalIgnoreCase)
            && File.Exists(_companion.ImportedFontPath))
        {
            try
            {
                var folder = new Uri(Path.GetDirectoryName(_companion.ImportedFontPath)! + Path.DirectorySeparatorChar, UriKind.Absolute);
                return new FontFamily(folder, $"./#{fontName}");
            }
            catch (Exception exception)
            {
                AppLogger.Warning("The imported chat-preview font could not be loaded.", exception);
            }
        }

        try
        {
            return new FontFamily(fontName);
        }
        catch (Exception exception)
        {
            AppLogger.Warning("The chat preview used the fallback font family.", exception);
            return new FontFamily("Segoe UI");
        }
    }

    private void ImportBubble_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Get("FileDialog.Bubble.Title"),
            Filter = LocalizationService.Get("FileDialog.Bubble.Filter")
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            _companion.BubbleImagePath = _storage.ImportAsset(_companion.Id, dialog.FileName, "Bubbles");
            LoadBubbleChoices();
        }
        catch (Exception exception)
        {
            AppLogger.Error("A chat-bubble image could not be imported.", exception);
            MessageBox.Show(exception.Message, LocalizationService.Get("Editor.ImportFailed.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BubbleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingBubbleChoices)
            return;
        _companion.BubbleImagePath = (BubbleCombo.SelectedItem as BubbleChoice)?.Path;
        UpdateChatPreview();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!CommitFrameEdits())
            return;
        _companion.Name = NameBox.Text.Trim();
        _companion.Birthday = BirthdayPicker.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        _companion.UserNickname = UserNicknameBox.Text.Trim();
        _companion.FontFamilyName = string.IsNullOrWhiteSpace(FontCombo.Text) ? "Segoe UI" : FontCombo.Text.Trim();
        if (!string.Equals(_companion.FontFamilyName, FontCombo.Text, StringComparison.Ordinal))
            FontCombo.Text = _companion.FontFamilyName;
        if (string.IsNullOrWhiteSpace(ImportedFontLabel.Text))
            _companion.ImportedFontPath = null;
        _companion.StartAnimationId = GetAssignmentId(StartCombo, null);
        _companion.ReminderAnimationId = GetAssignmentId(ReminderCombo, null);
        _companion.IdleDefaultAnimationId = GetAssignmentId(IdleDefaultCombo, null);
        _companion.ExitAnimationId = GetAssignmentId(ExitCombo, null);
        _companion.InteractionStates.DragLeftAnimationId = GetAssignmentId(DragLeftCombo, null);
        _companion.InteractionStates.DragRightAnimationId = GetAssignmentId(DragRightCombo, null);
        _companion.InteractionStates.DragHoldAnimationId = GetAssignmentId(DragHoldCombo, null);
        _companion.SelfBirthdayAnimation1Id = GetAssignmentId(SelfBirthday1Combo, null);
        _companion.SelfBirthdayAnimation2Id = GetAssignmentId(SelfBirthday2Combo, null);
        _companion.UserBirthdayAnimation1Id = GetAssignmentId(UserBirthday1Combo, null);
        _companion.UserBirthdayAnimation2Id = GetAssignmentId(UserBirthday2Combo, null);
        _companion.IdleRandomAnimationIds = IdleRandomChecksPanel.Children.OfType<CheckBox>()
            .Where(check => check.IsChecked == true)
            .Select(check => (Guid)check.Tag)
            .ToList();

        var errors = CompanionValidator.Validate(_companion);
        if (errors.Count > 0)
        {
            SetValidationHint(string.Join("  •  ", errors));
            MessageBox.Show(
                string.Join(Environment.NewLine, errors),
                LocalizationService.Get("Editor.Save.NotReadyTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            _manager.SaveCompanion(_companion);
            if (_isNew)
                _manager.SelectCompanion(_companion.Id);
            DialogResult = true;
            Close();
        }
        catch (Exception exception)
        {
            AppLogger.Error("The companion configuration could not be saved.", exception);
            SetValidationHint(exception.Message);
            MessageBox.Show(exception.Message, LocalizationService.Get("Editor.Save.FailedTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetValidationHint(string message)
    {
        ValidationHint.Text = message;
        ValidationHint.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    private IEnumerable<ComboBox> GetAssignmentCombos()
    {
        yield return StartCombo;
        yield return ReminderCombo;
        yield return IdleDefaultCombo;
        yield return ExitCombo;
        yield return DragLeftCombo;
        yield return DragRightCombo;
        yield return DragHoldCombo;
        yield return SelfBirthday1Combo;
        yield return SelfBirthday2Combo;
        yield return UserBirthday1Combo;
        yield return UserBirthday2Combo;
    }
}
