using Cuan.Data;
using Cuan.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cuan.Api;

/// <summary>
/// REST API Controller untuk mengakses semua data dengan ApiKey authentication
/// Akses melalui /api/v1/*
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class ApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public ApiController(AppDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    #region Auth Helper
    private async Task<bool> ValidateApiKey()
    {
        if (!Request.Headers.TryGetValue("X-Api-Key", out var apiKey))
            return false;

        var key = await _db.ApiKeys
            .FirstOrDefaultAsync(k => k.KeyValue == apiKey.ToString() && k.IsActive &&
                                      (k.ExpiresAt == null || k.ExpiresAt > DateTime.UtcNow));
        return key != null;
    }
    #endregion

    #region Chart of Accounts
    [HttpGet("coa")]
    public async Task<IActionResult> GetCOA()
    {
        if (!await ValidateApiKey()) return Unauthorized("Invalid API Key");
        var data = await _db.ChartOfAccounts.OrderBy(c => c.AccountCode).ToListAsync();
        return Ok(data);
    }

    [HttpGet("coa/{id}")]
    public async Task<IActionResult> GetCOAById(int id)
    {
        if (!await ValidateApiKey()) return Unauthorized("Invalid API Key");
        var coa = await _db.ChartOfAccounts.FindAsync(id);
        return coa == null ? NotFound() : Ok(coa);
    }

    [HttpPost("coa")]
    public async Task<IActionResult> CreateCOA([FromBody] ChartOfAccount coa)
    {
        if (!await ValidateApiKey()) return Unauthorized("Invalid API Key");
        coa.CreatedAt = DateTime.UtcNow;
        _db.ChartOfAccounts.Add(coa);
        await _db.SaveChangesAsync();
        return Created($"/api/v1/coa/{coa.Id}", coa);
    }
    #endregion

    #region Items
    [HttpGet("items")]
    public async Task<IActionResult> GetItems([FromQuery] int page = 1, [FromQuery] int size = 20, [FromQuery] string? search = null)
    {
        if (!await ValidateApiKey()) return Unauthorized("Invalid API Key");
        var query = _db.Items.Include(i => i.Category).AsQueryable();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(i => i.ItemCode.Contains(search) || i.ItemName.Contains(search));
        var total = await query.CountAsync();
        var items = await query.OrderBy(i => i.ItemCode).Skip((page - 1) * size).Take(size).ToListAsync();
        return Ok(new { total, page, size, data = items });
    }

    [HttpGet("items/{id}")]
    public async Task<IActionResult> GetItem(int id)
    {
        if (!await ValidateApiKey()) return Unauthorized("Invalid API Key");
        var item = await _db.Items.Include(i => i.Category).FirstOrDefaultAsync(i => i.Id == id);
        return item == null ? NotFound() : Ok(item);
    }

    [HttpGet("items/low-stock")]
    public async Task<IActionResult> GetLowStockItems()
    {
        if (!await ValidateApiKey()) return Unauthorized("Invalid API Key");
        var items = await _db.Items.Where(i => i.StockQuantity <= i.MinimumStock && i.IsActive).ToListAsync();
        return Ok(items);
    }
    #endregion

    #region Customers
    [HttpGet("customers")]
    public async Task<IActionResult> GetCustomers([FromQuery] int page = 1, [FromQuery] int size = 20)
    {
        if (!await ValidateApiKey()) return Unauthorized("Invalid API Key");
        var total = await _db.Customers.CountAsync();
        var customers = await _db.Customers.OrderBy(c => c.CustomerCode).Skip((page - 1) * size).Take(size).ToListAsync();
        return Ok(new { total, page, size, data = customers });
    }

    [HttpGet("customers/{id}")]
    public async Task<IActionResult> GetCustomer(int id)
    {
        if (!await ValidateApiKey()) return Unauthorized("Invalid API Key");
        var customer = await _db.Customers.FindAsync(id);
        return customer == null ? NotFound() : Ok(customer);
    }
    #endregion

    #region Suppliers
    [HttpGet("suppliers")]
    public async Task<IActionResult> GetSuppliers()
    {
        if (!await ValidateApiKey()) return Unauthorized("Invalid API Key");
        var suppliers = await _db.Suppliers.OrderBy(s => s.SupplierCode).ToListAsync();
        return Ok(suppliers);
    }
    #endregion

    #region Journal Entries
    [HttpGet("journals")]
    public async Task<IActionResult> GetJournals([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int page = 1, [FromQuery] int size = 20)
    {
        if (!await ValidateApiKey()) return Unauthorized("Invalid API Key");
        var query = _db.JournalEntries.Include(j => j.Details).AsQueryable();
        if (from.HasValue) query = query.Where(j => j.TransactionDate >= from.Value);
        if (to.HasValue) query = query.Where(j => j.TransactionDate <= to.Value);
        var total = await query.CountAsync();
        var journals = await query.OrderByDescending(j => j.TransactionDate).Skip((page - 1) * size).Take(size).ToListAsync();
        return Ok(new { total, page, size, data = journals });
    }

    [HttpGet("journals/{id}")]
    public async Task<IActionResult> GetJournal(int id)
    {
        if (!await ValidateApiKey()) return Unauthorized("Invalid API Key");
        var journal = await _db.JournalEntries.Include(j => j.Details).FirstOrDefaultAsync(j => j.Id == id);
        return journal == null ? NotFound() : Ok(journal);
    }

    [HttpPost("journals")]
    public async Task<IActionResult> CreateJournal([FromBody] JournalEntry journal)
    {
        if (!await ValidateApiKey()) return Unauthorized("Invalid API Key");

        // Validate debit = credit
        if (journal.Details.Sum(d => d.Debit) != journal.Details.Sum(d => d.Credit))
            return BadRequest("Total Debit harus sama dengan Total Credit");

        journal.CreatedAt = DateTime.UtcNow;
        journal.JournalNumber = $"JU-{DateTime.UtcNow.Year}-{await _db.JournalEntries.CountAsync() + 1:D4}";
        _db.JournalEntries.Add(journal);
        await _db.SaveChangesAsync();
        return Created($"/api/v1/journals/{journal.Id}", journal);
    }
    #endregion

    #region Sales Invoices
    [HttpGet("sales-invoices")]
    public async Task<IActionResult> GetSalesInvoices([FromQuery] int page = 1, [FromQuery] int size = 20)
    {
        if (!await ValidateApiKey()) return Unauthorized("Invalid API Key");
        var total = await _db.SalesInvoices.CountAsync();
        var invoices = await _db.SalesInvoices.Include(s => s.Customer).Include(s => s.Details)
            .OrderByDescending(s => s.InvoiceDate).Skip((page - 1) * size).Take(size).ToListAsync();
        return Ok(new { total, page, size, data = invoices });
    }
    #endregion

    #region Purchase Invoices
    [HttpGet("purchase-invoices")]
    public async Task<IActionResult> GetPurchaseInvoices([FromQuery] int page = 1, [FromQuery] int size = 20)
    {
        if (!await ValidateApiKey()) return Unauthorized("Invalid API Key");
        var total = await _db.PurchaseInvoices.CountAsync();
        var invoices = await _db.PurchaseInvoices.Include(p => p.Supplier).Include(p => p.Details)
            .OrderByDescending(p => p.InvoiceDate).Skip((page - 1) * size).Take(size).ToListAsync();
        return Ok(new { total, page, size, data = invoices });
    }
    #endregion

    #region Cash & Bank Transactions
    [HttpGet("cash-transactions")]
    public async Task<IActionResult> GetCashTransactions()
    {
        if (!await ValidateApiKey()) return Unauthorized("Invalid API Key");
        return Ok(await _db.CashTransactions.OrderByDescending(c => c.TransactionDate).Take(100).ToListAsync());
    }

    [HttpGet("bank-transactions")]
    public async Task<IActionResult> GetBankTransactions()
    {
        if (!await ValidateApiKey()) return Unauthorized("Invalid API Key");
        return Ok(await _db.BankTransactions.Include(b => b.BankAccount).OrderByDescending(b => b.TransactionDate).Take(100).ToListAsync());
    }
    #endregion

    #region Stock
    [HttpGet("stock-movements")]
    public async Task<IActionResult> GetStockMovements([FromQuery] int? itemId, [FromQuery] int? warehouseId)
    {
        if (!await ValidateApiKey()) return Unauthorized("Invalid API Key");
        var query = _db.StockMovements.Include(s => s.Item).Include(s => s.Warehouse).AsQueryable();
        if (itemId.HasValue) query = query.Where(s => s.ItemId == itemId.Value);
        if (warehouseId.HasValue) query = query.Where(s => s.WarehouseId == warehouseId.Value);
        return Ok(await query.OrderByDescending(s => s.MovementDate).Take(200).ToListAsync());
    }

    [HttpGet("items/{id}/stock")]
    public async Task<IActionResult> GetItemStock(int id)
    {
        if (!await ValidateApiKey()) return Unauthorized("Invalid API Key");
        var stocks = await _db.ItemStocks.Include(s => s.Warehouse).Where(s => s.ItemId == id).ToListAsync();
        return Ok(stocks);
    }
    #endregion

    #region Dashboard / Stats
    [HttpGet("dashboard/summary")]
    public async Task<IActionResult> GetDashboardSummary()
    {
        if (!await ValidateApiKey()) return Unauthorized("Invalid API Key");
        return Ok(new
        {
            totalRevenue = await _db.ChartOfAccounts.Where(c => c.AccountType == 4 && !c.IsHeader).SumAsync(c => c.CurrentBalance),
            totalExpense = await _db.ChartOfAccounts.Where(c => c.AccountType == 5 && !c.IsHeader).SumAsync(c => c.CurrentBalance),
            totalCustomers = await _db.Customers.CountAsync(),
            totalSuppliers = await _db.Suppliers.CountAsync(),
            totalItems = await _db.Items.CountAsync(),
            totalReceivables = await _db.Customers.SumAsync(c => c.Balance),
            totalPayables = await _db.Suppliers.SumAsync(s => s.Balance),
            lowStockCount = await _db.Items.CountAsync(i => i.StockQuantity <= i.MinimumStock && i.IsActive)
        });
    }
    #endregion

    #region Export
    [HttpGet("export/coa/csv")]
    public async Task<IActionResult> ExportCOAToCsv()
    {
        if (!await ValidateApiKey()) return Unauthorized("Invalid API Key");
        var coas = await _db.ChartOfAccounts.OrderBy(c => c.AccountCode).ToListAsync();
        var csv = "Kode,Nama,Tipe,Header,Saldo Awal,Saldo Saat Ini,Aktif\n";
        foreach (var c in coas)
            csv += $"{c.AccountCode},{c.AccountName},{c.AccountType},{c.IsHeader},{c.OpeningBalance},{c.CurrentBalance},{c.IsActive}\n";
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "chart_of_accounts.csv");
    }

    [HttpGet("export/items/csv")]
    public async Task<IActionResult> ExportItemsToCsv()
    {
        if (!await ValidateApiKey()) return Unauthorized("Invalid API Key");
        var items = await _db.Items.OrderBy(i => i.ItemCode).ToListAsync();
        var csv = "Kode,Nama,Harga Beli,Harga Jual,Stok,Min Stok,Aktif\n";
        foreach (var i in items)
            csv += $"{i.ItemCode},{i.ItemName},{i.CostPrice},{i.SellingPrice},{i.StockQuantity},{i.MinimumStock},{i.IsActive}\n";
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "items.csv");
    }
    #endregion
}
