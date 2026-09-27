namespace Odysseum.Server.Repositories;

/// <summary>Reads from memory and saves through the storage. Each change gives the ETag the caller last saw; the
/// storage refuses the change when the stored item has a different ETag.</summary>
public interface IRepository<T> : IReadOnlyRepository<T> where T : class
{
    Task<T> AddAsync(T item);
    Task<T> UpdateAsync(T item, string expectedETag);
    Task DeleteAsync(string id, string expectedETag);
}
