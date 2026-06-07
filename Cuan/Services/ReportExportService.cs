using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Cuan.Services;

/// <summary>
/// Service khusus untuk export report dashboard (Excel & PDF)
/// </summary>
public class ReportExportService
{
    public byte[] ExportExecutiveDashboardExcel(ExecutiveDashboardReport report)
    {
        using var wb = new XLWorkbook();

        var wsSummary = wb.Worksheets.Add("Summary");
        wsSummary.Cell(1, 1).Value = "Metric";
        wsSummary.Cell(1, 2).Value = "Value";
        wsSummary.Cell(2, 1).Value = "Total Penjualan";
        wsSummary.Cell(2, 2).Value = report.TotalSales;
        wsSummary.Cell(3, 1).Value = "Total Pembelian";
        wsSummary.Cell(3, 2).Value = report.TotalPurchases;
        wsSummary.Cell(4, 1).Value = "Laba Kotor";
        wsSummary.Cell(4, 2).Value = report.GrossProfit;
        wsSummary.Cell(5, 1).Value = "Kas & Bank";
        wsSummary.Cell(5, 2).Value = report.CashBank;
        wsSummary.Columns().AdjustToContents();

        var wsMonthly = wb.Worksheets.Add("Monthly");
        wsMonthly.Cell(1, 1).Value = "Month";
        wsMonthly.Cell(1, 2).Value = "Sales";
        wsMonthly.Cell(1, 3).Value = "Purchases";
        wsMonthly.Cell(1, 4).Value = "Cash In";
        wsMonthly.Cell(1, 5).Value = "Cash Out";
        for (int i = 0; i < report.MonthlySales.Count; i++)
        {
            wsMonthly.Cell(i + 2, 1).Value = report.MonthlySales[i].Month;
            wsMonthly.Cell(i + 2, 2).Value = report.MonthlySales[i].Value;
            wsMonthly.Cell(i + 2, 3).Value = report.MonthlyPurchases[i].Value;
            wsMonthly.Cell(i + 2, 4).Value = report.MonthlyCashIn[i].Value;
            wsMonthly.Cell(i + 2, 5).Value = report.MonthlyCashOut[i].Value;
        }
        wsMonthly.Columns().AdjustToContents();

        var wsCustomers = wb.Worksheets.Add("TopCustomers");
        wsCustomers.Cell(1, 1).Value = "Customer";
        wsCustomers.Cell(1, 2).Value = "Total Sales";
        wsCustomers.Cell(1, 3).Value = "Receivable";
        for (int i = 0; i < report.TopCustomers.Count; i++)
        {
            wsCustomers.Cell(i + 2, 1).Value = report.TopCustomers[i].Name;
            wsCustomers.Cell(i + 2, 2).Value = report.TopCustomers[i].TotalSales;
            wsCustomers.Cell(i + 2, 3).Value = report.TopCustomers[i].Balance;
        }
        wsCustomers.Columns().AdjustToContents();

        var wsOverdue = wb.Worksheets.Add("OverdueInvoices");
        wsOverdue.Cell(1, 1).Value = "Invoice";
        wsOverdue.Cell(1, 2).Value = "Customer";
        wsOverdue.Cell(1, 3).Value = "Due Date";
        wsOverdue.Cell(1, 4).Value = "Remaining";
        for (int i = 0; i < report.OverdueInvoices.Count; i++)
        {
            wsOverdue.Cell(i + 2, 1).Value = report.OverdueInvoices[i].InvoiceNumber;
            wsOverdue.Cell(i + 2, 2).Value = report.OverdueInvoices[i].CustomerName;
            wsOverdue.Cell(i + 2, 3).Value = report.OverdueInvoices[i].DueDate.ToString("yyyy-MM-dd");
            wsOverdue.Cell(i + 2, 4).Value = report.OverdueInvoices[i].RemainingAmount;
        }
        wsOverdue.Columns().AdjustToContents();

        var wsItems = wb.Worksheets.Add("TopItems");
        wsItems.Cell(1, 1).Value = "Item";
        wsItems.Cell(1, 2).Value = "Qty";
        wsItems.Cell(1, 3).Value = "Revenue";
        for (int i = 0; i < report.TopItems.Count; i++)
        {
            wsItems.Cell(i + 2, 1).Value = report.TopItems[i].ItemName;
            wsItems.Cell(i + 2, 2).Value = report.TopItems[i].TotalQty;
            wsItems.Cell(i + 2, 3).Value = report.TopItems[i].TotalRevenue;
        }
        wsItems.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] ExportExecutiveDashboardPdf(ExecutiveDashboardReport report)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Size(PageSizes.A4);
                page.Header().Text("Executive Dashboard - CUAN").FontSize(18).SemiBold();

