# Ebook Manager Desktop MVP Design

## 1. Purpose

Ebook Manager is a local-first ebook library application inspired by the useful core workflows of Calibre without copying its code or reproducing its full feature set. Calibre is used only as a functional reference for behavior, supported formats, metadata edge cases, and future e-reader integration patterns.

The application is a native Windows desktop app. It must not require a web server. The desktop MVP is intentionally small but functionally testable with real ebook files.

On June 2, 2026, the mobile client direction was deliberately dropped. A mobile app cannot directly access a library that exists only on the user's local Windows filesystem without adding a synchronization service or shared-storage architecture. That is outside the product goal. The application therefore targets Windows desktop with WPF and may use Windows-specific capabilities where they improve the experience.

## 2. Product Scope

Version `0.1` is delivered in two milestones.

### 2.1 Milestone 1: Vertical Slice

The first executable milestone supports:

- Creating, opening, and switching between multiple ebook libraries.
- Automatically reopening the last used library at application startup.
- Importing one or more ebook files through a file picker.
- Scanning a selected directory for ebook files, with an option to include subdirectories.
- Copying imported ebook files into a managed library while leaving source files unchanged.
- Extracting metadata and covers where possible, with a filename fallback.
- Storing library metadata in SQLite.
- Showing books in bookshelf, detailed grid, and coverless list views.
- Searching across available metadata fields.
- Selecting a book and editing its supported metadata.
- Saving metadata changes, undoing unsaved edits, and deleting a book.
- Selecting Dutch or English (US) as the application language.
- Selecting a light or dark theme.

The detailed grid includes these initial columns:

- Cover
- Title
- Author
- Reading status: unread, reading, or read
- E-reader status

The e-reader column displays a localized unavailable state in `0.1`. Active device integration is deferred to version `0.2`.

### 2.2 Milestone 2: Complete Version 0.1

The second milestone adds:

- Windows drag-and-drop import for one or more files.
- Faceted sorting and filtering in the left action panel.
- Search match highlighting in displayed metadata.
- A refined import result view.
- Sepia, blue, and red themes in addition to light and dark.
- Improved metadata extraction and safe portable metadata sidecars using representative test files.

### 2.3 Deferred Features

The following features are explicitly deferred:

- USB mass-storage, Kobo, Kindle, and MTP e-reader integration.
- Copying books to connected e-readers.
- Custom metadata columns.
- User-defined library views.
- A Calibre-wide set of legacy ebook formats.
- German, French, Spanish, and Italian translations beyond prepared resource infrastructure.
- Free versus paid product editions.
- In-app bug reports and feature requests that can create GitHub issues.

Version `0.2` should address e-reader support separately, with USB mass-storage and Kobo as the first practical priorities.

Version `0.3` or later should consider an opt-in support workflow for bug reports and feature requests. The app can expose actions such as `Report a bug`, `Request a feature`, and `Report issue` on unexpected error dialogs. When GitHub integration is configured, these actions may prepare or create a GitHub issue containing useful diagnostic context such as application version, operating system version, selected app language/theme, exception type, stack trace, and the current workflow that failed.

Privacy is mandatory for this workflow: the user must review the issue content before submission, and the app must not include ebook contents, personal library paths, full filenames, account identifiers, or library metadata unless the user explicitly chooses to include them. If direct GitHub issue creation is unavailable, the app should copy a prepared Markdown report to the clipboard or save it as a local text file.

## 3. Supported Ebook Formats

Version `0.1` recognizes:

- EPUB
- KEPUB
- PDF
- CBR
- CBZ
- MOBI
- AZW
- AZW3
- KFX

DRM-protected or unsupported files are not decrypted. The import result reports that the file could not be processed.

## 4. Technical Architecture

The application is a native `.NET 10` WPF solution using C# and XAML. Domain, application, infrastructure, and presentation logic remain separated from the WPF shell so they can be tested independently.

Syncfusion controls are used where they provide concrete value. In particular, `Syncfusion.SfGrid.WPF` provides `SfDataGrid` for the detailed grid view. The project uses `CommunityToolkit.Mvvm` for observable viewmodels and asynchronous commands.

