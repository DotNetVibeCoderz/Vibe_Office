using Microsoft.EntityFrameworkCore;

namespace PersonalFinanceApp.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<Account> Accounts { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Transaction> Transactions { get; set; }
        public DbSet<Budget> Budgets { get; set; }
        public DbSet<Goal> Goals { get; set; }
        public DbSet<AppSettings> Settings { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Seed Categories
            modelBuilder.Entity<Category>().HasData(
                new Category { Id = 1, Name = "Food", Type = TransactionType.Expense, Icon = "Icons.Material.Filled.Fastfood" },
                new Category { Id = 2, Name = "Transport", Type = TransactionType.Expense, Icon = "Icons.Material.Filled.DirectionsBus" },
                new Category { Id = 3, Name = "Salary", Type = TransactionType.Income, Icon = "Icons.Material.Filled.AttachMoney" },
                new Category { Id = 4, Name = "Entertainment", Type = TransactionType.Expense, Icon = "Icons.Material.Filled.Movie" },
                new Category { Id = 5, Name = "Health", Type = TransactionType.Expense, Icon = "Icons.Material.Filled.LocalHospital" }
            );

            // Seed Account
            modelBuilder.Entity<Account>().HasData(
                new Account { Id = 1, Name = "Main Wallet", Balance = 0, Type = "Cash" }
            );

            // Seed Settings
            modelBuilder.Entity<AppSettings>().HasData(
                new AppSettings { Id = 1, PinHash = "1234", IsPinEnabled = true } 
            );
        }
    }
}