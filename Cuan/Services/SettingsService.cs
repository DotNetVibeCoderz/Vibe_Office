using System.Collections.Concurrent;
using System.Globalization;
using Cuan.Data;
using Cuan.Models;
using Microsoft.EntityFrameworkCore;

namespace Cuan.Services;

/// <summary>
/// Pembaca parameter sistem dengan cache di memori.
///
/// Singleton: nilainya dibaca sekali dari tabel SystemSettings lalu dipakai
/// bersama seluruh circuit. Halaman Pengaturan memanggil <see cref="SaveAsync"/>
/// yang menulis ke basis data sekaligus menyegarkan cache, jadi perubahan
/// langsung terasa tanpa perlu restart.
/// </summary>
public class SettingsService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _loaded;

    public SettingsService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    /// <summary>Dipanggil ketika ada parameter yang berubah, supaya UI bisa menggambar ulang.</summary>
    public event Action? Changed;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        await _gate.WaitAsync();
        try
        {
            if (_loaded) return;
            await ReloadCoreAsync();
            _loaded = true;
        }
        finally { _gate.Release(); }
    }

    public async Task ReloadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await ReloadCoreAsync();
            _loaded = true;
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }

    private async Task ReloadCoreAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.SystemSettings.AsNoTracking().ToListAsync();

        _cache.Clear();
        foreach (var def in SettingsCatalog.All)
            _cache[def.Key] = def.Default;
        foreach (var row in rows)
            _cache[row.SettingKey] = row.SettingValue ?? string.Empty;
    }

    // ---------- Pembacaan ----------

    public string Get(string key, string? fallback = null)
    {
        if (!_loaded) EnsureLoadedAsync().GetAwaiter().GetResult();
        if (_cache.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
        return fallback ?? SettingsCatalog.DefaultOf(key);
    }

    public bool GetBool(string key, bool fallback = false)
        => bool.TryParse(Get(key, fallback.ToString()), out var b) ? b : fallback;

    public int GetInt(string key, int fallback = 0)
        => int.TryParse(Get(key, fallback.ToString()), NumberStyles.Any, CultureInfo.InvariantCulture, out var i) ? i : fallback;

    public decimal GetDecimal(string key, decimal fallback = 0)
        => decimal.TryParse(Get(key, fallback.ToString(CultureInfo.InvariantCulture)),
            NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : fallback;

    /// <summary>Tarif dalam persen dikembalikan sebagai pengali, mis. 11 → 0.11.</summary>
    public decimal GetRate(string key, decimal fallbackPercent = 0)
        => GetDecimal(key, fallbackPercent) / 100m;

    // ---------- Penulisan ----------

    public async Task SaveAsync(IDictionary<string, string> values)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = await db.SystemSettings.ToListAsync();
        var byKey = existing.ToDictionary(s => s.SettingKey, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in values)
        {
            var def = SettingsCatalog.Find(key);
            if (byKey.TryGetValue(key, out var row))
            {
                if (row.SettingValue == value) continue;
                row.SettingValue = value;
                row.UpdatedAt = DateTime.UtcNow;
                row.Group = def?.Group ?? row.Group;
                row.Description = def?.Description ?? row.Description;
            }
            else
            {
                db.SystemSettings.Add(new SystemSetting
                {
                    SettingKey = key,
                    SettingValue = value,
                    Group = def?.Group ?? "Lainnya",
                    Description = def?.Description,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        await db.SaveChangesAsync();
        await ReloadAsync();
    }

    public Task SaveOneAsync(string key, string value)
        => SaveAsync(new Dictionary<string, string> { [key] = value });

    /// <summary>Melengkapi tabel SystemSettings dengan parameter baru dari katalog.</summary>
    public async Task SyncCatalogAsync(AppDbContext db)
    {
        var existing = await db.SystemSettings.Select(s => s.SettingKey).ToListAsync();
        var have = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var def in SettingsCatalog.All)
        {
            if (have.Contains(def.Key)) continue;
            db.SystemSettings.Add(new SystemSetting
            {
                SettingKey = def.Key,
                SettingValue = def.Default,
                Group = def.Group,
                Description = def.Description ?? def.Label,
                UpdatedAt = DateTime.UtcNow
            });
            added++;
        }

        if (added > 0) await db.SaveChangesAsync();
        await ReloadAsync();
    }

    // ---------- Format tampilan ----------

    public CultureInfo Culture
    {
        get
        {
            try { return CultureInfo.GetCultureInfo(Get(SettingsCatalog.Locale, "id-ID")); }
            catch (CultureNotFoundException) { return CultureInfo.GetCultureInfo("id-ID"); }
        }
    }

    public string DateFormat => Get(SettingsCatalog.DateFormat, "dd/MM/yyyy");

    public string FormatDate(DateTime value) => value.ToString(DateFormat, Culture);

    /// <summary>Format uang mengikuti simbol, jumlah desimal, dan locale dari Pengaturan.</summary>
    public string Money(decimal amount)
    {
        var decimals = GetInt(SettingsCatalog.CurrencyDecimals, 0);
        var text = amount.ToString("N" + decimals, Culture);
        if (!GetBool(SettingsCatalog.CurrencyShowSymbol, true)) return text;
        var symbol = Get(SettingsCatalog.CurrencySymbol, "Rp");
        return string.IsNullOrWhiteSpace(symbol) ? text : $"{symbol} {text}";
    }

    /// <summary>Angka saja, tanpa simbol — untuk kolom tabel yang sudah berjudul mata uang.</summary>
    public string Number(decimal amount, int? decimals = null)
        => amount.ToString("N" + (decimals ?? GetInt(SettingsCatalog.CurrencyDecimals, 0)), Culture);

    public string CompanyName => Get(SettingsCatalog.CompanyName);
    public string CompanyNpwp => Get(SettingsCatalog.CompanyTaxNumber);
    public bool IsPkp => GetBool(SettingsCatalog.CompanyIsPkp, true);
    public decimal PpnRate => GetRate(SettingsCatalog.PpnRate, 11);
    public bool PpnEnabled => GetBool(SettingsCatalog.PpnEnabled, true) && IsPkp;
}
