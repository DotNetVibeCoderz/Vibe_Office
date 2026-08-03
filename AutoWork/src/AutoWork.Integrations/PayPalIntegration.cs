using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using AutoWork.Core.Agents;
using Microsoft.Extensions.AI;

namespace AutoWork.Integrations;

/// <summary>
/// PayPal transaction history, for the expense-tracking use case.
///
/// Read-only by design. A desktop agent with an LLM in the loop has no business being able to
/// move money, so no payment or refund tool is exposed here regardless of what the credentials
/// would technically permit.
/// </summary>
public sealed class PayPalIntegration : RestConnector
{
    private readonly Lock _gate = new();
    private string? _accessToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public override string Id => "paypal";
    public override string DisplayName => "PayPal";
    public override string Description => "Read transaction history for expense tracking. Read-only.";
    public override string DocsUrl => "https://developer.paypal.com/dashboard/applications";

    public override IReadOnlyList<IntegrationField> Fields =>
    [
        new("clientId", "Client id", "From a REST app in the PayPal developer dashboard."),
        new("clientSecret", "Client secret", "From the same REST app.", Secret: true),
        new("environment", "Environment", "live or sandbox.", Required: false, Placeholder: "live"),
    ];

    private static string BaseUrl(IntegrationCredentials credentials) =>
        string.Equals(credentials.Value("environment"), "sandbox", StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.sandbox.paypal.com"
            : "https://api-m.paypal.com";

    public override async Task<IntegrationStatus> TestAsync(
        IntegrationCredentials credentials, CancellationToken cancellationToken = default)
    {
        try
        {
            await AccessTokenAsync(credentials, cancellationToken).ConfigureAwait(false);
            var environment = string.Equals(credentials.Value("environment"), "sandbox", StringComparison.OrdinalIgnoreCase)
                ? "sandbox" : "live";

            return new IntegrationStatus(true, $"Connected to the {environment} environment.");
        }
        catch (Exception ex) when (ex is IntegrationException or InvalidOperationException or HttpRequestException)
        {
            return new IntegrationStatus(false, ex.Message);
        }
    }

    private async Task<string> AccessTokenAsync(IntegrationCredentials credentials, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _expiresAt) return _accessToken;
        }

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{credentials.Require("clientId")}:{credentials.Require("clientSecret")}"));

        var response = await PostFormAsync($"{BaseUrl(credentials)}/v1/oauth2/token",
            [new("grant_type", "client_credentials")],
            request => request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basic),
            cancellationToken).ConfigureAwait(false);

        var token = response?["access_token"]?.GetValue<string>()
            ?? throw new IntegrationException("PayPal did not return an access token. Check the client id and secret.");

        lock (_gate)
        {
            _accessToken = token;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds((response?["expires_in"]?.GetValue<int>() ?? 3600) - 60);
        }

        return token;
    }

    public override IEnumerable<ToolDescriptor> GetTools(ToolContext context, IntegrationCredentials credentials)
    {
        if (credentials.Any("clientSecret") is null) yield break;

        yield return Tool(AIFunctionFactory.Create(
            ([Description("Start date as YYYY-MM-DD.")] string startDate,
             [Description("End date as YYYY-MM-DD. Defaults to today.")] string? endDate = null) =>
                SafelyAsync(async () =>
                {
                    var token = await AccessTokenAsync(credentials, default).ConfigureAwait(false);

                    if (!DateTime.TryParse(startDate, out var from))
                        return "ERROR: startDate must be in YYYY-MM-DD form.";

                    var to = DateTime.TryParse(endDate, out var parsedEnd) ? parsedEnd : DateTime.Today;

                    // PayPal caps a single transaction query at 31 days.
                    if ((to - from).TotalDays > 31)
                        return "ERROR: PayPal allows at most 31 days per query. Narrow the range and call again.";

                    var url = $"{BaseUrl(credentials)}/v1/reporting/transactions" +
                              $"?start_date={from:yyyy-MM-dd}T00:00:00-0000" +
                              $"&end_date={to:yyyy-MM-dd}T23:59:59-0000" +
                              "&fields=transaction_info,payer_info&page_size=100";

                    var result = await GetAsync(url, Bearer(token)).ConfigureAwait(false);

                    if (result?["transaction_details"] is not JsonArray details || details.Count == 0)
                        return $"No transactions between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}.";

                    var builder = new StringBuilder($"{details.Count} transaction(s):\n");
                    var total = 0m;

                    foreach (var detail in details)
                    {
                        var info = detail?["transaction_info"];
                        var amount = info?["transaction_amount"];
                        var value = amount?["value"]?.GetValue<string>() ?? "0";
                        var currency = amount?["currency_code"]?.GetValue<string>() ?? "";
                        var date = info?["transaction_initiation_date"]?.GetValue<string>();
                        var payer = detail?["payer_info"]?["email_address"]?.GetValue<string>();

                        if (decimal.TryParse(value, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                            total += parsed;

                        builder.AppendLine($"{date?[..10]}  {value,12} {currency}  {payer}  " +
                                           $"{Trim(info?["transaction_subject"]?.GetValue<string>(), 60)}");
                    }

                    builder.AppendLine($"\nNet total: {total:N2}");
                    return builder.ToString();
                }),
            "paypal_list_transactions", "List PayPal transactions in a date range, at most 31 days."));

        yield return Tool(AIFunctionFactory.Create(
            () => SafelyAsync(async () =>
            {
                var token = await AccessTokenAsync(credentials, default).ConfigureAwait(false);
                var result = await GetAsync($"{BaseUrl(credentials)}/v1/reporting/balances", Bearer(token))
                    .ConfigureAwait(false);

                if (result?["balances"] is not JsonArray balances || balances.Count == 0)
                    return "No balances returned.";

                var lines = balances.Select(b =>
                    $"{b?["currency"]}: {b?["total_balance"]?["value"]} " +
                    $"(available {b?["available_balance"]?["value"]})");

                return string.Join('\n', lines);
            }),
            "paypal_balances", "Show current PayPal account balances."));
    }
}
