namespace MiniORM;

using System.ComponentModel.DataAnnotations;
using System.Reflection;

internal class ChangeTracker<T>
    where T : class, new()
{
    private readonly IList<T> _allEntities;
    private readonly IList<T> _added;
    private readonly IList<T> _removed;

    private ChangeTracker()
    {
        this._added = new List<T>();
        this._removed = new List<T>();
    }

    public ChangeTracker(IEnumerable<T> entities)
        : this()
    {
        this._allEntities = this.CloneEntities(entities);
    }

    public IReadOnlyCollection<T> AllEntities => (IReadOnlyCollection<T>)this._allEntities;
    public IReadOnlyCollection<T> Added => (IReadOnlyCollection<T>)this._added;
    public IReadOnlyCollection<T> Removed => (IReadOnlyCollection<T>)this._removed;

    public void Add(T entity) => this._added.Add(entity);
    public void Remove(T entity) => this._removed.Add(entity);

    public IEnumerable<T> GetModifiedEntities(DbSet<T> dbSet)
    {
        IList<T> modifiedEntities = new List<T>();

        PropertyInfo[] primaryKeys = typeof(T)
            .GetProperties()
            .Where(pi => pi.HasAttribute<KeyAttribute>())
            .ToArray();

        foreach (T proxyEntity in AllEntities)
        {
            object[] primaryKeyValues = this.GetPrimaryKeyValues(primaryKeys, proxyEntity)
                .ToArray();

            T originalEntity = dbSet.Entities
                .Single(e => this.GetPrimaryKeyValues(primaryKeys, e).SequenceEqual(primaryKeyValues));

            if (this.IsModified(proxyEntity, originalEntity))
            {
                modifiedEntities.Add(proxyEntity);
            }
        }

        return modifiedEntities;
    }

    private IEnumerable<object> GetPrimaryKeyValues(PropertyInfo[] primaryKeys, T proxyEntity)
        => primaryKeys.Select(pk => pk.GetValue(proxyEntity));

    private bool IsModified(T proxyEntity, T originalEntity)
    {
        PropertyInfo[] monitoredProperties = typeof(T)
            .GetProperties()
            .Where(pi => DbContext.AllowedSqlTypes.Contains(pi.PropertyType))
            .ToArray();

        PropertyInfo[] modifiedProperties = monitoredProperties
            .Where(pi => !Equals(pi.GetValue(proxyEntity), pi.GetValue(originalEntity)))
            .ToArray();

        return modifiedProperties.Any();
    }

    private IList<T> CloneEntities(IEnumerable<T> originalEntities)
    {
        IList<T> clonedEntities = new List<T>();

        PropertyInfo[] propertiesToClone = typeof(T)
            .GetProperties()
            .Where(pi => DbContext.AllowedSqlTypes.Contains(pi.PropertyType))
            .ToArray();

        foreach (T originalEntity in originalEntities)
        {
            T entityClone = Activator.CreateInstance<T>();

            foreach (PropertyInfo property in propertiesToClone)
            {
                object originalValue = property.GetValue(originalEntity);

                property.SetValue(entityClone, originalValue);
            }

            clonedEntities.Add(entityClone);
        }

        return clonedEntities;
    }
}