using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Cuan.Data;
using Cuan.Models;
using Microsoft.EntityFrameworkCore;

namespace Cuan.Services;

public sealed record DjpResult(bool Success, string Message, int? HttpStatus = null, string? Payload = null);

/// <summary>
/// Penghubung ke layanan pajak DJP (Coretax / e-Faktur host-to-host).
///
/// Seluruh alamat dan kredensial diambil dari Pengaturan → Integrasi DJP,
/// jadi berpindah dari sandbox ke produksi tidak perlu mengubah kode.
///
/// Catatan penting: akses host-to-host DJP hanya terbuka untuk wajib pajak
/// yang sudah mendaftar dan memperoleh client id, secret, serta sertifikat
/// elektronik. Selama kredensial itu belum diisi, layanan ini menolak dengan
/// pesan yang jelas dan pelaporan tetap bisa ditempuh lewat berkas ekspor
/// e-Faktur di halaman Faktur Pajak. Setiap percakapan dengan DJP dicatat di
/// tabel DjpSubmission supaya bisa ditelusuri kembali.
/// </summary>
public class DjpClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly SettingsService _settings;
    private readonly AppDbContext _db;

    public DjpClient(IHttpClientFactory httpFactory, SettingsService settings, AppDbContext db)
    {
        _httpFactory = httpFactory;
        _settings = settings;
        _db = db;
    }

    public bool Enabled => _settings.GetBool(SettingsCatalog.DjpEnabled, false);
    public string Mode => _settings.Get(SettingsCatalog.DjpMode, "Sandbox");
    public string BaseUrl => _settings.Get(SettingsCatalog.DjpBaseUrl).TrimEnd('/');

    /// <summary>Alasan mengapa integrasi belum siap dipakai, atau null bila sudah siap.</summary>
    public string? ReadinessProblem()
    {
        if (!Enabled) return "Integrasi DJP masih nonaktif. Aktifkan di Pengaturan → Integrasi DJP.";
        if (string.IsNullOrWhiteSpace(BaseUrl)) return "Alamat layanan DJP belum diisi.";
        if (string.IsNullOrWhiteSpace(_settings.Get(SettingsCatalog.DjpClientId)))
            return "Client ID belum diisi. Nilai ini diperoleh saat mendaftar akses host-to-host di DJP.";
        if (string.IsNullOrWhiteSpace(_settings.Get(SettingsCatalog.DjpClientSecret)))
            return "Client secret belum diisi.";
        if (string.IsNullOrWhiteSpace(_settings.Get(SettingsCatalog.DjpNpwpUser)))
            return "NPWP pengguna aplikasi belum diisi.";
        return null;
    }

    private HttpClient CreateClient()
    {
        var client = _httpFactory.CreateClient("djp");
        client.Timeout = TimeSpan.FromSeconds(_settings.GetInt(SettingsCatalog.DjpTimeoutSeconds, 30));
        return client;
    }

    // =====================================================================
    // Operasi
    // =====================================================================

    /// <summary>Memeriksa apakah kredensial dan alamat yang tersimpan bisa dipakai.</summary>
    public async Task<DjpResult> TestConnectionAsync(string? userName = null)
    {
        var problem = ReadinessProblem();
        if (problem is not null)
            return await LogAsync("TestConnection", null, false, null, null, null, problem, userName);

        try
        {
            var token = await RequestTokenAsync();
            return await LogAsync("TestConnection", null, token.Success, token.HttpStatus,
                null, token.Payload, token.Message, userName);
        }
        catch (Exception ex)
        {
            return await LogAsync("TestConnection", null, false, null, null, null,
                $"Gagal menghubungi {BaseUrl}: {ex.Message}", userName);
        }
    }

    /// <summary>Mengirim satu faktur pajak ke DJP dan menyimpan nomor approval-nya.</summary>
    public async Task<DjpResult> SubmitTaxInvoiceAsync(int taxInvoiceId, string? userName = null)
    {
        var faktur = await _db.TaxInvoices
            .Include(t => t.SalesInvoice)!.ThenInclude(s => s!.Details)!.ThenInclude(d => d.Item)
            .Include(t => t.Customer)
            .FirstOrDefaultAsync(t => t.Id == taxInvoiceId);

        if (faktur is null)
            return new DjpResult(false, "Faktur pajak tidak ditemukan.");

        var problem = ReadinessProblem();
        if (problem is not null)
        {
            faktur.DjpMessage = problem;
            await _db.SaveChangesAsync();
            return await LogAsync("SubmitFaktur", faktur.FakturNumber, false, null, null, null, problem, userName);
        }

        var payload = BuildInvoicePayload(faktur);
        var body = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        try
        {
            var token = await RequestTokenAsync();
            if (!token.Success)
            {
                faktur.DjpMessage = token.Message;
                await _db.SaveChangesAsync();
                return await LogAsync("SubmitFaktur", faktur.FakturNumber, false, token.HttpStatus,
                    body, token.Payload, token.Message, userName);
            }

            var client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Payload);

            var endpoint = $"{BaseUrl}/api/v1/tax-invoices";
            var response = await client.PostAsync(endpoint,
                new StringContent(body, Encoding.UTF8, "application/json"));
            var responseText = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                faktur.Status = TaxInvoiceStatus.Submitted;
                faktur.DjpSubmittedAt = DateTime.UtcNow;
                faktur.DjpApprovalCode = TryReadApproval(responseText) ?? faktur.DjpApprovalCode;
                faktur.DjpMessage = "Diterima DJP.";
                if (faktur.DjpApprovalCode is not null) faktur.Status = TaxInvoiceStatus.Approved;
            }
            else
            {
                faktur.Status = TaxInvoiceStatus.Rejected;
                faktur.DjpMessage = $"Ditolak DJP ({(int)response.StatusCode}).";
            }

            await _db.SaveChangesAsync();

            return await LogAsync("SubmitFaktur", faktur.FakturNumber, response.IsSuccessStatusCode,
                (int)response.StatusCode, body, responseText, faktur.DjpMessage, userName);
        }
        catch (Exception ex)
        {
            faktur.DjpMessage = ex.Message;
            await _db.SaveChangesAsync();
            return await LogAsync("SubmitFaktur", faktur.FakturNumber, false, null, body, null,
                $"Gagal mengirim: {ex.Message}", userName);
        }
    }

    /// <summary>Melaporkan SPT Masa PPN untuk satu masa pajak.</summary>
    public async Task<DjpResult> SubmitReturnAsync(int taxReturnId, string? userName = null)
    {
        var ret = await _db.TaxReturns.FirstOrDefaultAsync(r => r.Id == taxReturnId);
        if (ret is null) return new DjpResult(false, "SPT tidak ditemukan.");

        var reference = $"{ret.ReturnType}-{ret.PeriodYear}-{ret.PeriodMonth:00}";
        var problem = ReadinessProblem();
        if (problem is not null)
            return await LogAsync("SubmitSpt", reference, false, null, null, null, problem, userName);

        var payload = new
        {
            tin = new string(_settings.CompanyNpwp.Where(char.IsDigit).ToArray()),
            nitku = _settings.Get(SettingsCatalog.CompanyNitku),
            returnType = ret.ReturnType,
            taxPeriodMonth = ret.PeriodMonth,
            taxPeriodYear = ret.PeriodYear,
            revision = ret.Revision,
            outputTaxBase = ret.OutputDpp,
            outputTax = ret.OutputTax,
            inputTaxBase = ret.InputDpp,
            inputTax = ret.InputTax,
            carryForward = ret.CarryForward,
            payableAmount = ret.PayableAmount
        };
        var body = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        try
        {
            var token = await RequestTokenAsync();
            if (!token.Success)
                return await LogAsync("SubmitSpt", reference, false, token.HttpStatus, body, token.Payload,
                    token.Message, userName);

            var client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Payload);

            var endpoint = $"{BaseUrl}/api/v1/tax-returns";
            var response = await client.PostAsync(endpoint,
                new StringContent(body, Encoding.UTF8, "application/json"));
            var responseText = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                ret.Status = TaxReturnStatus.Submitted;
                ret.SubmittedAt = DateTime.UtcNow;
                ret.NtteNumber = TryReadNtte(responseText) ?? ret.NtteNumber;
                if (ret.NtteNumber is not null) ret.Status = TaxReturnStatus.Accepted;
                await _db.SaveChangesAsync();
            }

            return await LogAsync("SubmitSpt", reference, response.IsSuccessStatusCode,
                (int)response.StatusCode, body, responseText,
                response.IsSuccessStatusCode ? "SPT terkirim." : "SPT ditolak DJP.", userName);
        }
        catch (Exception ex)
        {
            return await LogAsync("SubmitSpt", reference, false, null, body, null,
                $"Gagal mengirim: {ex.Message}", userName);
        }
    }

    // =====================================================================
    // Internal
    // =====================================================================

    /// <summary>Mengambil access token lewat alur client credentials.</summary>
    private async Task<DjpResult> RequestTokenAsync()
    {
        var client = CreateClient();
        var endpoint = $"{BaseUrl}/oauth2/token";

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _settings.Get(SettingsCatalog.DjpClientId),
            ["client_secret"] = _settings.Get(SettingsCatalog.DjpClientSecret),
            ["scope"] = "tax-invoice tax-return"
        });

        var response = await client.PostAsync(endpoint, form);
        var text = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            return new DjpResult(false,
                $"Layanan DJP menolak kredensial ({(int)response.StatusCode}). Periksa client id dan secret.",
                (int)response.StatusCode, text);

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("access_token", out var token))
                return new DjpResult(true, "Kredensial diterima DJP.", (int)response.StatusCode, token.GetString());
        }
        catch (JsonException) { /* jawaban bukan JSON — jatuh ke pesan di bawah */ }

        return new DjpResult(false, "Jawaban DJP tidak memuat access_token.", (int)response.StatusCode, text);
    }

    private object BuildInvoicePayload(TaxInvoice faktur) => new
    {
        tin = new string(_settings.CompanyNpwp.Where(char.IsDigit).ToArray()),
        sellerIdtku = _settings.Get(SettingsCatalog.CompanyNitku),
        taxInvoiceDate = faktur.FakturDate.ToString("yyyy-MM-dd"),
        serialNumber = faktur.FakturNumber,
        trxCode = faktur.TransactionCode,
        replacementFlag = faktur.StatusCode,
        buyerTin = new string((faktur.BuyerNpwp ?? string.Empty).Where(char.IsDigit).ToArray()),
        buyerName = faktur.BuyerName,
        buyerAddress = faktur.BuyerAddress,
        refDesc = faktur.SalesInvoice?.InvoiceNumber,
        taxBase = faktur.Dpp,
        vat = faktur.PpnAmount,
        vatRate = faktur.PpnRate,
        stlg = faktur.PpnbmAmount,
        goods = (faktur.SalesInvoice?.Details ?? new List<SalesInvoiceDetail>()).Select(d => new
        {
            code = d.Item?.ItemCode,
            name = d.Item?.ItemName ?? d.Description,
            price = d.UnitPrice,
            qty = d.Quantity,
            discount = d.DiscountAmount,
            taxBase = d.LineTotal,
            vatRate = d.TaxPercent,
            vat = d.TaxAmount
        })
    };

    private static string? TryReadApproval(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var name in new[] { "approvalCode", "approval_code", "nomorApproval" })
                if (doc.RootElement.TryGetProperty(name, out var v)) return v.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    private static string? TryReadNtte(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var name in new[] { "ntte", "ntteNumber", "nomorNtte" })
                if (doc.RootElement.TryGetProperty(name, out var v)) return v.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    private async Task<DjpResult> LogAsync(string operation, string? documentNumber, bool success,
        int? httpStatus, string? request, string? response, string? message, string? userName)
    {
        _db.DjpSubmissions.Add(new DjpSubmission
        {
            Operation = operation,
            DocumentNumber = documentNumber,
            Mode = Mode,
            Endpoint = BaseUrl,
            Success = success,
            HttpStatus = httpStatus,
            RequestPayload = Trim(request),
            ResponsePayload = Trim(response),
            Message = message,
            CreatedBy = userName
        });
        await _db.SaveChangesAsync();
        return new DjpResult(success, message ?? string.Empty, httpStatus, response);
    }

    private static string? Trim(string? value)
        => value is null ? null : value.Length > 8000 ? value[..8000] + "…" : value;
}
