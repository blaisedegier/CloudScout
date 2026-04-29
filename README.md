# CloudScout ☁️

A pluggable document classifier that connects to your cloud storage, recursively scans every file, and suggests which ones matter — using a tiered AI pipeline that escalates from cheap metadata heuristics to a self-hosted multimodal LLM only when necessary.

Built with .NET 10, Gemma 4 E2B, and an extensible JSON taxonomy system.

## How It Works

CloudScout classifies files through three tiers, each progressively more expensive. The pipeline short-circuits as soon as confidence is high enough — most files never touch the LLM.

| Tier   | Method                                                      | Speed  | When it runs                |
| ------ | ----------------------------------------------------------- | ------ | --------------------------- |
| **T0** | Filename, folder path, MIME type keywords                   | ~1ms   | Always                      |
| **T1** | Text extraction + keyword/phrase matching (see table below) | ~100ms | When T0 confidence < 70%    |
| **T3** | Gemma 4 E2B via llama-server (OpenAI-compatible API)        | ~5-10s | When T0+T1 confidence < 70% |

### Tier 1 Format Support

Modern office formats are read natively so classification happens on real content, not filename guesses. Tier 3 is the fallback for ambiguous cases — not the workhorse.

| Format family             | Extensions                                  | Tier            | Library                                  |
| ------------------------- | ------------------------------------------- | --------------- | ---------------------------------------- |
| PDF                       | `.pdf`                                      | Tier 1          | PdfPig                                   |
| Microsoft Office (modern) | `.docx`, `.xlsx`, `.xlsm`, `.pptx`, `.pptm` | Tier 1          | DocumentFormat.OpenXml                   |
| OpenDocument              | `.odt`, `.ods`, `.odp`                      | Tier 1          | System.IO.Compression + System.Xml (BCL) |
| Rich Text                 | `.rtf`                                      | Tier 1          | Custom parser (no dependency)            |
| Plain text                | `.txt`, `.csv`, `.md`, `.log`               | Tier 1          | StreamReader                             |
| Images                    | `.jpg`, `.jpeg`, `.png`, `.webp`, `.gif`    | Tier 3 (vision) | Gemma 4 multimodal projector             |

Image classification requires a multimodal projector loaded into llama-server (`Llm:MmprojPath` in your config). Without it, image bytes still get sent to the model but the projector isn't available, so classification falls back to filename + folder metadata only.

Legacy binary formats (`.doc`, `.xls`, `.ppt`) fall through to Tier 3 — the .NET ecosystem has no clean permissive-licensed reader for them on modern runtimes.

Files are classified against a **pluggable JSON taxonomy**. The default covers 30 categories across Financial, Legal, Identity, Medical, Academic, Vehicle, Personal Memories, and Reference documents. You can author your own taxonomy for any domain.

```bash
cloudscout connect onedrive    # OAuth sign-in (one-time)
cloudscout scan                # Crawl + classify (auto-launches LLM server)
cloudscout results             # Review suggestions grouped by category
```

### Example Output