The solution contains:

- `EbookManager.App`: WPF UI, XAML windows and views, dependency injection, localization resources, themes, and Windows dialog adapters.
- `EbookManager.Domain`: ebook entities, metadata value objects, enums, result types, and service interfaces. It has no UI or database dependency.
- `EbookManager.Application`: use cases for library management, import, scan, search, metadata editing, undo, and delete.
- `EbookManager.Infrastructure`: EF Core SQLite persistence, filesystem operations, hashing, and metadata adapter implementations.
- `EbookManager.Tests`: unit and integration tests.

The implementation is clean-room code. No Calibre source code is copied into the project.

## 5. Library Storage

Each library is independent, portable, and represented by a directory selected by the user. A newly created library defaults to the name `ELibrary`, but the user can choose another name and location.

Example:

```text
My Library/
  library.db
  books/
    <book-id>/
      book.epub
      cover.jpg
      metadata.json
```

A book can have one or more managed files when different formats are associated with the same book record. All imported files are copied into the selected library. Original source files remain untouched.

SQLite is authoritative for the active library. Each managed book directory also stores a portable `metadata.json` sidecar containing editable metadata such as title, authors, description, tags, series, series number, language, publisher, publication date, and ISBN. The sidecar exists so corrected metadata can travel with a copied book directory and be reused during import into another library without modifying the ebook file itself.

Global application settings are stored in the platform-local application data directory rather than inside a library. They include:

- Known libraries with name and path
- Last opened library
- Selected language
- Selected theme
- Default library view
- Delete confirmation behavior

## 6. Data Model

Each library SQLite database contains at least:

### 6.1 `Books`

- `Id`
- `Title`
- `Description`
- `Language`
- `Publisher`
- `PublicationDate`
- `Series`
- `SeriesNumber`
- `Isbn`
- `ReadingStatus`
- `CoverRelativePath`
- `CreatedUtc`
- `UpdatedUtc`

### 6.2 `Authors` and `BookAuthors`

Supports multiple authors per book while retaining author ordering.

### 6.3 `Tags` and `BookTags`

Supports multiple categories or tags per book.

### 6.4 `BookFiles`

- `Id`
- `BookId`
- `Format`
- `RelativePath`
- `Sha256`
- `SizeBytes`
- `MetadataWriteBackStatus`
- `MetadataWriteBackMessage`

### 6.5 `ImportRuns` and `ImportItems`

Captures batch-level and file-level import results so the UI can show added, skipped, possible duplicate, and failed files after drag-and-drop or scan operations.

The schema is designed to allow later extension for custom fields and additional identifiers without implementing those features in `0.1`.

## 7. Import Pipeline

File picker import, directory scan, and future drag-and-drop all call the same application service.

For each input file:

1. Validate that the extension is recognized and the file is readable.
2. Compute a SHA-256 hash.
3. Skip the file automatically when the same hash already exists in the current library.
4. Read metadata and cover data through a format-specific metadata adapter.
5. Prefer a sibling `metadata.json` sidecar over embedded metadata when present and valid.
6. Fall back to filename-derived title and author data when embedded metadata is unavailable.
7. Report a possible duplicate when normalized title and author match an existing book but the file hash differs.
8. Copy the managed file into `books/<book-id>/`.
9. Store metadata, cover path, and file details in SQLite.
10. Add the outcome to the import result.

Possible duplicates are reported for human review but are not imported automatically in `0.1`. A future workflow can allow the user to merge or import them deliberately.

Import is transactional per book. Failure to process one input file does not abort the batch. Files that were copied for a failed import are cleaned up where possible and otherwise reported as cleanup problems.

Directory scan takes a starting directory and a boolean option controlling whether subdirectories are included.

## 8. Metadata Editing

The details panel shows and edits:

- Cover
- Title
- Authors
- Description
- Language
- Publisher
- Publication date
- Categories or tags
- Series
- Series number
- ISBN
- Reading status

The editor works on a viewmodel copy of the selected record. Any changed value causes a visible unsaved-changes notice to appear at the top of the details panel.

