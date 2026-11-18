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
            var cleaned = FinishCleanup(id);
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

    public void RetryPending(Guid? except = null)
    {
        foreach (var id in manager.Data.PendingCompanionCleanup.ToArray())
            if (id != except) FinishCleanup(id);
    }

    private bool FinishCleanup(Guid id)
    {
        if (!manager.Data.PendingCompanionCleanup.Contains(id) || manager.Data.Companions.Any(pet => pet.Id == id))
            return true;
        try
        {
            history.Clear([id], ChatHistoryClearScope.MessagesAndNotes);
            var liveAssets = manager.Data.Companions.SelectMany(pet => pet.GetAssetPaths());
            var archivedImages = history.ListPets().Values.Select(pet => pet.Portrait).OfType<ArchivedPetPortrait>()
                .SelectMany(portrait => new[] { portrait.PicturePath, portrait.IdleFramePath });
            if (storage.DeleteCompanionResources(id, liveAssets.Concat(archivedImages)))
                manager.CompleteCompanionCleanup(id);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Could not finish deleting local data for companion {id}. Cleanup will retry at startup.", ex);
            return false;
        }
    }
}
