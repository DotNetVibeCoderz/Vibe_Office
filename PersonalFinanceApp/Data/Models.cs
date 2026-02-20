using System.ComponentModel.DataAnnotations;

namespace PersonalFinanceApp.Data
{
    public enum TransactionType
    {
        Income,
        Expense,
        Transfer
    }

    public class Account
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "Bank"; // Bank, E-Wallet, Cash
        public decimal Balance { get; set; }
        public string Currency { get; set; } = "IDR";
    }

    public class Category
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public TransactionType Type { get; set; }
        public string Icon { get; set; } = "Icons.Material.Filled.Category";
    }

    public class Transaction
    {
        [Key]
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public virtual Category Category { get; set; }
        public int AccountId { get; set; }
        public virtual Account Account { get; set; }
        public TransactionType Type { get; set; }
    }

    public class Budget
    {
        [Key]
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public virtual Category Category { get; set; }
        public decimal Amount { get; set; }
        public int Month { get; set; }
        public int Year { get; set; }
    }

    public class Goal
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal TargetAmount { get; set; }
        public decimal CurrentAmount { get; set; }
        public DateTime? Deadline { get; set; }
    }

    public class AppSettings
    {
        [Key]
        public int Id { get; set; }
        public string PinHash { get; set; } = "1234"; // Default simple hash placeholder
        public bool IsPinEnabled { get; set; } = true;
    }
}