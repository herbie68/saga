# Ebook Manager Milestone 3: Calibre Metadata Import Design

## 1. Purpose

Milestone 3 turns the version `0.1` desktop app into a better real-library importer. The main goal is to make existing Calibre-managed collections and Calibre-style book folders useful in Ebook Manager without copying Calibre code and without modifying source files.

The milestone focuses on metadata quality because that immediately improves the library grid, bookshelf captions, details pane, search, and left-side filters for authors, series, tags, language, status, and e-reader state. It also keeps the work testable without requiring a physical e-reader.

## 2. User Outcomes

After this milestone a user can scan or import ebooks from folders that contain Calibre `metadata.opf` files and see richer metadata immediately:

- title
- authors
- description
- language
- publisher
- publication date where available
- tags
- series
- numeric series number
- ISBN
- cover where a normal ebook adapter can already extract it

When metadata is absent or incomplete, the existing embedded metadata and filename fallback behavior remains available.

## 3. Scope

Milestone 3 includes:

- Read Calibre `metadata.opf` sidecar files located next to an imported ebook file.
- Prefer the app's own `metadata.json` sidecar when present, then Calibre `metadata.opf`, then embedded metadata, then filename fallback.
- Use the same precedence for file picker import, drag-and-drop import, and directory scan.
- Make directory scan Calibre-friendly by associating an ebook file with a sibling `metadata.opf` in the same book folder.
- Parse common bracketed title patterns such as `[Atlanta 01] - Triptiek` into title, series, and numeric series number when no stronger metadata source already provides those values.
- Normalize simple comma-separated author names such as `Slaughter, Karin` to `Karin Slaughter` when the pattern is unambiguous.
- Keep series number numeric by accepting only values that parse to `decimal`; non-numeric values are ignored rather than stored as text.
- Improve import result messages where richer sidecar metadata affected the imported record or a duplicate was skipped.
- Add focused automated tests and a Milestone 3 manual test checklist.

Milestone 3 excludes:

- USB, Kobo, Kindle, or MTP e-reader detection.
- Copying books to e-readers.
- Native write-back into EPUB, MOBI, AZW, PDF, CBR, or CBZ files.
- Custom columns or user-defined views.
- Full-text search inside book contents.
- Direct GitHub issue creation from the app.

## 4. Metadata Precedence

The import pipeline keeps SQLite authoritative once a book is imported. During import, source metadata is resolved in this order:

1. `metadata.json` next to the source ebook file, because this is Ebook Manager's own portable correction format.
2. Calibre `metadata.opf` next to the source ebook file.
3. Format-specific embedded metadata, currently strongest for EPUB and KEPUB.
4. Filename fallback.

Precedence is field-level rather than all-or-nothing where practical. For example, if `metadata.opf` contains tags and series but no cover, the existing EPUB cover extraction can still supply the cover. The final merged `BookMetadata` is what gets stored in SQLite and later written to Ebook Manager's `metadata.json` sidecar inside the managed library book folder.

## 5. Calibre OPF Reader

A new infrastructure component reads Calibre OPF files as XML using namespace-tolerant element lookup. It must accept both namespaced and namespace-light OPF variants.

Supported OPF fields:

- `dc:title` for title.
- One or more `dc:creator` values for authors, preserving order where possible.
- `dc:description` for description.
- `dc:language` for language.
- `dc:publisher` for publisher.
- `dc:date` for publication date when it parses safely.
- `dc:identifier` values for ISBN, preferring identifiers whose scheme or value indicates ISBN.
- `dc:subject` values for tags.
- Calibre `meta` entries for `calibre:series` and `calibre:series_index`.

Malformed OPF files must not crash a batch import. They are ignored with a warning recorded in the import item when possible, and the pipeline continues with embedded metadata or filename fallback.

## 6. Title And Author Cleanup

Title cleanup is conservative. The pattern `[Series 01] - Title` is recognized when:

- the title starts with `[`;
- a matching `]` appears before the main title;
- the text after `]` starts with `-`, `:`, or whitespace;
- the bracket content ends with a numeric token;
- the remaining title is not empty.

The last numeric token becomes `SeriesNumber`. The preceding bracket text becomes `Series`. The remaining text becomes `Title`. Leading zeroes are discarded through numeric parsing, so `01` becomes `1`.

Author cleanup handles only the simple case `Lastname, Firstname` with one comma and non-empty parts. It does not try to interpret multi-author strings, suffixes, initials, particles, or cultural naming rules beyond this safe pattern. Ambiguous names are left unchanged.

Cleanup is applied after metadata is merged, but it must not overwrite explicit series or series number values already supplied by `metadata.json`, `metadata.opf`, or embedded EPUB Calibre metadata.

## 7. Import And Scan Behavior

File picker and drag-and-drop continue to accept one or more ebook files. For each source ebook, the pipeline looks in the source file's directory for:

- `metadata.json`
- `metadata.opf`

Directory scan continues to find supported ebook files. It does not import OPF files as books by themselves. If a Calibre book folder contains multiple ebook formats for the same title, the existing duplicate detection remains authoritative:

- same hash: skipped as exact duplicate;
- same normalized title and author: reported as possible duplicate;
- distinct title and author: imported as a separate book.

Automatic multi-format merging is deferred because the current app model supports multiple files per book, but the current user workflow has not yet introduced a merge/review screen.

## 8. User Interface

No new major UI surface is required. The existing import result window should make this milestone visible by showing clearer messages such as:

- metadata loaded from Ebook Manager sidecar;
- metadata loaded from Calibre OPF;
- Calibre OPF ignored because it could not be read;
- possible duplicate detected after OPF metadata was applied.

The details pane, filters, search, bookshelf, detailed grid, and list view should update through existing viewmodels because imported metadata lands in the same SQLite model as current metadata.

## 9. Error Handling And Privacy

Calibre OPF reading is local-only. The app does not contact Calibre, GitHub, or the internet.

The import batch must continue when a single OPF file is missing, malformed, unreadable, or incomplete. Error text should identify the metadata sidecar type and the reason in general terms, but should not expose full personal paths in future reportable diagnostics.

Unreadable ebook source files keep the existing failure behavior.

## 10. Testing Strategy

Automated tests cover:

- OPF reader maps title, authors, description, language, publisher, date, ISBN, tags, series, and series index.
- OPF reader tolerates namespaces and missing optional fields.
- Malformed OPF returns a warning and does not abort import.
- Import prefers `metadata.json` over `metadata.opf`.
- Import prefers `metadata.opf` over embedded EPUB metadata for overlapping fields.
- EPUB cover extraction can still supply cover bytes when OPF sidecar supplies text metadata.
- Directory scan associates sibling `metadata.opf` with the discovered ebook file.
- Bracketed title cleanup extracts title, series, and numeric series number.
- Non-numeric series numbers are ignored.
- Simple `Lastname, Firstname` author names normalize to `Firstname Lastname`.
- Ambiguous author names remain unchanged.

Manual tests cover:

- Scan a small Calibre-style folder with one book folder and one ebook.
- Scan a Calibre-style folder with multiple book folders.
- Confirm tags and series appear in the left filters immediately.
- Confirm edited metadata still writes Ebook Manager `metadata.json` sidecars after import.
- Confirm existing drag-and-drop and Add Books behavior still work for normal files without OPF sidecars.

## 11. Completion Criteria

Milestone 3 is complete when:

- automated tests pass;
- release build succeeds;
- a manual checklist exists under `docs/manual-tests/`;
- README documents Calibre `metadata.opf` support and the current metadata precedence;
- no Calibre source code is copied into the repository;
- the app remains a native Windows desktop app with no web server requirement.