                page.Content().Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Text($"Periode: {report.PeriodLabel}").FontSize(10).FontColor(Colors.Grey.Darken1);

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.RelativeColumn();
                        });

                        table.Header(h =>
                        {
                            h.Cell().Element(CellStyle).Text("Metric");
                            h.Cell().Element(CellStyle).Text("Value");
                        });

                        table.Cell().Element(CellStyle).Text("Total Penjualan");
                        table.Cell().Element(CellStyle).Text(report.TotalSales.ToString("N0"));
                        table.Cell().Element(CellStyle).Text("Total Pembelian");
                        table.Cell().Element(CellStyle).Text(report.TotalPurchases.ToString("N0"));
                        table.Cell().Element(CellStyle).Text("Laba Kotor");
                        table.Cell().Element(CellStyle).Text(report.GrossProfit.ToString("N0"));
                        table.Cell().Element(CellStyle).Text("Kas & Bank");
                        table.Cell().Element(CellStyle).Text(report.CashBank.ToString("N0"));
                    });

                    col.Item().Text("Top 5 Customer").SemiBold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2);
                            c.RelativeColumn();
                            c.RelativeColumn();
                        });
                        table.Header(h =>
                        {
                            h.Cell().Element(CellStyle).Text("Customer");
                            h.Cell().Element(CellStyle).Text("Total Sales");
                            h.Cell().Element(CellStyle).Text("Piutang");
                        });

                        foreach (var c in report.TopCustomers)
                        {
                            table.Cell().Element(CellStyle).Text(c.Name);
                            table.Cell().Element(CellStyle).Text(c.TotalSales.ToString("N0"));
                            table.Cell().Element(CellStyle).Text(c.Balance.ToString("N0"));
                        }
                    });

                    col.Item().Text("Invoice Jatuh Tempo").SemiBold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.RelativeColumn(2);
                            c.RelativeColumn();
                            c.RelativeColumn();
                        });
                        table.Header(h =>
                        {
                            h.Cell().Element(CellStyle).Text("Invoice");
                            h.Cell().Element(CellStyle).Text("Customer");
                            h.Cell().Element(CellStyle).Text("Due Date");
                            h.Cell().Element(CellStyle).Text("Remaining");
                        });

                        foreach (var i in report.OverdueInvoices)
                        {
                            table.Cell().Element(CellStyle).Text(i.InvoiceNumber);
                            table.Cell().Element(CellStyle).Text(i.CustomerName);
                            table.Cell().Element(CellStyle).Text(i.DueDate.ToString("dd/MM/yyyy"));
                            table.Cell().Element(CellStyle).Text(i.RemainingAmount.ToString("N0"));
                        }
                    });
                });

                page.Footer().AlignCenter().Text(txt =>
                {
                    txt.Span("Generated at ").FontSize(9);
                    txt.Span(DateTime.Now.ToString("dd MMM yyyy HH:mm")).FontSize(9);
                });
            });
        });

        return document.GeneratePdf();
    }

    private IContainer CellStyle(IContainer container)
    {
        return container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(4);
    }
}

public class ExecutiveDashboardReport
{
    public string PeriodLabel { get; set; } = "";
    public decimal TotalSales { get; set; }
    public decimal TotalPurchases { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal CashBank { get; set; }
    public List<MonthlyValue> MonthlySales { get; set; } = new();
    public List<MonthlyValue> MonthlyPurchases { get; set; } = new();
    public List<MonthlyValue> MonthlyCashIn { get; set; } = new();
    public List<MonthlyValue> MonthlyCashOut { get; set; } = new();
    public List<CustomerSummary> TopCustomers { get; set; } = new();
    public List<InvoiceSummary> OverdueInvoices { get; set; } = new();
    public List<ItemSummary> TopItems { get; set; } = new();
}

public class MonthlyValue { public string Month { get; set; } = string.Empty; public decimal Value { get; set; } }
public class CustomerSummary { public string Name { get; set; } = string.Empty; public decimal TotalSales { get; set; } public decimal Balance { get; set; } }
public class InvoiceSummary { public string InvoiceNumber { get; set; } = string.Empty; public string CustomerName { get; set; } = string.Empty; public DateTime DueDate { get; set; } public decimal RemainingAmount { get; set; } }
public class ItemSummary { public string ItemName { get; set; } = string.Empty; public decimal TotalQty { get; set; } public decimal TotalRevenue { get; set; } }
