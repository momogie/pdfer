# Fase 6 — Paginasi & Fragmentasi Cerdas

**Ukuran:** L · **Prasyarat:** Fase 1 (box tree utuh)

## Tujuan

Kontrol page break selayaknya media cetak. [baseline-audit.md](baseline-audit.md): model
streaming sekarang hanya bisa "pindah halaman kalau lewat batas"
([LayoutEngine.cs:458-464](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L458-L464)) —
**tidak bisa memotong** satu box menjadi dua halaman. Fase ini menambah **fragmentasi** di
atas box tree.

## Checklist — Fragmentasi inti

- [ ] **Potong box antar halaman**: satu box bisa terbagi; padding/border/background
      diteruskan ke tiap fragmen sesuai spec fragmentasi CSS.
- [ ] Fragmentasi **line box** (jangan potong di tengah baris teks).
- [ ] Tinggi konten dihitung sebelum fragmentasi (dari Fase 1), bukan ditebak.

## Checklist — Kontrol break

- [ ] **`break-before/after/inside`** (`auto|avoid|page|column`) + legacy `page-break-*`
      (naikkan penanganan `always/avoid` yang sekarang parsial,
      [LayoutEngine.cs:245-262](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L245-L262),
      [:452-457](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L452-L457)).
- [ ] **`orphans` / `widows`** untuk paragraf (min. baris di awal/akhir halaman).
- [ ] `break-inside: avoid` untuk blok/gambar/baris tabel.

## Checklist — @page & margin boxes

- [ ] **`@page`** lengkap: `:first`, `:left`, `:right` (sekarang di-skip,
      [LayoutEngine.cs:73](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L73));
      `size`, margin per-halaman (sebagian sudah, [LayoutEngine.cs:81-111](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L81-L111)).
- [ ] **Margin boxes** `@top-center`/`@bottom-right`/dst untuk header/footer cetak.
- [ ] **Penomoran halaman**: `counter(page)`, `counter(pages)` dalam margin box.

## Checklist — Header/footer & navigasi

- [ ] Header/footer **berulang** dengan nomor, beda ganjil/genap & halaman pertama
      (struktur ada di [LayoutTypes.cs:146-153](../../src/PdfEr.Core/Domain/Layout/LayoutTypes.cs#L146-L153)).
- [ ] **Table of Contents** dengan nomor halaman + leader dots (setelah paginasi final diketahui).
- [ ] **`content` + counters**: `counter-reset/increment`, `::before/::after` (nyambung Fase 8),
      penomoran heading otomatis.
- [ ] **Bookmark/outline & link internal** menunjuk halaman yang benar setelah fragmentasi.

## Kriteria selesai

- Dokumen laporan panjang: sampul + TOC bernomor + header/footer ganjil/genap, **tanpa baris
  yatim**, box terpotong mulus antar halaman.
- Kategori `pagination-*` mencapai **SSIM ≥ 0.92** dan jumlah/urutan halaman cocok Chromium.
