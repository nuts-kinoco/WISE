using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WISE.Infrastructure.Data;

public class WiseDbContextFactory : IDesignTimeDbContextFactory<WiseDbContext>
{
    public WiseDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<WiseDbContext>();
        optionsBuilder.UseSqlite("Data Source=wise.db");

        return new WiseDbContext(optionsBuilder.Options);
    }
}
