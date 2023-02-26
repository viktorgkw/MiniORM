namespace MiniORM;

using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Microsoft.Data.SqlClient;

/// <summary>
/// User should derive from DbContext and add wanted DbSet from Entities.
/// We need to discover run-time added DbSet from user.
/// </summary>
public abstract class DbContext
{
    private readonly DatabaseConnection _connection;
    private readonly IDictionary<Type, PropertyInfo> _dbSetProperties;

    internal DbContext(string connectionString)
    {
        this._connection = new DatabaseConnection(connectionString);
        this._dbSetProperties = DicoverDbSets();

        using (new ConnectionManager(this._connection))
        {
            this.InitializeDbSets();
        }

        this.MapAllRelations();
    }

    internal static Type[] AllowedSqlTypes =
    {
        typeof(string),
        typeof(int),
        typeof(uint),
        typeof(long),
        typeof(ulong),
        typeof(decimal),
        typeof(bool),
        typeof(DateTime)
    };

    public void SaveChanges()
    {
        var dbSets = this._dbSetProperties
            .Select(dbSetInfo => dbSetInfo.Value.GetValue(this))
            .ToArray();

        foreach (IEnumerable<object> dbSet in dbSets)
        {
            IEnumerable<object> invalidEntities = dbSet
                .Where(e => !this.IsObjectValid(e))
                .ToArray();

            if (invalidEntities.Any())
            {
                throw new InvalidOperationException(
                    string.Format(
                        ExceptionMessages.InvalidEntitiesException,
                        invalidEntities.Count(),
                        dbSet.GetType().Name)
                    );
            }
        }

        using (new ConnectionManager(this._connection))
        {
            using SqlTransaction transaction = this._connection.StartTransaction();

            foreach (IEnumerable dbSet in dbSets)
            {
                Type dbSetType = dbSet.GetType().GetGenericArguments().First();
                MethodInfo persistMethod = typeof(DbContext)
                    .GetMethod("Persist", BindingFlags.Instance | BindingFlags.NonPublic)
                    .MakeGenericMethod(dbSetType);

                try
                {
                    persistMethod.Invoke(this, new object[] { dbSet });
                }
                catch (TargetInvocationException tie)
                {
                    throw tie.InnerException;
                }
                catch (InvalidOperationException)
                {
                    transaction.Rollback();
                    throw;
                }
                catch (SqlException)
                {
                    transaction.Rollback();
                    throw;
                }
            }

            transaction.Commit();
        }
    }

    private bool IsObjectValid(object o)
    {
        ValidationContext validationContext = new ValidationContext(o);
        ICollection<ValidationResult> errors = new List<ValidationResult>();

        bool validationResult = Validator.TryValidateObject(o, validationContext, errors, true);

        return validationResult;
    }

    private void Persist<TEntity>(DbSet<TEntity> dbSet)
        where TEntity : class, new()
    {
        string tableName = this.GetTableName(typeof(TEntity));
        string[] columns = this._connection.FetchColumnNames(tableName).ToArray();

        if (dbSet.ChangeTracker.Added.Any())
        {
            this._connection.InsertEntities(dbSet.ChangeTracker.Added, tableName, columns);
        }

        IEnumerable<TEntity> modifiedEntities = dbSet.ChangeTracker
            .GetModifiedEntities(dbSet)
            .ToArray();

        if (modifiedEntities.Any())
        {
            this._connection.UpdateEntities(modifiedEntities, tableName, columns);
        }

        if (dbSet.ChangeTracker.Removed.Any())
        {
            this._connection.DeleteEntities(dbSet.ChangeTracker.Removed, tableName, columns);
        }
    }

    private string GetTableName(Type tableType)
    {
        string tableName = tableType.GetCustomAttribute<TableAttribute>().Name;

        if (tableType is null)
        {
            tableName = this._dbSetProperties[tableType].Name;
        }

        return tableName;
    }

