namespace MiniORM;

internal class ConnectionManager : IDisposable
{
    /// <summary>
    /// Used for wrapping a database connection with a using statement and
    /// automatically closing it when the using statement ends
    /// </summary>

    private readonly DatabaseConnection connection;
    public ConnectionManager(DatabaseConnection connection)
    {
        this.connection = connection;

        this.connection.Open();
    }

    public void Dispose()
    {
        this.connection.Close();
    }
}
