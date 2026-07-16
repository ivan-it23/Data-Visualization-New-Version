# Template contract: Отчёт по практике

- Reference: `C:\Users\Admin\Desktop\Отчёт по практике\Отчёт по практике.docx`
- SHA-256: `EBFA2DC1CA1B3B31907830BAFD8286D4B708A8DFC9B842570C141E1D79EBF808`
- Source page pattern: one intentionally blank A4 title page followed by a manual page break and an empty paragraph on page 2.
- Sections: 1.
- Rendering: the standard DOCX renderer could not be used because LibreOffice is not installed. Page count is inferred from the explicit page break and document structure.

## Page system

- A4 portrait: 210 x 297 mm.
- Margins: left 30 mm, right 15 mm, top 20 mm, bottom 20 mm.
- Header/footer distance: 12.5 mm.
- Preserve the first page visually blank. Do not place text, images, headers, or a visible page number on it.
- Add report content after the existing page break.
- Page numbering counts the title page but is shown only from page 2, centered in the footer.

## Typography and formatting

- The reference contains no visible typography system or content styles; its page geometry is authoritative.
- Apply the teacher's requirements to added content: Times New Roman 14 pt, 1.5 line spacing, first-line indent 12.5 mm, justified body text.
- Heading level 1: Times New Roman 16 pt, bold, centered, decimal numbering where applicable.
- Heading level 2: Times New Roman 14 pt, bold, left aligned, decimal numbering.
- Captions: Times New Roman 12 pt, centered, single spacing.
- Table text: Times New Roman 12 pt with explicit cell padding and widths.

## Editable slots and flow

- Preserve `word/document.xml` first paragraph and its page-break run semantically unchanged.
- The empty paragraph following the page break is the start of page 2 and may be replaced with the report body.
- Add: contents, introduction, numbered chapters/subchapters, conclusion, source list, and appendix.
- Add the three supplied interface screenshots as inline images with numbered captions.

## Package preservation

- Preserve the source file unchanged at its original path until the final working copy passes structural checks.
- Preserve page size and margins.
- Theme, font table, web settings, and existing style definitions may be retained; new styles, numbering, image relationships, and footer parts may be added.
- The document has no existing headers, footers, images, tables, comments, content controls, fields, bookmarks, or custom XML.

## Fidelity gates

- The first page remains blank.
- The report begins on page 2.
- A4 geometry and margins remain unchanged.
- Visible page number is absent on page 1 and centered thereafter.
- All three images have captions and fit within the text area.
- Final report is at least 10 pages and follows a consistent style.
