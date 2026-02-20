using Microsoft.EntityFrameworkCore;
using PersonalFinanceApp.Data;

namespace PersonalFinanceApp.Services
{
    public class TransactionService
    {
        private readonly AppDbContext _context;

        public TransactionService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<Transaction>> GetAllAsync()
        {
            return await _context.Transactions
                .Include(t => t.Category)
                .Include(t => t.Account)
                .OrderByDescending(t => t.Date)
                .ToListAsync();
        }

        public async Task AddTransactionAsync(Transaction transaction)
        {
            _context.Transactions.Add(transaction);
            
            // Update Account Balance
            var account = await _context.Accounts.FindAsync(transaction.AccountId);
            if (account != null)
            {
                if (transaction.Type == TransactionType.Income)
                    account.Balance += transaction.Amount;
                else if (transaction.Type == TransactionType.Expense)
                    account.Balance -= transaction.Amount;
            }

            await _context.SaveChangesAsync();
        }

        public async Task<List<Category>> GetCategoriesAsync()
        {
            return await _context.Categories.ToListAsync();
        }

        public async Task<List<Account>> GetAccountsAsync()
        {
            return await _context.Accounts.ToListAsync();
        }
    }
}