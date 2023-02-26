namespace MiniORM.App;

using MiniORM.App.Data;

public class StartUp
{
    static void Main()
    {
        SoftUniDbContext dbContext = new SoftUniDbContext(Config.ConnectionString);

        Console.WriteLine("Connection success!");
    }
}