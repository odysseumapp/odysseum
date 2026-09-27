using System.Collections.Concurrent;

namespace Odysseum.Server.Repositories;

/// <summary>A repository that keeps all items of its type in memory. It fills and updates the memory from
/// <see cref="IStorageContext.Changed"/>, which reports each write and each read of a project, also reads caused by
/// other programs changing the files. Reads never go to the storage.</summary>
public abstract class CachedRepository<T> : IReadOnlyRepository<T> where T : class
{
    private readonly ConcurrentDictionary<string, T> _items = new(StringComparer.Ordinal);

    protected CachedRepository(IStorageContext storage)
    {
        Storage = storage;
        storage.Changed += Apply;
    }

    protected IStorageContext Storage { get; }

    public event EventHandler<RepositoryChangeEventArgs<T>>? ItemAdded;
    public event EventHandler<RepositoryChangeEventArgs<T>>? ItemUpdated;
    public event EventHandler<RepositoryChangeEventArgs<T>>? ItemRemoved;

    public Task<T?> GetByIdAsync(string id) => Task.FromResult(_items.GetValueOrDefault(id));

    public Task<IReadOnlyList<T>> GetAllAsync() => Task.FromResult<IReadOnlyList<T>>(_items.Values.ToArray());

    public IQueryable<T> Query() => _items.Values.ToArray().AsQueryable();

    protected abstract string IdOf(T item);
    protected abstract string ProjectIdOf(T item);
    /// <summary>A value that changes when the item changes, for example its ETag.</summary>
    protected abstract string VersionOf(T item);
    protected abstract IReadOnlyList<T> ChangedItemsIn(StorageChanges changes);
    protected abstract IReadOnlyList<string> RemovedIdsIn(StorageChanges changes);

    /// <summary>The item with that ID among the changes of a write this repository made.</summary>
    protected T ChangedItem(StorageChanges changes, string id) => ChangedItemsIn(changes).First(item => IdOf(item) == id);

    protected virtual void OnEndSave(T? before, T item)
    {
        if (before is null) ItemAdded?.Invoke(this, new(null, item));
        else if (VersionOf(before) != VersionOf(item)) ItemUpdated?.Invoke(this, new(before, item));
    }

    protected virtual void OnEndDelete(T item) => ItemRemoved?.Invoke(this, new(item, item));

    private void Apply(StorageChanges changes)
    {
        var changed = ChangedItemsIn(changes);
        var removedIds = new HashSet<string>(RemovedIdsIn(changes), StringComparer.Ordinal);
        if (changes.ReplacesProject)
        {
            var kept = changed.Select(IdOf).ToHashSet(StringComparer.Ordinal);
            foreach (var (id, item) in _items)
                if (ProjectIdOf(item) == changes.ProjectId && !kept.Contains(id)) removedIds.Add(id);
        }
        foreach (var item in changed)
        {
            var id = IdOf(item);
            var before = _items.GetValueOrDefault(id);
            _items[id] = item;
            OnEndSave(before, item);
        }
        foreach (var id in removedIds)
            if (_items.TryRemove(id, out var removed)) OnEndDelete(removed);
    }
}
