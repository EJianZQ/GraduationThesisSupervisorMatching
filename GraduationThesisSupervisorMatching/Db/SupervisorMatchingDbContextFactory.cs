using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GraduationThesisSupervisorMatching.Db
{
    public class SupervisorMatchingDbContextFactory : IDesignTimeDbContextFactory<SupervisorMatchingDbContext>
    {
        public SupervisorMatchingDbContext CreateDbContext(string[] args)
        {
            var builder = new DbContextOptionsBuilder<SupervisorMatchingDbContext>();
            string connectString = "server=localhost;port=3307;database=graduation;user=root;password=Byw200323;";
            builder.UseMySql(connectString, ServerVersion.AutoDetect(connectString));
            return new SupervisorMatchingDbContext(builder.Options);
        }
    }
}