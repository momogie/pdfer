# PdfEr — HTML-to-PDF Converter

PdfEr is a **standalone C# library** that converts HTML to PDF, built from the ground up for .NET 10. No external dependencies for PDF generation — the entire PDF writer is custom-implemented.

## Quick Start

### CLI

```bash
dotnet run --project src/PdfEr.Cli -- input.html output.pdf
dotnet run --project src/PdfEr.Cli -- input.html output.pdf --format A4 --orientation landscape
```

### C# API

```csharp
using Microsoft.Extensions.DependencyInjection;
using PdfEr.Core.Application.Interfaces;
using PdfEr.Infrastructure;

var services = new ServiceCollection();
services.AddPdfEr(cfg =>
{
    cfg.PageFormat = PageFormat.A4;
    cfg.Orientation = PageOrientation.Portrait;
    cfg.MarginTop = 20f;
});
var sp = services.BuildServiceProvider();

var converter = sp.GetRequiredService<IPdfConverter>();
var pdf = converter.ConvertHtmlToPdf("<html><body><p>Hello PDF!</p></body></html>");
File.WriteAllBytes("output.pdf", pdf);

// Async
await converter.ConvertHtmlToPdfToFileAsync(html, "output.pdf");
```

## Installation

```bash
dotnet add reference src/PdfEr.Core src/PdfEr.Infrastructure
```

Requires .NET 10 SDK.

## CLI Reference

```
PdfEr.Cli <input.html> <output.pdf> [options]

Options:
  --format <A4|Letter|Legal|...>     Page format (default: A4)
  --orientation <portrait|landscape>  Page orientation
  --margin <mm>                       All margins (default: 15)
  --margin-top <mm>                   Top margin
  --margin-bottom <mm>                Bottom margin
  --margin-left <mm>                  Left margin
  --margin-right <mm>                 Right margin
  --dpi <n>                           Output DPI (default: 96)
  --compress <true|false>             Enable compression (default: true)
  --encrypt                           Enable encryption
  --user-password <pwd>               User password
  --owner-password <pwd>              Owner password
  --debug                             Enable debug logging
  --version                           Show version
```

### Supported Page Formats

`A0`–`A6`, `B0`–`B6`, `Letter`, `Legal`, `Ledger`, `Tabloid`, `Executive`

## Configuration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `PageFormat` | `PageFormat` | `A4` | Page size |
| `Orientation` | `PageOrientation` | `Portrait` | Page orientation |
| `MarginTop/Bottom/Left/Right` | `float` | `15` | Margins in mm |
| `MarginHeader/Footer` | `float` | `5` | Header/footer margin in mm |
| `DefaultFontFamily` | `string` | `"DejaVu Sans"` | Fallback font |
| `DefaultFontSize` | `float` | `10` | Base font size in pt |
| `Dpi` | `int` | `96` | Output resolution |
| `EnableCompression` | `bool` | `true` | Deflate stream compression |
| `PdfVersion` | `PdfVersion` | `V1_7` | PDF version target |
| `EnableEncryption` | `bool` | `false` | Enable PDF encryption |
| `UserPassword` | `string?` | `null` | User password |
| `OwnerPassword` | `string?` | `null` | Owner password |
| `FontDirectories` | `string[]` | `[]` | Extra font search paths |
| `TempDirectory` | `string` | `"./temp"` | Cache directory |

## Features

### Supported HTML Tags

**Block:** `div`, `p`, `h1`–`h6`, `ul`, `ol`, `li`, `blockquote`, `pre`, `hr`, `section`, `article`, `header`, `footer`, `nav`, `main`

**Inline:** `b`, `strong`, `i`, `em`, `u`, `ins`, `span`, `a`, `code`, `sub`, `sup`, `small`, `mark`, `del`, `q`, `cite`, `abbr`, `acronym`

**Special:** `br`, `hr`

### CSS Support

