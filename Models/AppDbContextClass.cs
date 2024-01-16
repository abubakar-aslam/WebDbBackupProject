using Microsoft.EntityFrameworkCore;

namespace BackupDBWeb.Models
{
    public class AppDbContextClass : DbContext
    {
        public AppDbContextClass(DbContextOptions<AppDbContextClass> options) : base(options) { }
    }
}
