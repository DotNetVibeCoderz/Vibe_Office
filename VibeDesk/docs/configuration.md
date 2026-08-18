# Configuration

[← README](../README.md) · [Architecture](architecture.md) · [Apps](apps.md) · [API](api.md) · [Mr Clippy](assistant.md) · [Scripts](scripting.md) · [Development](development.md)

Everything lives in `src/VibeDesk.Web/appsettings.json`. Secrets belong in user secrets or environment
variables instead — see [Secrets](#secrets) below.

---

## Database

```jsonc
{
  "Database": {
    "Provider": "Sqlite",          // Sqlite | SqlServer | MySql | PostgreSql
    "MigrateOnStartup": true,
    "SeedSampleData": true,
    "EnableSensitiveDataLogging": false,
    "EnableDetailedErrors": false
  },
  "ConnectionStrings": {
    "Sqlite": "Data Source=App_Data/vibedesk.db",
    "SqlServer": "Server=localhost;Database=VibeDesk;Trusted_Connection=True;TrustServerCertificate=True",
    "MySql": "Server=localhost;Port=3306;Database=VibeDesk;User=root;Password=changeme",
    "PostgreSql": "Host=localhost;Port=5432;Database=VibeDesk;Username=postgres;Password=changeme"
  }
}
```

The connection string is resolved by provider name, then by a `Default` entry, then falls back to local
SQLite — so a misconfigured deployment starts and tells you, rather than failing at the first query.

`SeedSampleData` only takes effect in Development. The seeder refuses to run in any other environment,
because it creates accounts with a published password.

Switching provider needs no rebuild: all four migration assemblies are already referenced.

---

## Cache

```jsonc
{
  "Cache": {
    "Provider": "Memory",          // Memory | Redis
    "InstanceName": "vibedesk:",
    "DefaultTtlSeconds": 300,
    "MemorySizeLimit": 8192
  }
}
```

Redis needs `ConnectionStrings:Redis`. Use it for any deployment with more than one instance —
in-memory caching in a multi-instance deployment means instances disagree about what is current.

---

## File storage

```jsonc
{
  "Storage": {
    "Provider": "FileSystem",      // FileSystem | AzureBlob | S3 | MinIO
    "RootPath": "App_Data/storage",
    "Bucket": "vibedesk",
    "MaxUploadBytes": 268435456,   // 256 MB
    "SignedUrlMinutes": 30,
    "EncryptAtRest": false
  }
}
```

| Provider | Also needs |
|---|---|
| `AzureBlob` | `Storage:ConnectionString` |
| `S3` | `Storage:AccessKey`, `SecretKey`, `Region` |
| `MinIO` | the same, plus `Storage:ServiceUrl` (path-style addressing is enabled automatically) |

`EncryptAtRest: true` wraps whichever provider you chose in AES-256-GCM using `Storage:EncryptionKey`
(base64, 32 bytes). Objects written before it was enabled still read correctly — the format carries a
magic prefix, and anything without it is passed through.

---

## The assistant

```jsonc
{
  "Assistant": {
    "Provider": "OpenAI",          // OpenAI | Anthropic | Google | Ollama
    "Temperature": 0.4,
    "MaxTokens": 4096,
    "MaxHistoryTurns": 20,
    "MaxToolResultChars": 12000,
    "MaxToolIterations": 6,
    "TimeZoneId": "Asia/Jakarta",
    "SystemPrompt": "…",

    "OpenAI":    { "ApiKey": "", "Model": "gpt-4o-mini",     "Models": [ … ], "Endpoint": null },
    "Anthropic": { "ApiKey": "", "Model": "claude-sonnet-5", "Models": [ … ] },
    "Google":    { "ApiKey": "", "Model": "gemini-2.5-flash","Models": [ … ] },
    "Ollama":    { "Endpoint": "http://localhost:11434", "Model": "llama3.2", "Models": [ … ] },

    "Tavily":    { "ApiKey": "", "Endpoint": "https://api.tavily.com/search", "MaxResults": 5 }
  }
}
```

Notes that will save you a confusing hour:

- **A provider with no key is simply absent from the picker.** It is not an error, and the app starts
  fine with every key blank.
- **Ollama needs no key**, so a configured endpoint is the whole requirement — meaning the assistant
  reports itself as configured out of the box. If Ollama is not actually running you get a clear
  connection message rather than a silent failure.
- **`Temperature` is not sent to Anthropic.** Anthropic removed sampling parameters from every model
  after Claude Opus 4.6 and now rejects them with a 400. The setting still applies to the other three.
- **`MaxTokens` covers reasoning as well as the answer** on models that think by default, which is why
  the default is 4096 rather than a chat-sized number.
- **`Tavily:ApiKey` is optional.** Without it the `web_search` tool tells the model it is unconfigured
  instead of failing the turn.
- `OpenAI:Endpoint` points the OpenAI connector at an Azure-style gateway or any OpenAI-compatible
  proxy.

---

## Secrets

Never commit keys. In development:

```bash
cd src/VibeDesk.Web
dotnet user-secrets set "Assistant:OpenAI:ApiKey" "sk-..."
dotnet user-secrets set "ConnectionStrings:PostgreSql" "Host=...;Password=..."
dotnet user-secrets set "Storage:EncryptionKey" "$(openssl rand -base64 32)"
```

In production use environment variables — `__` is the section separator:

```bash
export Assistant__Anthropic__ApiKey="sk-ant-..."
export Database__Provider="PostgreSql"
export Storage__EncryptAtRest="true"
```

---

## Branding

```jsonc
{ "Branding": { "Studio": "Gravicode Studios", "Lead": "Kang Fadhil" } }
```

Surfaced in the app shell and in the assistant's system prompt.
