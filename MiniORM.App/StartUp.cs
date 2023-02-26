namespace MiniORM.App;

using MiniORM.App.Data;

public class StartUp
{
    static void Main()
    {
        SoftUniDbContext dbContext = new SoftUniDbContext(Config.ConnectionString);

        Console.WriteLine("Connection success!");

        Thread.Sleep(1000);

        Console.Clear();

        // Add

        //Employee newEmployee = new Employee()
        //{
        //    FirstName = "Test",
        //    LastName = "Testov",
        //    DepartmentId = dbContext.Departments.First().Id,
        //    IsEmployed = true
        //};
        //dbContext.Employees.Add(newEmployee);

        // Delete

        //Employee employee = dbContext.Employees
        //    .First(e => e.FirstName == "Test");
        //dbContext.Employees.Remove(employee);

        dbContext.SaveChanges();
    }
}