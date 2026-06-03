# Ebook Manager

Native Windows desktop ebook library manager built with .NET 10, WPF, SQLite, CommunityToolkit.Mvvm, EF Core, and Syncfusion WPF DataGrid.

## Current Milestone

Milestone 1 is a functional vertical slice:

- portable local ebook libraries with `library.db`
- import pipeline with duplicate detection
- EPUB metadata and cover extraction
- CBZ cover extraction
- safe fallback import for recognized formats
- searchable library viewmodels
- editable metadata details with save, undo, and delete services
- WPF workspace with bookshelf, detailed grid, and list views
- light/dark theme infrastructure
- English and Dutch selectable culture support
- prepared fallback resource files for German, French, Spanish, and Italian

## Prerequisites

- Windows
- .NET SDK 10
- Visual Studio 2026 or another IDE that supports .NET 10 WPF
- Syncfusion WPF license

Set the Syncfusion key in your user environment before running the app:

```powershell
[Environment]::SetEnvironmentVariable("SYNCFUSION_LICENSE_KEY", "<your-key>", "User")
```

Alternatively, place the key in `docs/SynfusionLicense.txt` or `docs/SyncfusionLicense.txt` for local development. These files are ignored by Git. Do not commit a Syncfusion license key.

## Build And Test

```powershell
dotnet restore EbookManager.sln
dotnet test EbookManager.sln
dotnet build EbookManager.sln
```

Run the desktop app:

```powershell
dotnet run --project src/EbookManager.App/EbookManager.App.csproj
```

## Supported Import Formats

Milestone 1 recognizes:

- EPUB
- KEPUB
- PDF
- CBR
- CBZ
- MOBI
- AZW
- AZW3
- KFX

Metadata extraction is intentionally conservative:

- EPUB and KEPUB: OPF metadata and embedded cover where available
- EPUB and KEPUB: `dc:subject` tags and Calibre-style series metadata where available
- CBZ: first supported image as cover, filename fallback for title/author
- PDF, CBR, MOBI, AZW, AZW3, and KFX: safe import with filename fallback

SQLite is authoritative for metadata inside the application. Metadata edits are also written to a portable `metadata.json` sidecar file next to the managed book file. On import, a sibling `metadata.json` is preferred over embedded metadata so corrected metadata can travel with a copied book directory. Native write-back into ebook files remains a later opt-in feature per format, starting with EPUB only after representative safety tests.

## Manual Verification

Use [docs/manual-tests/milestone-1-checklist.md](docs/manual-tests/milestone-1-checklist.md) for the current manual test checklist.

## Milestone 2 Work

Milestone 2 extends the desktop app with:

- drag-and-drop import
- faceted sorting and filters in the left action list
- search-term highlighting
- refined import result summaries
- extra themes beyond light/dark
- portable `metadata.json` sidecar metadata

The following remain later-version candidates:

- active e-reader detection and USB sync
- native metadata write-back into ebook files
- custom metadata fields
- user-defined views
- ebook conversion
- full-text search inside book contents
- in-app bug reports and feature requests that can prepare or create GitHub issues after user review
