# Fase 3 — Teks & Tipografi Setara Browser

**Ukuran:** L · **Prasyarat:** Fase 1, Fase 2 · **Lompatan kemiripan terbesar.**

## Tujuan

Teks adalah ~80% kesan "browser-grade". [baseline-audit.md](baseline-audit.md) menemukan
lebar teks **sebagian dipalsukan** (`SizePoints * 0.5f`) dan line-breaking hanya di spasi.
Fase ini membuat pengukuran & pemenggalan teks **akurat seperti browser**.

## Checklist — Pengukuran akurat (kerjakan pertama, dampak tertinggi)

- [ ] Hapus lebar palsu `SizePoints * 0.5f` di `MeasureRunWidth`
      ([TextLayoutEngine.cs:170-176](../../src/PdfEr.Infrastructure/Typography/TextLayoutEngine.cs#L170-L176));
      pakai **advance width asli** dari font (SixLabors.Fonts).
- [ ] Ganti estimasi kasar di layout: inline-block
      ([LayoutEngine.cs:272-273](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L272-L273))
      dan `LayoutInlineContent` ([LayoutEngine.cs:486](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L486))
      → pakai pengukur teks yang sama.
- [ ] Fallback advance width jangan pukul rata ke `'n'`
      ([TextLayoutEngine.cs:201-204](../../src/PdfEr.Infrastructure/Typography/TextLayoutEngine.cs#L201-L204));
      ambil advance glyph sebenarnya (termasuk `.notdef`).

## Checklist — Line breaking & shaping

- [ ] **Unicode UAX#14 line breaking**: break opportunity benar (setelah tanda hubung,
      sebelum/ sesudah CJK, dsb), bukan sekadar split spasi
      ([TextLayoutEngine.cs:69-91](../../src/PdfEr.Infrastructure/Typography/TextLayoutEngine.cs#L69-L91)).
- [ ] **Shaping**: kerning & ligatur (SixLabors.Fonts; pertimbangkan HarfBuzz jika perlu
      script kompleks). Advance per-glyph, bukan penjumlahan per-char naif.
- [ ] **Font fallback per-glyph**: glyph absent → cari di rantai fallback (CJK/emoji/simbol)
      alih-alih tofu.
- [ ] **`letter-spacing` & `word-spacing`** masuk ke pengukuran & penempatan.
- [ ] **`text-align: justify` presisi**: distribusi ruang antar-kata berdasarkan lebar asli
      (memperbaiki [PdfTextWriter.cs:177](../../src/PdfEr.Infrastructure/PdfWriters/PdfTextWriter.cs#L177),
      [:328](../../src/PdfEr.Infrastructure/PdfWriters/PdfTextWriter.cs#L328)).

## Checklist — Lanjutan (bisa iterasi berikutnya)

- [ ] **Hyphenation** (`hyphens: auto`, kamus per bahasa) — meningkatkan justify secara nyata.
- [ ] **Bidi/RTL** (UAX#9) + `direction` / `unicode-bidi` untuk Arab/Ibrani.
- [ ] **`font-variant`, `font-feature-settings`** (small-caps, angka tabular, dsb).

## Checklist — Output teks di PDF (agar bisa copy/search)

- [ ] Emit **`ToUnicode` CMap** yang benar → teks bisa di-*select/search/copy* dari PDF.
- [ ] **Font subsetting** saat embedding untuk memangkas ukuran file (nyambung Fase 9).

## Kriteria selesai

- Teks membungkus di titik yang **sama persis** dengan Chromium (font sama) pada suite `text-*`.
- Paragraf justified rata & rapi; multi-bahasa (termasuk minimal satu non-Latin) tanpa tofu.
- Teks bisa diseleksi & dicari di PDF viewer.
- Kategori `text-*` mencapai **SSIM ≥ 0.90**.
