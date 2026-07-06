# Fase 5 — Tabel Setara Browser

**Ukuran:** M · **Prasyarat:** Fase 1 (intrinsic sizing), Fase 2; sinkron dengan Fase 6.

## Tujuan

Tabel adalah kasus umum laporan. [baseline-audit.md](baseline-audit.md): `TableLayoutEngine`
sudah punya colspan/rowspan, tapi belum ada auto-layout lebar kolom, `border-collapse` benar,
dan **pengulangan `<thead>` saat pecah antar halaman**.

## Checklist — Algoritma lebar

- [ ] **Auto table layout**: hitung `min-content`/`max-content` tiap sel (pakai intrinsic
      sizing Fase 1), lalu distribusikan lebar kolom sesuai algoritma CSS auto.
- [ ] **Fixed table layout** (`table-layout: fixed`): lebar dari `<col>`/sel baris pertama.
- [ ] **`<colgroup>`/`<col>` width** dihormati ([ColgroupHandler.cs](../../src/PdfEr.Core/Application/HtmlProcessing/TagHandlers/ColgroupHandler.cs)).
- [ ] `table` `width` `%`/`auto` relatif containing block.

## Checklist — Border & spacing

- [ ] **`border-collapse: collapse`** dengan resolusi konflik border (aturan menang: lebar,
      style, warna). Default sheet sekarang `separate` ([CssMerger.cs:58](../../src/PdfEr.Core/Application/HtmlProcessing/CssMerger.cs#L58)).
- [ ] **`border-collapse: separate`** + `border-spacing`.
- [ ] `empty-cells`, `caption-side`.

## Checklist — Struktur & spanning

- [ ] **`colspan`/`rowspan`** diverifikasi ulang di layout baru (logika ada di
      [TableLayoutEngine.cs:184-261](../../src/PdfEr.Core/Application/HtmlProcessing/TableLayoutEngine.cs#L184-L261)).
- [ ] **`vertical-align`** sel (`top|middle|bottom|baseline`).
- [ ] Tinggi baris = maksimum tinggi sel; distribusi tinggi untuk rowspan.

## Checklist — Paginasi tabel (butuh Fase 6)

- [ ] **`<thead>` diulang** di tiap halaman saat tabel melewati batas halaman.
- [ ] **`<tfoot>`** di bawah tiap fragmen / akhir tabel sesuai spec.
- [ ] Pemecahan **antar baris** yang aman (hindari memotong satu baris; `break-inside: avoid`
      per baris bila memungkinkan).
- [ ] `caption` ikut di fragmen pertama.

## Kriteria selesai

- Tabel panjang multi-halaman dengan header berulang, colspan/rowspan, dan border-collapse
  ter-render rapi dan **cocok Chromium**.
- Lebar kolom auto mengikuti isi seperti browser.
- Kategori `table-*` mencapai **SSIM ≥ 0.92**.
