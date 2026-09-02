using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TicketSales.Server.Storage;

namespace TicketSales.Server.Storage.InMemory;

public abstract class InMemoryRepository<TEntity> : IRepository<TEntity>
{
    protected readonly ConcurrentDictionary<int, TEntity> Storage = new();
    private int _lastId;
    private readonly Func<TEntity, int> _idGetter;
    private readonly Action<TEntity, int> _idSetter;

    protected InMemoryRepository(Func<TEntity, int> idGetter, Action<TEntity, int> idSetter)
    {
        _idGetter = idGetter;
        _idSetter = idSetter;
    }

    public TEntity? GetById(int id)
    {
        Storage.TryGetValue(id, out var entity);
        return entity;
    }

    public IReadOnlyList<TEntity> GetAll()
    {
        return Storage.Values.ToList();
    }

    public TEntity Add(TEntity entity)
    {
        var id = _idGetter(entity);
        if (id <= 0)
        {
            id = Interlocked.Increment(ref _lastId);
            _idSetter(entity, id);
        }

        Storage[id] = entity;
        return entity;
    }

    public void Update(TEntity entity)
    {
        Storage[_idGetter(entity)] = entity;
    }
}
