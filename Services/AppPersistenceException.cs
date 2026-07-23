namespace DesktopFamiliar.Services;

/// <summary>
/// A recoverable configuration persistence failure. UI exception boundaries
/// should report this without terminating the application.
/// </summary>
public sealed class AppPersistenceException : Exception
{
    public AppPersistenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
