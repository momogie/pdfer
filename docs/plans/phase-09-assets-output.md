# Fase 9 — Aset, Warna & Kepatuhan Output

**Ukuran:** M · **Prasyarat:** sebagian independen; subsetting/link sinkron dengan Fase 3/6.

## Tujuan

Gambar, warna, dan PDF yang **benar secara teknis** dan efisien. Infrastruktur dasar sudah
ada (`ImageService`, `SvgRasterizer`, `FontRegistry`, embedding TTF, Flate); fase ini
memperkuat kualitas & kepatuhan.

## Checklist — Gambar

- [ ] Format: JPEG/PNG/GIF/WebP; verifikasi jalur di [ImageService.cs](../../src/PdfEr.Infrastructure/Services/ImageService.cs).
- [ ] **Alpha/transparansi** PNG via **SMask** PDF.
- [ ] **DPI-aware**: `srcset`/densitas, **downsample** ke DPI target agar tajam tapi tak boros.
- [ ] **`object-fit`** (`fill|contain|cover|none|scale-down`) untuk `<img>` (nyambung Fase 4).
- [ ] **SVG**: perkuat rasterisasi ([SvgRasterizer.cs](../../src/PdfEr.Infrastructure/Services/SvgRasterizer.cs));
      pertimbangkan jalur vektor untuk SVG sederhana (lebih tajam di zoom).

## Checklist — Warna

- [ ] **sRGB eksplisit**; `rgb()/rgba()/hsl()/hsla()`, named colors penuh, `currentColor`
      (perkuat [ColorParser.cs](../../src/PdfEr.Core/Application/HtmlProcessing/ColorParser.cs)).
- [ ] Alpha warna konsisten dengan ExtGState (Fase 4).
- [ ] Opsi **CMYK** untuk alur cetak (opsional, di belakang konfigurasi).

## Checklist — Kepatuhan & struktur PDF

- [ ] **Font subsetting** + `ToUnicode` CMap (koordinasi dengan Fase 3) — ukuran kecil + teks
      bisa di-copy/search.
- [ ] **Link** internal (anchor→halaman) & eksternal (URL); anchor sudah ada di model
      ([LayoutTypes.cs:84-85](../../src/PdfEr.Core/Domain/Layout/LayoutTypes.cs#L84-L85)).
- [ ] **Bookmark/outline** dari heading (struktur sudah disebut di README).
- [ ] **Metadata XMP** + info dictionary (judul/penulis/subjek).
- [ ] **Tagged PDF / PDF/UA** (aksesibilitas) — struktur tag dari box tree.
- [ ] **PDF/A** (embed ICC, font penuh/ subset benar, tanpa fitur terlarang) — di belakang opsi.

## Checklist — Optimasi

- [ ] **Object streams** & xref stream (kurangi overhead; hati-hati regresi xref —
      pernah jadi bug kritis di histori proyek).
- [ ] **Dedup** font & gambar identik; recompress gambar.

## Kriteria selesai

- Gambar tajam pada DPI target, transparansi benar; SVG rapi.
- Teks bisa dicari/di-copy; link & bookmark berfungsi di viewer.
- Jika PDF/A ditargetkan: **lolos validator** (mis. veraPDF).
- Ukuran file wajar dibanding output Chromium.
