using Microsoft.EntityFrameworkCore;
using PersonalFinanceApp.Data;

namespace PersonalFinanceApp.Services
{
    public class AuthService
    {
        private readonly AppDbContext _context;
        public bool IsAuthenticated { get; private set; } = false;

        public AuthService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<bool> VerifyPin(string pin)
        {
            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings != null && settings.PinHash == pin)
            {
                IsAuthenticated = true;
                return true;
            }
            return false;
        }

        public bool CheckAuth() => IsAuthenticated;
    }
}