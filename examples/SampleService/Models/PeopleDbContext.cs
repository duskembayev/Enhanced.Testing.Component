using Microsoft.EntityFrameworkCore;

namespace SampleService.Models;

public class PeopleDbContext(DbContextOptions options) : DbContext(options)
{
    public const string ConnectionStringName = "PeopleDb";

    public DbSet<Person> Persons { get; init; }
}
