namespace Tessera.UI;

/// <summary>
/// What the user is being asked to confirm deleting. A standalone type rather than a
/// nested one: <see cref="ConfirmDialog"/> is a general-purpose dialog and should not have
/// to reach into the window that happens to be asking.
/// </summary>
internal readonly record struct DeleteRequest(string Name, string FullPath, long Size);
