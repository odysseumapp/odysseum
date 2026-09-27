namespace Odysseum.Server.Repositories;

/// <summary>An item that was added, updated or removed. <c>Before</c> is null for an added item.</summary>
public sealed class RepositoryChangeEventArgs<T>(T? before, T item) : EventArgs where T : class
{
    public T? Before { get; } = before;
    public T Item { get; } = item;
}
