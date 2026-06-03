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

Do not commit a Syncfusion license key.

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
- CBZ: first supported image as cover, filename fallback for title/author
- PDF, CBR, MOBI, AZW, AZW3, and KFX: safe import with filename fallback

SQLite is authoritative for metadata. Format write-back currently reports adapter outcomes and remains unsupported until format-specific write-back is proven safe.

## Manual Verification

Use [docs/manual-tests/milestone-1-checklist.md](docs/manual-tests/milestone-1-checklist.md) for the current manual test checklist.

## Explicit Milestone 2 Candidates

The following are deliberately outside Milestone 1:

- drag-and-drop import
- faceted filters in the left action list
- search-term highlighting
- active e-reader detection and USB sync
- metadata write-back into ebook files
- extra themes beyond light/dark
- custom metadata fields
- user-defined views
- ebook conversion
- full-text search inside book contents
