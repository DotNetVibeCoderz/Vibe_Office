using Microsoft.EntityFrameworkCore;
using PersonalFinanceApp.Data;

namespace PersonalFinanceApp.Services
{
    public class BudgetService
    {
        private readonly AppDbContext _context;

        public BudgetService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<Budget>> GetBudgetsAsync(int month, int year)
        {
            return await _context.Budgets
                .Include(b => b.Category)
                .Where(b => b.Month == month && b.Year == year)
                .ToListAsync();
        }

        public async Task SetBudgetAsync(Budget budget)
        {
            var existing = await _context.Budgets
                .FirstOrDefaultAsync(b => b.CategoryId == budget.CategoryId && b.Month == budget.Month && b.Year == budget.Year);
            
            if (existing != null)
            {
                existing.Amount = budget.Amount;
            }
            else
            {
                _context.Budgets.Add(budget);
            }
            await _context.SaveChangesAsync();
        }

        public async Task<decimal> GetSpentAmountAsync(int categoryId, int month, int year)
        {
            // SQLite workaround for decimal Sum
            var amounts = await _context.Transactions
                .Where(t => t.CategoryId == categoryId && t.Date.Month == month && t.Date.Year == year && t.Type == TransactionType.Expense)
                .Select(t => t.Amount)
                .ToListAsync();

            return amounts.Sum();
        }
    }
}