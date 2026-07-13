using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace vKOROBKU.App.Services;

internal static class WorkerTokenFile
{
    internal static string Create(string token)
    {
        var directoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "vKOROBKU", "WorkerAuth");
        EnsurePrivateDirectory(directoryPath);

        var path = Path.Combine(directoryPath, $"{Guid.NewGuid():N}.token");
        File.WriteAllText(path, token, new UTF8Encoding(false));
        File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.Temporary);
        return path;
    }

    internal static void Delete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void EnsurePrivateDirectory(string path)
    {
        var directory = Directory.CreateDirectory(path);
        var currentUser = WindowsIdentity.GetCurrent().User
                          ?? throw new InvalidOperationException("Не удалось определить текущего пользователя Windows.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(currentUser);
        security.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        directory.SetAccessControl(security);
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
    }
}
