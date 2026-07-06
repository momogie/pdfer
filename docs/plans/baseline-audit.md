# Baseline Audit — Kondisi Nyata Kode (per 2026-07-06)

Ini hasil membaca kode, bukan asumsi. Setiap temuan menunjuk file:baris agar bisa diverifikasi.
Tujuannya: menjelaskan **mengapa** tiap fase dibutuhkan dan di mana titik berangkatnya.

## Ringkasan: model layout saat ini

Pipeline sekarang: DOM (AngleSharp) → resolve CSS → tag handler memanggil `LayoutEngine`
yang menempatkan blok **satu-arah dari atas ke bawah** memakai kursor global `_currentY`
([LayoutEngine.cs:18-27](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L18-L27)).

Ini **single-pass streaming layout**. Konsekuensinya (semua terverifikasi di kode):

- Tidak ada **box tree** yang bisa di-query dua arah; box langsung didorong ke
  `_currentPage.Blocks` begitu ditemui ([LayoutEngine.cs:471](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L471)).
- Tinggi blok yang belum diketahui ditebak dari `fontSize * 1.3f`
  ([LayoutEngine.cs:366-367](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L366-L367)),
  bukan dihitung dari isi.
- `height: %` diukur relatif tinggi **halaman**, bukan containing block
  ([LayoutEngine.cs:369-375](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L369-L375)).
- Page break hanya cek "apakah `_currentY` lewat batas" lalu pindah halaman
  ([LayoutEngine.cs:458-464](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L458-L464)) —
  **tidak bisa memotong** satu box menjadi dua halaman.

**→ Alasan Fase 1.** Semua di atas mustahil diperbaiki tanpa box tree + multi-pass.

## Temuan per area

### Teks & tipografi — **prioritas tertinggi setelah pondasi**