    private IDictionary<Type, PropertyInfo> DicoverDbSets()
    {
        IDictionary<Type, PropertyInfo> dbSets = this.GetType()
            .GetProperties()
            .Where(pi => pi.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .ToDictionary(key => key.PropertyType.GetGenericArguments().First(),
                        val => val);

        return dbSets;
    }

    private void InitializeDbSets()
    {
        foreach (KeyValuePair<Type, PropertyInfo> dbSetData in _dbSetProperties)
        {
            Type dbSetType = dbSetData.Key;
            PropertyInfo dbSetProperty = dbSetData.Value;

            MethodInfo populateDbSetMethod = typeof(DbContext)
                .GetMethod("PopulateDbSet", BindingFlags.Instance | BindingFlags.NonPublic)
                .MakeGenericMethod(dbSetType);

            populateDbSetMethod.Invoke(this, new object[] { dbSetProperty });
        }
    }

    private void PopulateDbSet<TEntity>(PropertyInfo dbSet)
        where TEntity : class, new()
    {
        IEnumerable<TEntity> entities = this.LoadTableEntities<TEntity>();

        DbSet<TEntity> dbSetInstance = new DbSet<TEntity>(entities);

        ReflectionHelper.ReplaceBackingField(this, dbSet.Name, dbSetInstance);
    }

    private IEnumerable<TEntity> LoadTableEntities<TEntity>()
        where TEntity : class, new()
    {
        var entityType = typeof(TEntity);
        string[] columns = this.GetEntityColumnNames(entityType);
        string tableName = this.GetTableName(entityType);

        IEnumerable<TEntity> fetchedRows = this._connection
            .FetchResultSet<TEntity>(tableName, columns);

        return fetchedRows;
    }

    private string[] GetEntityColumnNames(Type entityType)
    {
        string tableName = this.GetTableName(entityType);

        string[] dbColumns = this._connection
            .FetchColumnNames(tableName)
            .ToArray();

        string[] columns = entityType
            .GetProperties()
            .Where(pi => dbColumns.Contains(pi.Name) &&
                !pi.HasAttribute<NotMappedAttribute>() &&
                DbContext.AllowedSqlTypes.Contains(pi.PropertyType))
            .Select(pi => pi.Name)
            .ToArray();

        return columns;
    }

    private void MapAllRelations()
    {
        foreach (KeyValuePair<Type, PropertyInfo> dbSetInfo in _dbSetProperties)
        {
            Type dbSetEntityType = dbSetInfo.Key;
            object dbSetInstance = dbSetInfo.Value.GetValue(this);

            MethodInfo mapRelationsMethod = typeof(DbContext)
                .GetMethod("MapRelations", BindingFlags.Instance | BindingFlags.NonPublic)
                .MakeGenericMethod(dbSetEntityType);

            mapRelationsMethod.Invoke(this, new object[] { dbSetInstance });
        }
    }

    private void MapRelations<TEntity>(DbSet<TEntity> dbSet)
        where TEntity : class, new()
    {
        Type entityType = typeof(TEntity);
        this.MapNavigationProperties(dbSet);
        PropertyInfo[] collections = entityType
            .GetProperties()
            .Where(pi => pi.PropertyType.IsGenericType &&
                pi.PropertyType.GetGenericTypeDefinition() == typeof(ICollection))
            .ToArray();

        foreach (PropertyInfo collection in collections)
        {
            Type collectionEntityType = collection.PropertyType
                .GetGenericArguments()
                .First();

            MethodInfo mapCollectionMethod = typeof(DbContext)
                .GetMethod("MapCollection", BindingFlags.Instance | BindingFlags.NonPublic)
                .MakeGenericMethod(entityType, collectionEntityType);

            mapCollectionMethod.Invoke(this, new object[] { dbSet, collection });
        }
    }

    private void MapNavigationProperties<TEntity>(DbSet<TEntity> dbSet)
        where TEntity : class, new()
    {
        Type entityType = typeof(TEntity);

        PropertyInfo[] foreignKeys = entityType
            .GetProperties()
            .Where(pi => pi.HasAttribute<ForeignKeyAttribute>())
            .ToArray();

        foreach (PropertyInfo foreignKey in foreignKeys)
        {
            string navigationPropertyName = foreignKey
                .GetCustomAttribute<ForeignKeyAttribute>().Name;

            PropertyInfo navigationProperty = entityType
                .GetProperty(navigationPropertyName);

            IEnumerable<object> navigationDbSet = (IEnumerable<object>)this._dbSetProperties[navigationProperty.PropertyType]
                .GetValue(this);

            PropertyInfo navigationEntityPrimaryKey = navigationProperty.PropertyType
                .GetProperties()
                .First(pi => pi.HasAttribute<KeyAttribute>());

            foreach (TEntity entity in dbSet)
            {
                object foreignKeyValue = foreignKey.GetValue(entity);

                object navPropertyValue = navigationDbSet
                    .First(cnp => navigationEntityPrimaryKey
                            .GetValue(cnp).Equals(foreignKeyValue));

                navigationProperty.SetValue(entity, navPropertyValue);
            }
        }
    }

    private void MapCollection<TEntity, TCollection>(DbSet<TEntity> dbSet, PropertyInfo collection)
        where TEntity : class, new()
        where TCollection : class, new()
    {
        Type entityType = typeof(TEntity);
        Type collectionType = typeof(TCollection);

        PropertyInfo[] collectionTypePrimaryKeys = collectionType
            .GetProperties()
            .Where(pi => pi.HasAttribute<KeyAttribute>())
            .ToArray();

        PropertyInfo foreignKey = collectionTypePrimaryKeys.First();
        PropertyInfo primaryKey = entityType
            .GetProperties()
            .First(pi => pi.HasAttribute<KeyAttribute>());

        bool isManyToMany = collectionTypePrimaryKeys.Length >= 2;

        if (isManyToMany)
        {
            foreignKey = collectionType
                .GetProperties()
                .First(pi => collectionType
                    .GetProperty(pi.GetCustomAttribute<ForeignKeyAttribute>().Name)
                    .PropertyType == entityType);
        }

        DbSet<TCollection> navDbSet = (DbSet<TCollection>)this._dbSetProperties[collectionType]
            .GetValue(this);

        foreach (var entity in dbSet)
        {
            object pkValue = primaryKey.GetValue(entity);

            IEnumerable<TCollection> navEntities = navDbSet
                .Where(navEntity => foreignKey.GetValue(navEntity).Equals(pkValue))
                .ToArray();

            ReflectionHelper.ReplaceBackingField(entity, collection.Name, navEntities);
        }
    }
}