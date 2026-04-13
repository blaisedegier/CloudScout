# Setup — CloudScout CLI (Phase A)

Everything below runs on your local machine. The only external requirement is a **free** Microsoft Entra ID app registration so that Microsoft will allow your app to ask users for permission to read OneDrive.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.201 or newer)
- A Microsoft account (personal or work/school) with files in OneDrive
- Access to [portal.azure.com](https://portal.azure.com)

## Step 1 — Register an Entra ID application

1. Navigate to **portal.azure.com → Microsoft Entra ID → App registrations → New registration**.
2. Fill in:
   - **Name:** `CloudScout` (or whatever you like)
   - **Supported account types:** *Accounts in any organizational directory and personal Microsoft accounts*
   - **Redirect URI:** leave blank for now (the CLI uses device-code flow, which doesn't require a redirect URI)
3. Click **Register**.
4. On the overview page that opens, copy the **Application (client) ID** — you'll paste it into local config in Step 3.

## Step 2 — Enable public client flows

The CLI uses MSAL's **device-code flow**, which requires the app registration to allow public-client flows.

1. In the app registration, go to **Authentication**.
2. Scroll to **Advanced settings → Allow public client flows**.
3. Set it to **Yes** and click **Save**.

## Step 3 — Grant Microsoft Graph permissions

1. Go to **API permissions → Add a permission → Microsoft Graph → Delegated permissions**.
2. Add these three:
   - `Files.Read` — read the user's OneDrive files
   - `User.Read` — basic profile info (usually pre-granted)
   - `offline_access` — refresh tokens so scans can run without re-auth
3. You do **not** need to click "Grant admin consent" — users consent individually during the OAuth flow.

## Step 4 — Create local config

From the repo root:

```bash
cp src/CloudScout.Cli/appsettings.Local.json.example src/CloudScout.Cli/appsettings.Local.json
```

Open `src/CloudScout.Cli/appsettings.Local.json` and paste your **Application (client) ID** from Step 1 into the `ClientId` field. Leave `TenantId` as `"common"` unless you want to restrict to a specific tenant.

The file is `.gitignore`'d, so it will not be committed.

## Step 5 — Run the CLI

Build first (only needed once per code change):

```bash
dotnet build
```

### Connect an account

```bash
dotnet run --project src/CloudScout.Cli -- connect onedrive
```

You'll see output like:

```
Starting device-code flow for OneDrive...

To sign in, use a web browser to open the page https://microsoft.com/devicelogin
and enter the code ABCD1234 to authenticate.

Connected you@example.com (id: 7f1c...).
```

Open the URL, paste the code, sign in, and consent. Control returns to the CLI once authentication completes.

### Scan your OneDrive

```bash
dotnet run --project src/CloudScout.Cli -- scan
```

The crawler walks your drive breadth-first and prints progress:

```
Scanning OneDrive account you@example.com...
  Crawling: 1247 files discovered
Scan 3b2e... completed: 1247 files.
```

### Review discovered files

```bash
dotnet run --project src/CloudScout.Cli -- results
```

Prints files grouped by parent folder. In Phase A this is just raw discovery; Phase B adds classification and suggestions.

## Troubleshooting

- **`Missing Authentication:Microsoft:ClientId`** — you haven't created `appsettings.Local.json` or the ClientId is empty. Go back to Step 4.
- **`AADSTS7000218` during device-code login** — public client flows aren't enabled on the app registration. Go back to Step 2.
- **`AADSTS65001` consent errors** — you didn't grant the `Files.Read` delegated permission. Go back to Step 3.
- **`No cached MSAL account with HomeAccountId=...`** — the MSAL token cache was cleared (e.g. you deleted `%LocalAppData%\CloudScout\msal-cache.bin`). Re-run `connect`.

## What's next

Phase B adds the taxonomy loader and Tier 0 (metadata) + Tier 1 (text extraction + keyword) classification. Phase C adds the Gemma 4 E2B multimodal LLM via LlamaSharp for ambiguous cases.
