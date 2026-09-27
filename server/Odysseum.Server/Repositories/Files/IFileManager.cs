namespace Odysseum.Server.Repositories.Files;

public interface IFileManager
{
    string Root { get; }
    FileStream AcquireInstanceLock();
    bool Exists(string relative, bool metadata = false);
    bool FolderExists(string relative);
    DateTime LastModified(string relative, bool metadata = false);
    Task<byte[]> ReadAsync(string relative, bool metadata = false);
    Task WriteAsync(string relative, byte[] bytes, bool overwrite = true, bool metadata = false);
    void Move(string source, string destination);
    void MoveFolder(string source, string destination);
    void Delete(string relative, bool metadata = false);
    FileStream Lock(string relative, bool metadata = false);
    IEnumerable<string> EnumerateDocuments();
    IEnumerable<string> EnumerateFolders(bool recursive = true);
    IEnumerable<string> EnumerateSubfolders(string relative);
    IEnumerable<string> EnumerateDocumentsIn(string relative);
    IEnumerable<string> EnumerateFiles(string relative, string pattern, bool metadata = false);
    void CreateFolder(string relative);
    void RemoveEmptyFolder(string relative);
}