- **Lebar teks dipalsukan di jalur alignment.** `MeasureRunWidth` menghitung lebar
  sebagai `SizePoints * 0.5f` per karakter — konstanta, bukan advance width font
  ([TextLayoutEngine.cs:170-176](../../src/PdfEr.Infrastructure/Typography/TextLayoutEngine.cs#L170-L176)).
  Center/right/justify jadi meleset.
- **Fallback lebar karakter** ke huruf `'n'` bila glyph tak ada di tabel advance
  ([TextLayoutEngine.cs:201-204](../../src/PdfEr.Infrastructure/Typography/TextLayoutEngine.cs#L201-L204)).
- **Line-breaking hanya di spasi/tab** ([TextLayoutEngine.cs:69-91](../../src/PdfEr.Infrastructure/Typography/TextLayoutEngine.cs#L69-L91)) —
  tidak ada Unicode UAX#14 (tak ada break setelah tanda hubung, CJK, dsb), tak ada hyphenation.
- **Tak ada kerning/ligatur/shaping**; lebar = jumlah advance per-char.
- **Tak ada bidi/RTL**, tak ada `letter-spacing`/`word-spacing` di pengukuran.
- Estimasi lebar di beberapa tempat lain juga kasar: inline-block `textLen * fontSize * 0.5f`
  ([LayoutEngine.cs:272-273](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L272-L273)),
  `LayoutInlineContent` `text.Length * fontSize * 0.5f`
  ([LayoutEngine.cs:486](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L486)).

**→ Alasan Fase 3.**

### Block & inline formatting

- **Tak ada margin collapsing.** Margin atas/bawah selalu ditambahkan penuh
  ([LayoutEngine.cs:347](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L347),
  [:450](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L450)) — di browser margin
  bersebelahan meng-*collapse*. Spacing vertikal akan berbeda dari Chrome.
- **`box-sizing` tidak ditangani** — width CSS dipakai apa adanya sebagai `box.Width`
  ([LayoutEngine.cs:161-167](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L161-L167)),
  padding/border tidak diperhitungkan ke arah dalam/luar sesuai `box-sizing`.
- **Tidak ada line box / IFC sungguhan.** Teks satu blok jadi satu `InlineBox`
  ([LayoutEngine.cs:420-434](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L420-L434));
  `vertical-align`, baseline, dan campuran inline-block dalam satu baris tidak dimodelkan
  di layout (hanya ada penanganan `vertical-align` terbatas di writer,
  [PdfTextWriter.cs:28](../../src/PdfEr.Infrastructure/PdfWriters/PdfTextWriter.cs#L28)).

**→ Alasan Fase 2.**

### Flexbox & Grid — parsial

- Properti flex/grid **diparse** ([LayoutEngine.cs:202-224](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L202-L224))
  dan ada penempatan sederhana ([LayoutEngine.cs:548-589](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L548-L589)),
  tapi:
  - **Tidak ada `flex-grow/shrink/basis`** — anak tidak melar/menyusut.
  - `justify-content`/`align-items` **disimpan tapi tidak diterapkan** ke posisi anak
    ([LayoutEngine.cs:207-208](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L207-L208)).
  - Grid: `grid-template-columns` ditangani (termasuk `fr`, `repeat`, `minmax` kasar,
    [LayoutEngine.cs:615-698](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L615-L698)),
    tapi **tak ada `grid-template-rows`, area, auto-placement, row spanning**.
  - Single-pass: ukuran anak tak diukur dulu sebelum dibagi ruang.

**→ Alasan Fase 7 (dan mengapa ditaruh belakangan — butuh Fase 1/2).**

### Fidelitas visual (painting)

- **Stub parsial** di writer: `box-shadow` ([PdfBlockContentWriter.cs:111](../../src/PdfEr.Infrastructure/PdfWriters/PdfBlockContentWriter.cs#L111)),
  `background-image` ([:148](../../src/PdfEr.Infrastructure/PdfWriters/PdfBlockContentWriter.cs#L148)),
  `border-radius` ([PdfTextWriter.cs:458](../../src/PdfEr.Infrastructure/PdfWriters/PdfTextWriter.cs#L458)).
- **Belum ada**: gradient (linear/radial), `background-size/position/repeat`,
  `transform`, `text-shadow`, `opacity` grup via ExtGState, `overflow` clipping, z-index stacking.

**→ Alasan Fase 4.**

### Tabel

- `TableLayoutEngine` ada (288 baris) dengan **colspan/rowspan**
  ([TableLayoutEngine.cs:184-261](../../src/PdfEr.Core/Application/HtmlProcessing/TableLayoutEngine.cs#L184-L261)).
- **Belum ada**: auto-layout lebar kolom dari min/max-content isi, `border-collapse`
  yang benar (default sheet memakai `separate`, [CssMerger.cs:58](../../src/PdfEr.Core/Application/HtmlProcessing/CssMerger.cs#L58)),
  **pengulangan `<thead>`/`<tfoot>` saat tabel pecah antar halaman**, pemecahan baris aman.

**→ Alasan Fase 5.**

### Cascade & CSS values

- Sudah cukup matang (lihat memory [[pdfer-css-cascade]]): specificity per-selector,
  kombinator turunan, `var()`, ekspansi shorthand.
- **Belum ada**: `calc()`/`min()`/`max()`/`clamp()`, unit `vw/vh/ch/ex`,
  `initial/inherit/unset/revert` eksplisit, pseudo-element `::before/::after` dengan `content`,
  `:nth-child()` dan selector lanjutan lain.
- Konversi unit **tersebar & tak konsisten**: `ParseLength` vs `ParseCssLength` vs
  `GetFontSizePt` masing-masing punya tabel unit sendiri
  ([LayoutEngine.cs:509-546](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L509-L546),
  [:723-754](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L723-L754)).
  Catatan penting unit ada di memory [[pdfer-layout-units]] (layout mm, `.Tf` pt).

**→ Alasan Fase 8.**

### Aset & output

- Ada `ImageService`, `SvgRasterizer`, `FontRegistry`, embedding TTF, kompresi Flate.
- **Perlu dicek/diperkuat**: alpha PNG (SMask), `object-fit`, **font subsetting** +
  `ToUnicode` CMap (agar teks bisa di-copy/search), warna sRGB eksplisit, PDF/A, link internal.

**→ Alasan Fase 9.**

## Kesimpulan audit

Klaim awal "flex/grid parsial" **terkonfirmasi** dan lebih luas dari dugaan: masalah
terdalam ada di **model streaming layout** (Fase 1) dan **pengukuran teks yang sebagian
dipalsukan** (Fase 3). Dua hal ini paling menentukan seberapa jauh output dari browser.
Fitur visual & tabel sudah punya kerangka, tinggal dinaikkan kualitasnya.