- `Save` persists the changes to SQLite, writes a portable `metadata.json` sidecar next to the managed book file, then attempts native metadata write-back only for formats where the adapter supports reliable write-back.
- `Undo` restores the values loaded at the start of the current edit session.
- `Delete` removes the book from the database and deletes its managed `books/<book-id>/` directory.

SQLite is authoritative. The sidecar file is the safe portable write-back layer. Native metadata write-back into ebook files is best effort, optional per format, and must not roll back saved SQLite changes. Unsupported native write-back is represented explicitly rather than treated as a failure.

## 9. User Interface

The desktop screen uses the approved four-region layout:

1. An application toolbar with actions such as add books, scan folder, create library, and settings.
2. A secondary bar with the three view selectors and an all-fields search box.
3. A main workspace with:
   - A left sorting and faceted filtering panel.
   - A center library view.
   - A right editable details panel.
4. A status bar with application assembly version and displayed book count.

The detailed view uses Syncfusion `SfDataGrid`. The bookshelf view shows covers with a two-line title and author caption. The list view shows the detailed-view columns without the cover.

Toolbar and action-panel actions use SVG icons with localized tooltips. The visual direction is a fresh, restrained desktop UI with clear hierarchy rather than a direct visual copy of Calibre.

The approved wireframe is stored only as temporary local design material under `.superpowers/`, which is excluded from Git.

## 10. Localization and Themes

All user-facing strings are stored in resource files from the start.

Version `0.1` ships complete:

- Dutch
- English (US)

Resource infrastructure is prepared for:

- German
- French
- Spanish
- Italian

Milestone 1 ships light and dark themes. Milestone 2 adds sepia, blue, and red themes. Syncfusion theme resources and application-level resource dictionaries are applied together for a consistent result.

## 11. Delete Confirmation

Deleting a book removes its database record and managed files by default.

The initial confirmation dialog includes a localized remember-answer option. When the user confirms deletion and selects remember-answer, later delete actions proceed without another prompt. Settings include a way to restore confirmation prompts.

If one or more managed files cannot be deleted, the user receives a clear cleanup warning. The failure must remain discoverable so it can be resolved later.

## 12. Error Handling

Expected file-level failures are reported in import results rather than crashing or aborting a batch:

- Unsupported extension
- Unreadable file
- DRM-protected or otherwise unprocessable file
- Metadata extraction failure
- Managed-copy failure
- Database persistence failure
- Metadata write-back failure
- Cleanup failure

Metadata extraction failure does not necessarily block import: when the file remains readable, the application falls back to filename-derived metadata and a default cover.

A later support workflow can add a `Report issue` action to unexpected error dialogs. That workflow is deferred beyond version `0.1` and must remain opt-in, reviewable, and privacy-preserving.

## 13. Testing Strategy

Automated tests focus on business behavior and real persistence boundaries.

### 13.1 Unit Tests

- Supported extension validation
- Hash calculation
- Exact duplicate detection
- Possible duplicate detection by normalized title and author
- Filename metadata fallback
- Search filtering
- Viewmodel dirty tracking and undo
- Delete confirmation setting behavior

### 13.2 Integration Tests

- Create, open, switch, and reopen libraries
- EF Core SQLite schema creation and migrations
- Import managed copies into temporary library directories
- Scan with and without subdirectories
- Import batch isolation when one file fails
- Save metadata to SQLite and record adapter write-back outcomes
- Remove database records and managed files

### 13.3 Manual Windows Checks

- Run the native Windows WPF app with real ebooks.
- Validate the three library views.
- Confirm language and theme switching.
- Confirm file picker import, scan feedback, details editing, and deletion.
- Confirm Syncfusion grid behavior with a realistically sized library.

Small representative ebook fixtures are stored only where their licenses permit redistribution.

## 14. Implementation Boundaries

The first implementation plan must cover Milestone 1 only. Milestone 2 receives a follow-up plan after the vertical slice has been executed and tested.

The application may define extension interfaces for later e-reader support, custom fields, and additional formats, but it must not implement those deferred features during Milestone 1.
