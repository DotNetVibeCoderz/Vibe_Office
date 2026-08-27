using Microsoft.AspNetCore.SignalR;

namespace Cuan.Services;

/// <summary>
/// SignalR Hub untuk notifikasi real-time ke semua client
/// </summary>
public class NotificationHub : Hub
{
    /// <summary>Kirim notifikasi ke semua client</summary>
    public async Task SendNotification(string title, string message, string type = "Info")
    {
        await Clients.All.SendAsync("ReceiveNotification", new
        {
            Title = title,
            Message = message,
            Type = type,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>Notifikasi stok rendah</summary>
    public async Task SendLowStockAlert(string itemName, decimal currentStock, decimal minStock)
    {
        await Clients.All.SendAsync("ReceiveNotification", new
        {
            Title = "⚠️ Stok Rendah",
            Message = $"{itemName} tersisa {currentStock} (min: {minStock})",
            Type = "Warning",
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>Notifikasi transaksi baru</summary>
    public async Task SendTransactionAlert(string transType, string number, decimal amount)
    {
        await Clients.All.SendAsync("ReceiveNotification", new
        {
            Title = $"📝 {transType} Baru",
            Message = $"#{number} — Rp {amount:N0}",
            Type = "Info",
            Timestamp = DateTime.UtcNow
        });
    }

    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("ReceiveNotification", new
        {
            Title = "🔌 Terhubung",
            Message = "Notifikasi real-time aktif",
            Type = "Success",
            Timestamp = DateTime.UtcNow
        });
        await base.OnConnectedAsync();
    }
}

/// <summary>
/// Service untuk mengirim notifikasi via SignalR dan menyimpan ke DB
/// </summary>
public class NotificationService
{
    private readonly IHubContext<NotificationHub> _hub;
    private readonly Data.AppDbContext _db;

    public NotificationService(IHubContext<NotificationHub> hub, Data.AppDbContext db)
    {
        _hub = hub;
        _db = db;
    }

    public async Task NotifyAsync(string title, string message, string type = "Info", string? userId = null, string? link = null)
    {
        // Simpan ke DB
        _db.Notifications.Add(new Cuan.Models.Notification
        {
            UserId = userId,
            Title = title,
            Message = message,
            Type = type,
            Link = link,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        // Kirim via SignalR
        await _hub.Clients.All.SendAsync("ReceiveNotification", new
        {
            Title = title,
            Message = message,
            Type = type,
            Timestamp = DateTime.UtcNow
        });
    }
}
