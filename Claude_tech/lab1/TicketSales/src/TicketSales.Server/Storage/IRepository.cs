using System.Collections.Generic;

namespace TicketSales.Server.Storage;

public interface IRepository<TEntity>
{
    TEntity? GetById(int id);
    IReadOnlyList<TEntity> GetAll();
    TEntity Add(TEntity entity);
    void Update(TEntity entity);
}
