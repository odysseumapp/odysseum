namespace Odysseum.Server.Repositories;

public interface IReadOnlyRepository<T> where T : class
{
    Task<T?> GetByIdAsync(string id);
    Task<IReadOnlyList<T>> GetAllAsync();
    IQueryable<T> Query();
}
