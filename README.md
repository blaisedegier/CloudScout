# CloudScout

A pluggable document classifier that recursively scans cloud storage (OneDrive; Google Drive planned) and suggests files matching a user-defined taxonomy, using a tiered classification pipeline that escalates from cheap heuristics to a self-hosted multimodal LLM only when necessary.

## Status

Under active development. V1 is CLI + minimal Blazor web UI targeting .NET 10.

## Architecture

Three-tier classification pipeline:

- **Tier 0 — Metadata heuristics.** Filename, extension, folder keywords. Free, milliseconds.
- **Tier 1 — Text extraction + keyword matching.** PdfPig, OpenXml. Milliseconds per file.
- **Tier 3 — Multimodal LLM.** Gemma 4 E2B via LlamaSharp, self-hosted, CPU-friendly. Seconds per file, only used when earlier tiers lack confidence.

Taxonomies are JSON documents. Ship with a generic default; bring your own for domain-specific use cases.

## Setup

See [`docs/setup.md`](docs/setup.md) for Microsoft Entra ID app registration and Gemma model download instructions.

## License

MIT — see [`LICENSE`](LICENSE).

Dependencies: Microsoft.Graph (MIT), PdfPig (Apache 2.0), LlamaSharp (MIT), Gemma 4 model (Apache 2.0).
