using Microsoft.EntityFrameworkCore;
using PersonalFinanceApp.Data;

namespace PersonalFinanceApp.Services
{
    public class ReportService
    {
        private readonly AppDbContext _context;

        public ReportService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<decimal> GetTotalIncomeAsync(int month, int year)
        {
            // SQLite restriction: Cannot Sum decimal directly on server side easily without precision loss or config.
            // fetching amounts to client side for accurate summation.
            var amounts = await _context.Transactions
                .Where(t => t.Type == TransactionType.Income && t.Date.Month == month && t.Date.Year == year)
                .Select(t => t.Amount)
                .ToListAsync();
                
            return amounts.Sum();
        }

        public async Task<decimal> GetTotalExpenseAsync(int month, int year)
        {
            var amounts = await _context.Transactions
                .Where(t => t.Type == TransactionType.Expense && t.Date.Month == month && t.Date.Year == year)
                .Select(t => t.Amount)
                .ToListAsync();
                
            return amounts.Sum();
        }

        public async Task<List<Transaction>> GetRecentTransactionsAsync(int count)
        {
            return await _context.Transactions
                .Include(t => t.Category)
                .OrderByDescending(t => t.Date)
                .Take(count)
                .ToListAsync();
        }
    }
}