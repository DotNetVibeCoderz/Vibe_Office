using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using PersonalFinanceApp.Data;
using PersonalFinanceApp.Services;

namespace PersonalFinanceApp
{
    public partial class MainWindow : Window
    {
        public IServiceProvider Services { get; }

        public MainWindow()
        {
            InitializeComponent();

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddWpfBlazorWebView();
            serviceCollection.AddMudServices();
            serviceCollection.AddDbContext<AppDbContext>(options =>
                options.UseSqlite("Data Source=finance.db"));
            
            // Register Services
            serviceCollection.AddScoped<TransactionService>();
            serviceCollection.AddScoped<BudgetService>();
            serviceCollection.AddScoped<GoalService>();
            serviceCollection.AddScoped<ReportService>();
            serviceCollection.AddScoped<AuthService>();

            Services = serviceCollection.BuildServiceProvider();
            
            // Initialize DB
            var db = Services.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();

            blazorWebView.Services = Services;
        }
    }
}