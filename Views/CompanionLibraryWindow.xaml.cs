using System.Windows;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Storage;
using DesktopFamiliar.Views.Controls;
using Microsoft.Win32;

namespace DesktopFamiliar.Views;

public partial class CompanionLibraryWindow : Window
{
    private readonly CompanionManager _manager;
    private readonly CompanionStorage _storage;

    public CompanionLibraryWindow(CompanionManager manager, CompanionStorage storage)
    {
        InitializeComponent();
        _manager = manager;
        _storage = storage;
        _manager.CompanionCollectionChanged += ManagerChanged;
        _manager.CurrentCompanionChanged += ManagerChanged;
        _manager.ThemeChanged += ManagerChanged;
        Closed += (_, _) =>
        {
            _manager.CompanionCollectionChanged -= ManagerChanged;
            _manager.CurrentCompanionChanged -= ManagerChanged;
            _manager.ThemeChanged -= ManagerChanged;
        };
        RefreshCards();
    }

    private void ManagerChanged(object? sender, EventArgs e) => Dispatcher.Invoke(RefreshCards);

    private void RefreshCards()
    {
        CompanionCards.Children.Clear();
        EmptyCard.Visibility = _manager.Data.Companions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var companion in _manager.Data.Companions.OrderBy(companion => companion.Name))
        {
            var card = new CompanionCard(
                companion,
                companion.Id == _manager.Data.Settings.CurrentCompanionId);
            card.SelectRequested += (_, _) => _manager.SelectCompanion(card.Companion.Id);
            card.ConfigureRequested += (_, _) => OpenEditor(card.Companion.Id);
            card.ExportRequested += (_, _) => ExportCompanion(card.Companion.Id);
            card.DeleteRequested += (_, _) => DeleteCompanion(card.Companion.Id);
            CompanionCards.Children.Add(card);
        }
    }

    private void CreateCompanion_Click(object sender, RoutedEventArgs e)
    {
        var editor = new CompanionEditorWindow(_manager, _storage, null) { Owner = this };
        editor.ShowDialog();
    }

    private void ImportCompanion_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Get("CompanionBundle.Import.Title"),
            Filter = LocalizationService.Get("CompanionBundle.FileDialog.Filter"),
            DefaultExt = CompanionBundleService.FileExtension,
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var preview = _storage.InspectCompanionBundle(dialog.FileName);
            var sameName = _manager.Data.Companions.FirstOrDefault(companion =>
                string.Equals(companion.Name, preview.Name, StringComparison.CurrentCultureIgnoreCase));
            var destinationId = Guid.NewGuid();
            var importedName = preview.Name;

            if (sameName is not null)
            {
                var conflict = new CompanionImportConflictWindow(preview.Name) { Owner = this };
                conflict.ShowDialog();
                if (conflict.Choice == CompanionImportConflictChoice.Cancel)
                    return;
                if (conflict.Choice == CompanionImportConflictChoice.Overwrite)
                    destinationId = sameName.Id;
                else
                    importedName = CreateUniqueName(preview.Name);
            }
            else if (_manager.Data.Companions.Any(companion => companion.Id == preview.CompanionId))
            {
                destinationId = Guid.NewGuid();
            }
            else
            {
                destinationId = preview.CompanionId;
            }

            var importResult = _storage.ImportCompanionBundle(dialog.FileName, destinationId, importedName);
            try
            {
                _manager.SaveCompanion(importResult.Companion);
            }
            catch
            {
                try
                {
                    _storage.DiscardCompanionBundleImport(importResult);
                }
                catch (Exception cleanupException)
                {
                    AppLogger.Warning("The failed companion import's staged assets could not be removed.", cleanupException);
                }
                throw;
            }
            MessageBox.Show(
                LocalizationService.Format("CompanionBundle.Import.Success", importResult.Companion.Name),
                LocalizationService.Get("CompanionBundle.Import.SuccessTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            AppLogger.Error($"The companion bundle '{dialog.FileName}' could not be imported.", exception);
            MessageBox.Show(
                LocalizationService.Format("CompanionBundle.Import.Failed", exception.Message),
                LocalizationService.Get("CompanionBundle.Import.FailedTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ExportCompanion(Guid id)
    {
        var companion = _manager.Data.Companions.FirstOrDefault(item => item.Id == id);
        if (companion is null)
            return;

        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.Get("CompanionBundle.Export.Title"),
            Filter = LocalizationService.Get("CompanionBundle.FileDialog.Filter"),
            DefaultExt = CompanionBundleService.FileExtension,
            AddExtension = true,
            FileName = MakeSafeFileName(companion.Name) + CompanionBundleService.FileExtension
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            _storage.ExportCompanion(companion, dialog.FileName);
            MessageBox.Show(
                LocalizationService.Format("CompanionBundle.Export.Success", companion.Name, dialog.FileName),
                LocalizationService.Get("CompanionBundle.Export.SuccessTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            AppLogger.Error($"Companion '{companion.Name}' could not be exported to '{dialog.FileName}'.", exception);
            MessageBox.Show(
                LocalizationService.Format("CompanionBundle.Export.Failed", exception.Message),
                LocalizationService.Get("CompanionBundle.Export.FailedTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private string CreateUniqueName(string requestedName)
    {
        var number = 2;
        string candidate;
        do
        {
            candidate = LocalizationService.Format("CompanionBundle.Import.CopyName", requestedName, number++);
        }
        while (_manager.Data.Companions.Any(companion =>
            string.Equals(companion.Name, candidate, StringComparison.CurrentCultureIgnoreCase)));
        return candidate;
    }

    private static string MakeSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat(name.Select(character => invalid.Contains(character) ? '_' : character)).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "companion" : safe;
    }

    private void OpenEditor(Guid id)
    {
        var companion = _manager.Data.Companions.FirstOrDefault(item => item.Id == id);
        if (companion is null)
            return;
        var editor = new CompanionEditorWindow(_manager, _storage, companion) { Owner = this };
        editor.ShowDialog();
    }

    private void DeleteCompanion(Guid id)
    {
        var companion = _manager.Data.Companions.FirstOrDefault(item => item.Id == id);
        if (companion is null)
            return;
        var result = MessageBox.Show(
            LocalizationService.Format("Library.Delete.Message", companion.Name),
            LocalizationService.Get("Library.Delete.Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
            _manager.DeleteCompanion(id);
    }
}
