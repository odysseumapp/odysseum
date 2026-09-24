namespace Odysseum.Server.Repositories.Files;

public interface IFileManager
{
    string Root { get; }
    bool Exists(string relative);
    DateTime LastModified(string relative);
    Task<byte[]> ReadAsync(string relative);
    Task WriteAsync(string relative, byte[] bytes, bool overwrite = true);
    void Move(string source, string destination);
    IEnumerable<string> EnumerateDocuments();
    IEnumerable<string> EnumerateFolders();
    void CreateFolder(string relative);
    void RemoveEmptyFolder(string relative);
}