```text
  Payslips  (3 files)
    100% T3  /Pay Slips/February.pdf  [LLM: detailed salary information, deductions, and net pay]
    100% T3  /Pay Slips/January.pdf   [LLM: detailed salary information, deductions, and net pay]
    100% T3  /Pay Slips/March.pdf     [LLM: detailed salary information, deductions, and net pay]

  Vehicle Registration  (2 files)
     85% T0  /Car/COR.pdf             [filename 'cor'; folder 'car'; mime application/pdf]
     85% T0  /Car/Log Book.pdf        [filename 'log book'; folder 'car'; mime application/pdf]

  Employment Contracts  (1 file)
     85% T1  /Employment Contract.pdf  [3 keyword hits, 1 phrase hit in content]

  Unclassified  (3 files)
      --     /Prompts/Auto-Verify.txt
      --     /Prompts/Humanise.txt
      --     /Prompts/Sceptic.txt
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.201+)
- At least one personal cloud account: OneDrive, Google Drive, or Dropbox (see [Limitations](#limitations))
- Access to the relevant developer console for the provider(s) you want to use (all free): [portal.azure.com](https://portal.azure.com), [console.cloud.google.com](https://console.cloud.google.com), or [dropbox.com/developers/apps](https://www.dropbox.com/developers/apps)
- **For Tier 3 (optional but recommended):** a Gemma 4 E2B GGUF model file + llama-server binary

## Quick Start

### 1. Clone and build

```bash
git clone https://github.com/YOUR-USERNAME/CloudScout.git
cd CloudScout
dotnet build
```

### 2. Register an app with your chosen provider

You need a developer-console app registration for **each** provider you want to use. They're independent — pick one and ignore the others if you only want a single provider.

- **OneDrive** → follow the steps below (Azure portal)
- **Google Drive** → skip ahead to [Additional Provider Setup > Google Drive](#google-drive), then return to step 3
- **Dropbox** → skip ahead to [Additional Provider Setup > Dropbox](#dropbox), then return to step 3

#### OneDrive (Microsoft Entra ID)

CloudScout needs an Entra ID (Azure AD) app registration so Microsoft will allow it to read your OneDrive files. This is free.

1. Go to **[portal.azure.com](https://portal.azure.com) > Microsoft Entra ID > App registrations > New registration**
2. **Name:** `CloudScout`
3. **Supported account types:** _Accounts in any organizational directory and personal Microsoft accounts_
4. **Redirect URI:** leave blank (the CLI uses device-code flow)
5. Click **Register**, then copy the **Application (client) ID**

**Enable public client flows:**

- Go to **Authentication > Advanced settings > Allow public client flows > Yes > Save**

**Grant permissions:**

- Go to **API permissions > Add a permission > Microsoft Graph > Delegated permissions**
- Add: `Files.Read`, `User.Read`, `offline_access`

### 3. Configure

```bash
cp src/CloudScout.Cli/appsettings.Local.json.example src/CloudScout.Cli/appsettings.Local.json
```

Edit `appsettings.Local.json` and fill in **only the provider section(s) you registered in step 2**. Leave the others as empty strings — providers with empty config are silently skipped at startup.

```json
{
  "Authentication": {
    "Microsoft": { "ClientId": "YOUR-APPLICATION-CLIENT-ID" },
    "Google": {
      "ClientId": "...apps.googleusercontent.com",
      "ClientSecret": "..."
    },
    "Dropbox": { "AppKey": "..." }
  }
}
```

### 4. Connect a cloud provider

CloudScout supports OneDrive, Google Drive, and Dropbox. Pick whichever you have files in:

```bash
cloudscout connect onedrive       # device-code flow — paste the URL + code in your browser
cloudscout connect googledrive    # opens the system browser, captures the redirect on localhost
cloudscout connect dropbox        # opens the system browser, PKCE flow
```

You can connect multiple providers; each one ends up as a separate `CloudConnection` row and is scanned independently.

For Google Drive and Dropbox you'll first need to register an app in their respective consoles — see [Additional Provider Setup](#additional-provider-setup-google-drive--dropbox) below.

### 5. Scan and review

```bash
cloudscout scan
cloudscout results
```

That's it. Tier 0 and Tier 1 run without any additional setup. For Tier 3 (LLM), see the next section.

## Tier 3: Local LLM Setup (Optional)

Tier 3 uses [Gemma 4 E2B](https://huggingface.co/ggml-org/gemma-4-E2B-it-GGUF) — a 4.6B parameter model that runs on CPU. Without it, CloudScout still works (Tier 0 + Tier 1 handle most documents); Tier 3 fills in the ambiguous cases.

### Download the model

1. Go to [ggml-org/gemma-4-E2B-it-GGUF](https://huggingface.co/ggml-org/gemma-4-E2B-it-GGUF) on Hugging Face
2. Download `gemma-4-E2B-it-Q8_0.gguf` (~5 GB)
3. Place it in the `models/` directory at the repo root

### Download llama-server

1. Go to [llama.cpp releases](https://github.com/ggml-org/llama.cpp/releases)
2. Download the CPU binary for your platform (e.g. `llama-*-bin-win-cpu-x64.zip`)
3. Extract the contents into the `tools/` directory at the repo root

### Configure the paths

Add to your `appsettings.Local.json`:

```json
{
  "Authentication": { ... },
  "Llm": {
    "ServerExePath": "tools/llama-server.exe",
    "ModelPath": "models/gemma-4-E2B-it-Q8_0.gguf"
  }
}
```

### Run

```bash
cloudscout scan
```

CloudScout auto-launches llama-server when Tier 3 is needed, waits for it to load the model, runs inference, and shuts the server down on exit. No separate terminal required.

## Additional Provider Setup (Google Drive / Dropbox)

Both flows take ~5–10 minutes and produce the values you paste into `appsettings.Local.json`.

### Google Drive

1. Open **[console.cloud.google.com](https://console.cloud.google.com)** and create (or select) a project
2. **APIs & Services > Library** > enable **Google Drive API**
3. **APIs & Services > OAuth consent screen** > **External** > fill in app name + support email > add the scope `https://www.googleapis.com/auth/drive.readonly` > add your own email as a **Test user**
4. **APIs & Services > Credentials > Create credentials > OAuth client ID** > Application type **Desktop app** > name "CloudScout"
5. Copy the **Client ID** and **Client secret** into `appsettings.Local.json`:

   ```json
   {
     "Authentication": {
       "Google": {
         "ClientId": "...apps.googleusercontent.com",
         "ClientSecret": "..."
       }
     }
   }
   ```

   > **Note:** Per Google's own docs, OAuth client secrets for installed/desktop apps are not truly confidential — they're identifiers paired with the client ID. They still belong in the gitignored local config, never in committed files.

