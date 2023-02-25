namespace MiniORM;

using System.Reflection;

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
        throw new NotImplementedException();
    }

    private IDictionary<Type, PropertyInfo> DicoverDbSets()
    {
        throw new NotImplementedException();
    }

    private void InitializeDbSets()
    {
        throw new NotImplementedException();
    }

    private void MapAllRelations()
    {
        throw new NotImplementedException();
    }
}