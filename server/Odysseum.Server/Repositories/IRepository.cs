namespace Odysseum.Server.Repositories;

public interface IRepository<T, in TId> where T : class where TId : notnull
{
    Task<T?> GetAsync(TId id);
    Task<IReadOnlyList<T>> GetAllAsync();
    Task SaveAsync(T item);
    Task DeleteAsync(TId id);
}
