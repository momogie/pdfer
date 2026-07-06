# Fase 2 — Block & Inline Formatting yang Benar

**Ukuran:** L · **Prasyarat:** Fase 1

## Tujuan

Dokumen "normal flow" (heading, paragraf, list, blockquote, div bersarang) ter-render
dengan **spacing setara Chrome dalam beberapa px**. Ini menutup gap dari
[baseline-audit.md](baseline-audit.md): tak ada margin collapse, tak ada `box-sizing`,
tak ada line box sungguhan.

## Checklist — Block formatting

- [ ] **Margin collapsing**: sibling bersebelahan, parent↔first/last child, blok kosong.
      Ganti penambahan margin penuh yang sekarang
      ([LayoutEngine.cs:347](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L347),
      [:450](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L450)).
- [ ] **`box-sizing`** `content-box` (default) & `border-box`; terapkan konsisten ke width/height
      (sekarang width CSS dipakai mentah, [LayoutEngine.cs:161-167](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L161-L167)).
- [ ] **Width `auto` & `%`** dihitung dari containing block (bukan selalu lebar halaman).
- [ ] **Height `auto`** dari isi; `height: %` relatif containing block (bukan tinggi halaman,
      [LayoutEngine.cs:369-375](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L369-L375)).
- [ ] **`min/max-width/height`** diterapkan setelah resolusi ukuran (sebagian sudah ada,
      rapikan ke satu tempat).
- [ ] **Margin auto** untuk centering horizontal (`margin: 0 auto`).

## Checklist — Inline formatting (IFC)

- [ ] **Line box** sungguhan: bungkus beberapa inline-level box per baris (bukan satu
      `InlineBox` per blok, [LayoutEngine.cs:420-434](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L420-L434)).
- [ ] **Baseline alignment** & **`vertical-align`** (`baseline|sub|super|top|middle|bottom|<len>`)
      dihitung di layout, bukan tambalan di writer ([PdfTextWriter.cs:28](../../src/PdfEr.Infrastructure/PdfWriters/PdfTextWriter.cs#L28)).
- [ ] **`line-height`** sebagai jarak antar-baseline (leading atas/bawah), bukan tinggi box
      (memory [[pdfer-layout-units]] soal unit).
- [ ] **`inline-block`** ikut aliran baris dengan ukuran benar (ganti heuristik
      [LayoutEngine.cs:264-311](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L264-L311)).
- [ ] **`white-space`**: `normal | nowrap | pre | pre-wrap | pre-line` di layout (writer sudah
      menyentuh sebagian, [PdfTextWriter.cs:73](../../src/PdfEr.Infrastructure/PdfWriters/PdfTextWriter.cs#L73)).
- [ ] **`text-align`** termasuk `justify` (distribusi ruang antar-kata) & `text-indent`.
      Justify sekarang meleset karena lebar dipalsukan — perbaikan akurat di Fase 3.
- [ ] **`text-transform`, `text-decoration`** posisinya benar dalam line box.

## Checklist — Overflow dasar

- [ ] `overflow: visible | hidden` untuk clipping konten yang melampaui box (clip path Fase 4
      menyempurnakan; di sini minimal potong tinggi konten).

## Kriteria selesai

- Kategori uji `text-*` dan dokumen normal-flow campuran mencapai **SSIM ≥ 0.85** vs Chromium.
- Jarak vertikal antar-blok (dengan margin collapse) cocok Chrome dalam toleransi kecil.
- Paragraf multi-baris membungkus di titik yang sama dengan Chrome (untuk font yang sama).
