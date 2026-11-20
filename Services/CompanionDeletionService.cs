using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Storage;

namespace DesktopFamiliar.Services;

public sealed class CompanionDeletionService(CompanionManager manager, CompanionStorage storage, ChatHistoryStore history)
{
    public bool Delete(Guid id, bool deleteLocalData)
    {
        var companion = manager.Data.Companions.FirstOrDefault(pet => pet.Id == id);
        manager.DeleteCompanion(id, deleteLocalData);
        if (deleteLocalData)
        {
            var cleaned = FinishCleanup(id, newRequest: companion != null);
            RetryPending(except: id);
            return cleaned;
        }
        if (companion != null)
        {
            try { history.RememberArchivedAppearance(companion); }
            catch (Exception ex)
            {
                // The existing conversation remains readable with its last saved name and default icon.
                AppLogger.Warning("Could not save the portrait for a removed companion's chat history.", ex);
            }
        }
        RetryPending();
        return true;
    }

    public void RetryPending(Guid? except = null, bool deferSave = false)
    {
        if (manager.RequiresExplicitSave) return;
        foreach (var id in manager.Data.PendingCompanionCleanup.ToArray())
            if (id != except) FinishCleanup(id, deferSave);
    }

    private bool FinishCleanup(Guid id, bool deferSave = false, bool newRequest = false)
    {
        if (!manager.Data.PendingCompanionCleanup.Contains(id) || manager.Data.Companions.Any(pet => pet.Id == id))
            return true;
        try
        {
            var receipt = storage.HistoryWipeReceipt(id);
            if (newRequest || !File.Exists(receipt))
            {
                history.Clear([id], ChatHistoryClearScope.MessagesAndNotes);
                // Remember this phase across asset retries and launches that leave the library read-only.
                using var completed = File.Create(receipt);
            }
            var liveAssets = manager.Data.Companions.SelectMany(pet => pet.GetAssetPaths());
            var archivedImages = history.ListPets().Values.Select(pet => pet.Portrait).OfType<CompanionPortrait>()
                .SelectMany(portrait => new[] { portrait.PicturePath, portrait.IdleFramePath, portrait.ModelPath });
            if (storage.DeleteCompanionResources(id, liveAssets.Concat(archivedImages)))
            {
                storage.FinishWipeOnNextSave(id);
                manager.CompleteCompanionCleanup(id, deferSave);
            }
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Could not finish deleting local data for companion {id}. Cleanup will retry at startup.", ex);
            return false;
        }
    }
}