- Tag, class (`.`), ID (`#`) selectors
- Basic attribute and pseudo-class selectors
- `@font-face` (local TTF/OTF files)
- `@media print` / `@media screen`
- `@page` rules (basic)
- Shorthand expansion (`font`, `margin`, `padding`, `border`, `background`)
- Inheritance for font, color, text properties
- `!important` priority
- Inline `style=""` attribute

### Lists

- Unordered lists (`<ul>`) with bullet markers
- Ordered lists (`<ol>`) with numbering
- `start` attribute support
- List style types: `decimal`, `lower-alpha`, `upper-alpha`, `lower-roman`, `upper-roman`
- Nested list support

### Fonts

- TrueType (`.ttf`) and OpenType (`.otf`) font embedding
- Standard PDF 14 fonts (Helvetica, Times, Courier — Regular, Bold, Italic, BoldItalic)
- `@font-face` CSS rule support
- Font fallback chain (`sans-serif` → Helvetica/Arial/...)
- `font-family` resolution from CSS

### PDF Output

- PDF 1.7 compliant
- Deflate (FlateDecode) stream compression
- TrueType font embedding (full program)
- Pages tree with per-page resources
- UTF-16BE text encoding for Unicode
- Bookmarks/outlines structure
- AcroForm dictionary structure
- Encryption dictionary structure
- Metadata (title)

## Architecture

```
┌─────────────────────────────────────┐
│         PdfEr.Cli (CLI)             │
├─────────────────────────────────────┤
│      PdfEr.Infrastructure           │
│  ┌─────────┐ ┌──────────────────┐   │
│  │PdfWriter│ │PdfConverterService│   │
│  │(custom) │ │FontRegistry      │   │
│  │TextLayout│ │Tag Handlers (20+)│   │
│  └─────────┘ └──────────────────┘   │
├─────────────────────────────────────┤
│        PdfEr.Core                    │
│  ┌────────┐ ┌─────────┐ ┌────────┐  │
│  │HtmlParser│ │CssEngine│ │Layout  │  │
│  │(AngleSharp)│ │(custom)│ │Engine  │  │
│  └────────┘ └─────────┘ └────────┘  │
└─────────────────────────────────────┘
```

## Conversion Pipeline

1. **HTML Parsing** — AngleSharp produces DOM tree, extracts `<style>` and inline CSS
2. **CSS Processing** — Parse, merge (default → user → inline), normalize shorthands
3. **Tag Processing** — Dispatched to 20+ tag-specific handlers via `TagRegistry`
4. **Layout** — Block elements positioned vertically with margin collapsing, auto page breaks
5. **PDF Generation** — Custom PdfWriter produces content streams, font embedding, pages tree

## Project Structure

```
src/
├── PdfEr.Core/             Domain + Application
│   ├── Domain/             Value objects, enums, layout types
│   ├── Application/        HTML/CSS processing, layout engine, tag handlers
│   └── Application/Interfaces/    IPdfConverter, IFontRegistry, config
├── PdfEr.Infrastructure/   Implementation
│   ├── PdfWriters/         PdfWriter, PdfStreamWriter
│   ├── Typography/         FontRegistry, TextLayoutEngine
│   └── Caching/            FileCacheService
└── PdfEr.Cli/              CLI frontend

tests/
├── PdfEr.Core.Tests/       Unit tests (domain, CSS, layout, barcodes, tags)
├── PdfEr.Infrastructure.Tests/     Infrastructure tests
└── PdfEr.IntegrationTests/ Full conversion integration tests
```

## Requirements

- .NET 10 SDK
- Dependencies: AngleSharp, SixLabors.Fonts, SixLabors.ImageSharp, SkiaSharp

## License

MIT License — see [LICENSE](LICENSE).

---

*PdfEr is an independent clean-room implementation inspired by the functional behavior of mPDF PHP. No source code from mPDF was copied or referenced during development.*
