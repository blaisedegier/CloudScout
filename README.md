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

| Format family             | Extensions                                  | Library                                  |
| ------------------------- | ------------------------------------------- | ---------------------------------------- |
| PDF                       | `.pdf`                                      | PdfPig                                   |
| Microsoft Office (modern) | `.docx`, `.xlsx`, `.xlsm`, `.pptx`, `.pptm` | DocumentFormat.OpenXml                   |
| OpenDocument              | `.odt`, `.ods`, `.odp`                      | System.IO.Compression + System.Xml (BCL) |
| Rich Text                 | `.rtf`                                      | Custom parser (no dependency)            |
| Plain text                | `.txt`, `.csv`, `.md`, `.log`               | StreamReader                             |

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
- A Microsoft account with files in OneDrive
- Access to [portal.azure.com](https://portal.azure.com) (free tier is sufficient)
- **For Tier 3 (optional but recommended):** a Gemma 4 E2B GGUF model file + llama-server binary

## Quick Start

### 1. Clone and build

```bash
git clone https://github.com/YOUR-USERNAME/CloudScout.git
cd CloudScout
dotnet build
```

### 2. Register an Azure app (for OneDrive access)

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

Edit `appsettings.Local.json` and paste your Application (client) ID:

```json
{
  "Authentication": {
    "Microsoft": {
      "ClientId": "YOUR-APPLICATION-CLIENT-ID"
    }
  }
}
```

### 4. Connect your OneDrive

```bash
cloudscout connect onedrive
```

A device-code prompt will appear — open the URL in your browser, enter the code, and sign in. Control returns to the CLI once authentication completes.

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
      Crawling/                ICloudStorageProvider + OneDrive implementation
      Inference/               LLM server manager + HTTP inference client
      Persistence/             EF Core + SQLite (entities, migrations)
      Services/                Scan orchestrator
      Taxonomy/                JSON taxonomy model, loader, embedded defaults
    CloudScout.Cli/            CLI entry point (System.CommandLine)
  tests/
    CloudScout.Core.Tests/     58 unit tests (taxonomy, Tier 0, Tier 1, extractors)
  models/                      (gitignored) GGUF model files
  tools/                       (gitignored) llama-server binary + DLLs
  cloudscout                   Shell wrapper (macOS/Linux)
  cloudscout.cmd               Batch wrapper (Windows)
```

## Tech Stack

| Component                          | Choice                                     | License             |
| ---------------------------------- | ------------------------------------------ | ------------------- |
| Runtime                            | .NET 10 LTS                                | MIT                 |
| Cloud API                          | Microsoft.Graph SDK                        | MIT                 |
| OAuth                              | Microsoft.Identity.Client (MSAL)           | MIT                 |
| PDF extraction                     | PdfPig                                     | Apache 2.0          |
| Office extraction (docx/xlsx/pptx) | DocumentFormat.OpenXml                     | MIT                 |
| OpenDocument / RTF extraction      | BCL (System.IO.Compression, custom parser) | MIT                 |
| LLM model                          | Gemma 4 E2B                                | Apache 2.0          |
| LLM server                         | llama-server (llama.cpp)                   | MIT                 |
| Local storage                      | SQLite via EF Core                         | MIT / Public Domain |
| CLI framework                      | System.CommandLine                         | MIT                 |

All dependencies are MIT or Apache 2.0 licensed. No proprietary, commercial, or restrictively licensed components.

## Troubleshooting

| Symptom                                     | Cause                                                         | Fix                                                                    |
| ------------------------------------------- | ------------------------------------------------------------- | ---------------------------------------------------------------------- |
| `Missing Authentication:Microsoft:ClientId` | appsettings.Local.json not created or ClientId empty          | Copy the `.example` file and fill in your app registration ID          |
| `AADSTS7000218` during login                | Public client flows not enabled                               | App registration > Authentication > Allow public client flows > Yes    |
| `AADSTS65001` consent error                 | Missing Graph permissions                                     | Add `Files.Read`, `User.Read`, `offline_access` delegated permissions  |
| `No cached MSAL account`                    | Token cache was cleared                                       | Re-run `cloudscout connect onedrive`                                   |
| Tier 3 skipped (no errors)                  | `Llm:ServerUrl` is empty or `Llm:ModelPath` not set           | Add the Llm section to appsettings.Local.json                          |
| `llama-server executable not found`         | Binary not in `tools/`                                        | Extract llama.cpp release into `tools/` directory                      |
| Tier 3 returns empty results                | Model generates thinking tokens that exhaust the token budget | Increase `Llm:MaxGenerationTokens` (default 2048 should be sufficient) |

## Roadmap

- [ ] Google Drive provider (`ICloudStorageProvider` abstraction is already in place)
- [ ] `.doc` binary format extraction (NPOI or similar)
- [ ] Gemma 4 vision — feed scanned/image documents directly to the LLM
- [ ] Scan history comparison (detect new/changed files across runs)
- [ ] Export suggestions to CSV/JSON for import into other systems

## License

MIT — see [LICENSE](LICENSE).
