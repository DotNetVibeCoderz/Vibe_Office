using Microsoft.EntityFrameworkCore;
using PersonalFinanceApp.Data;

namespace PersonalFinanceApp.Services
{
    public class GoalService
    {
        private readonly AppDbContext _context;

        public GoalService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<Goal>> GetGoalsAsync()
        {
            return await _context.Goals.ToListAsync();
        }

        public async Task AddGoalAsync(Goal goal)
        {
            _context.Goals.Add(goal);
            await _context.SaveChangesAsync();
        }

        public async Task AddSavingsAsync(int goalId, decimal amount)
        {
            var goal = await _context.Goals.FindAsync(goalId);
            if (goal != null)
            {
                goal.CurrentAmount += amount;
                await _context.SaveChangesAsync();
            }
        }
    }
}