6. Run `cloudscout connect googledrive` — your browser opens, you consent, control returns to the CLI.

### Dropbox

1. Open **[dropbox.com/developers/apps](https://www.dropbox.com/developers/apps)** and click **Create app**
2. **API:** _Scoped access_ · **Type:** _Full Dropbox_ (or _App folder_ for tighter scope) · **Name:** something globally unique like `CloudScout-<your-handle>`
3. On the app's **Permissions** tab, check `files.metadata.read` and `files.content.read`, then **Submit**
4. On the **Settings** tab, copy the **App key** (no secret needed — PKCE replaces it):

   ```json
   {
     "Authentication": {
       "Dropbox": {
         "AppKey": "..."
       }
     }
   }
   ```

5. Run `cloudscout connect dropbox` — your browser opens, you consent, control returns to the CLI.

## Limitations

CloudScout is designed for **personal cloud accounts**. Work, school, and other organisation-controlled accounts will likely fail to authenticate with an "admin approval required" message — Microsoft, Google, and Dropbox all enforce administrator consent for unverified third-party apps in enterprise tenants. This isn't a CloudScout bug; it's how the providers gate access. App "verification" with each provider is a multi-month commercial-publisher process and would not lift the restriction in tenants whose policy requires admin consent for _all_ third-party apps (which most do).

For the same reason **SharePoint is not supported** — it has no personal-account variant, so every SharePoint user is in some org's tenant. The OneDrive, Google Drive, and Dropbox providers are expected to work cleanly only against personal accounts (e.g. `outlook.com`, `gmail.com`, personal Dropbox).

## Custom Taxonomies

The default taxonomy (`generic-default`) ships as an embedded resource. To create your own, author a JSON file following this schema:

```json
{
  "name": "My Domain",
  "version": "1.0.0",
  "categories": [
    {
      "id": "legal.contracts",
      "displayName": "Contracts",
      "parentId": "legal",
      "filenameKeywords": ["contract", "agreement"],
      "folderKeywords": ["contracts", "legal"],
      "contentKeywords": ["hereinafter", "effective date"],
      "contentPhrases": ["this agreement is made"],
      "negativeKeywords": ["social contract"],
      "mimeTypes": ["application/pdf"],
      "baseConfidence": 0.85
    }
  ]
}
```

Pass it to the scan command:

```bash
cloudscout scan --taxonomy path/to/my-taxonomy.json
```

| Field              | Purpose                                                            |
| ------------------ | ------------------------------------------------------------------ |
| `filenameKeywords` | Case-insensitive substrings matched against the file name (Tier 0) |
| `folderKeywords`   | Matched against the parent folder path (Tier 0)                    |
| `contentKeywords`  | Word-boundary matched against extracted text (Tier 1)              |
| `contentPhrases`   | Multi-word phrases — higher weight than single keywords (Tier 1)   |
| `negativeKeywords` | If found anywhere, the category is excluded for that file          |
| `mimeTypes`        | Preferred MIME types — adds a small confidence bonus (Tier 0)      |
| `baseConfidence`   | Maximum confidence ceiling for this category (0.0 - 1.0)           |

The Tier 3 LLM also uses the taxonomy — category IDs and display names are injected into the prompt as the valid output enum.

## Project Structure

```text
CloudScout/
  src/
    CloudScout.Core/           Core engine library (no I/O framework dependency)
      Classification/          Tiered pipeline, extractors, classifiers
      Crawling/                ICloudStorageProvider + OneDrive / GoogleDrive / Dropbox implementations
      Inference/               LLM server manager + HTTP inference client
      Persistence/             EF Core + SQLite (entities, migrations)
      Services/                Scan orchestrator
      Taxonomy/                JSON taxonomy model, loader, embedded defaults
    CloudScout.Cli/            CLI entry point (System.CommandLine)
  tests/
    CloudScout.Core.Tests/     74 unit tests (taxonomy, tiers, extractors, delta, providers)
  models/                      (gitignored) GGUF model files
  tools/                       (gitignored) llama-server binary + DLLs
  cloudscout                   Shell wrapper (macOS/Linux)
  cloudscout.cmd               Batch wrapper (Windows)
```

## Tech Stack

| Component                          | Choice                                                 | License             |
| ---------------------------------- | ------------------------------------------------------ | ------------------- |
| Runtime                            | .NET 10 LTS                                            | MIT                 |
| OneDrive API + OAuth               | Microsoft.Graph SDK + Microsoft.Identity.Client (MSAL) | MIT                 |
| Google Drive API + OAuth           | Google.Apis.Drive.v3 + Google.Apis.Auth                | Apache 2.0          |
| Dropbox API + OAuth                | Dropbox.Api (PKCE flow)                                | MIT                 |
| PDF extraction                     | PdfPig                                                 | Apache 2.0          |
| Office extraction (docx/xlsx/pptx) | DocumentFormat.OpenXml                                 | MIT                 |
| OpenDocument / RTF extraction      | BCL (System.IO.Compression, custom parser)             | MIT                 |
| LLM model                          | Gemma 4 E2B                                            | Apache 2.0          |
| LLM server                         | llama-server (llama.cpp)                               | MIT                 |
| Local storage                      | SQLite via EF Core                                     | MIT / Public Domain |
| CLI framework                      | System.CommandLine                                     | MIT                 |

All dependencies are MIT or Apache 2.0 licensed. No proprietary, commercial, or restrictively licensed components.

## Troubleshooting

| Symptom                                                                                | Cause                                                                                    | Fix                                                                                                                                                                                                              |
| -------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Missing Authentication:Microsoft:ClientId`                                            | appsettings.Local.json not created or ClientId empty                                     | Copy the `.example` file and fill in your app registration ID                                                                                                                                                    |
| `AADSTS7000218` during login                                                           | Public client flows not enabled                                                          | App registration > Authentication > Allow public client flows > Yes                                                                                                                                              |
| `AADSTS65001` consent error                                                            | Missing Graph permissions                                                                | Add `Files.Read`, `User.Read`, `offline_access` delegated permissions                                                                                                                                            |
| `No cached MSAL account`                                                               | Token cache was cleared                                                                  | Re-run `cloudscout connect onedrive`                                                                                                                                                                             |
| Tier 3 skipped (no errors)                                                             | `Llm:ServerUrl` is empty or `Llm:ModelPath` not set                                      | Add the Llm section to appsettings.Local.json                                                                                                                                                                    |
| `llama-server executable not found`                                                    | Binary not in `tools/`                                                                   | Extract llama.cpp release into `tools/` directory                                                                                                                                                                |
| Tier 3 returns empty results                                                           | Model generates thinking tokens that exhaust the token budget                            | Increase `Llm:MaxGenerationTokens` (default 2048 should be sufficient)                                                                                                                                           |
| `Missing Authentication:Google:ClientId/ClientSecret`                                  | appsettings.Local.json missing the Google section                                        | Fill in both `ClientId` and `ClientSecret` from Google Cloud Console (see Additional Provider Setup)                                                                                                             |
| Google consent screen says "App not verified"                                          | OAuth consent screen is in test mode (expected)                                          | Click _Advanced_ > _Go to CloudScout (unsafe)_ — the warning is normal for unverified personal-use apps. Make sure your email is added as a Test user.                                                           |
| Google: `Error 403: access_denied` "has not completed the Google verification process" | Account isn't on the test-users allow-list while the app is in Testing publishing status | Google Cloud Console > **APIs & Services > OAuth consent screen** > **Test users** > **+ Add users** > add the Gmail you're connecting with > Save. Up to 100 testers allowed; no need to publish to Production. |
| Google: "access_denied" or "admin approval required"                                   | Trying to use a work/school Google account                                               | Use a personal Google account (gmail.com). See [Limitations](#limitations).                                                                                                                                      |
| `Missing Authentication:Dropbox:AppKey`                                                | appsettings.Local.json missing the Dropbox section                                       | Fill in `AppKey` from your Dropbox app's Settings tab                                                                                                                                                            |
| Dropbox: browser opens but redirect fails                                              | Loopback port range blocked / occupied                                                   | Adjust `Authentication:Dropbox:LoopbackPortStart`/`LoopbackPortEnd` in config to a free range                                                                                                                    |
| Dropbox: "missing scope" error during scan                                             | App permissions not submitted on Dropbox console                                         | Permissions tab > tick `files.metadata.read` + `files.content.read` > **Submit** > re-run `connect dropbox`                                                                                                      |

## Roadmap

- [x] Gemma 4 vision — feed scanned/image documents directly to the LLM
- [x] Scan delta — detect new/modified/unchanged files across runs and skip re-classification
- [x] Google Drive + Dropbox providers
- [ ] Export suggestions to CSV/JSON for import into other systems

## License

MIT — see [LICENSE](LICENSE).
