# Milestone 3 Manual Test Checklist

Use this checklist for Calibre metadata import testing.

## Calibre-Style Import

- Create or select a test library.
- Scan a folder where each book directory contains one supported ebook file plus sibling `metadata.opf` and `cover.jpg`.
- Confirm imported title, authors, description, language, publisher, ISBN, tags, series, and series number match the OPF metadata.
- Confirm tags and series appear immediately in the left filters.
- Confirm search finds values imported from OPF metadata.
- Confirm bookshelf, detailed grid, list view, and details pane show the imported metadata.
- Confirm covers are shown from Calibre `cover.jpg` when available.

## Precedence

- Import a book with only embedded metadata and confirm existing behavior still works.
- Import a book with embedded metadata plus `metadata.opf` and confirm OPF text metadata wins.
- Import a book with `metadata.json` plus `metadata.opf` and confirm `metadata.json` wins.
- Confirm cover extraction still works for EPUB when text metadata comes from `metadata.opf`.

## Cleanup Rules

- Import `[Atlanta 01] - Triptiek.epub` without stronger series metadata and confirm title `Triptiek`, series `Atlanta`, and series number `1`.
- Import a book with author `Slaughter, Karin` and confirm the author is stored as `Karin Slaughter`.
- Import a book with a non-numeric OPF series index and confirm the series number remains empty.
- Right-click an author, series, tag, or language filter and confirm Rename updates all matching books and refreshes the filters.
- Right-click a tag or series filter and confirm Delete removes that metadata value from matching books after confirmation.

## Regression

- Drag and drop normal files without OPF sidecars.
- Add books through the toolbar.
- Scan with subdirectories disabled and enabled.
- Confirm duplicate and possible-duplicate results are still reported clearly.
