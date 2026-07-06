# Rencana Browser-Grade PdfEr — Indeks

> **Status: PERENCANAAN.** Belum ada kode yang diubah. Dokumen-dokumen ini adalah
> rencana eksekusi bertahap untuk membuat output PDF PdfEr **≥ 95% mirip** dengan
> tampilan HTML yang sama di browser (Chromium).

## Definisi target: "≥ 95% mirip browser"

Diukur secara objektif, bukan perasaan (detail di [Fase 0 — Harness](phase-00-harness.md)):

- Render HTML uji dengan **headless Chromium → PDF** = *ground truth*.
- Render dengan **PdfEr → PDF**, rasterisasi kedua PDF ke gambar pada DPI sama.
- Hitung **SSIM / skor perseptual** per halaman. Target: **rata-rata SSIM ≥ 0.95**
  pada suite uji per kategori, tanpa satu kategori pun < 0.90.

## Cara membaca dokumen ini

1. Mulai dari [baseline-audit.md](baseline-audit.md) — kondisi **nyata** kode saat ini
   (dengan referensi file:baris), supaya tahu titik berangkat dan mengapa tiap fase perlu.
2. [roadmap.md](roadmap.md) — urutan fase, dependensi, dan estimasi ukuran.
3. File per fase — masing-masing berisi tujuan, checklist task konkret, file yang disentuh,
   dan kriteria "selesai" yang terukur.

## Daftar fase

| Fase | Judul | Fokus | Estimasi |
|------|-------|-------|----------|
| 0 | [Harness & Fidelity Testing](phase-00-harness.md) | Alat ukur kemiripan (dibangun **paling awal**) | M |
| 1 | [Pondasi: Box Tree & Multi-Pass](phase-01-foundation.md) | Ganti streaming layout → box tree | XL |
| 2 | [Block & Inline Formatting](phase-02-block-inline.md) | Margin collapse, box-sizing, line box | L |
| 3 | [Teks & Tipografi](phase-03-text.md) | Advance width asli, UAX#14, fallback glyph, justify | L |
| 4 | [Painting / Fidelitas Visual](phase-04-painting.md) | border-radius, gradient, shadow, transform | L |
| 5 | [Tabel](phase-05-tables.md) | Auto-layout, border-collapse, thead berulang | M |
| 6 | [Paginasi & Fragmentasi](phase-06-pagination.md) | Potong box antar halaman, break-*, @page | L |
| 7 | [Flexbox, Grid, Float, Positioning](phase-07-flex-grid.md) | Layout 2D modern (paling kompleks) | XL |
| 8 | [Cascade & CSS Values](phase-08-cascade.md) | calc(), unit, pseudo-elements, inherit/initial | M |
| 9 | [Aset, Warna & Output](phase-09-assets-output.md) | Gambar/alpha, warna, subsetting, PDF/A, link | M |

Ukuran: **S** ≈ hari, **M** ≈ 1–2 minggu, **L** ≈ 2–4 minggu, **XL** ≈ 1 bulan+
(perkiraan kasar untuk satu pengembang; angka untuk prioritisasi, bukan komitmen).

## Prinsip eksekusi

- **Fase 0 dulu**, walau minimal. Tanpa alat ukur, "95%" tidak bisa diklaim.
- **Fase 1 adalah gerbang.** Hampir semua fitur lain butuh box tree. Kerjakan di belakang
  feature flag, jalankan paralel dengan pipeline lama, bandingkan sampai setara/lebih baik.
- Setelah Fase 1, **Fase 3 (teks) memberi lompatan kemiripan terbesar** — lihat baseline audit:
  lebar teks saat ini sebagian dipalsukan (`SizePoints * 0.5`).
- Fase 7 (flex/grid) sengaja **belakangan** karena butuh formatting context matang dari Fase 1–2.
- Setiap fase harus **menaikkan skor SSIM** pada kategori terkait, dijaga oleh CI (Fase 0).

## Di luar scope (untuk sekarang)

JavaScript/DOM dinamis, CSS animations/transitions (ambil computed state), scroll/interaktivitas,
web font remote via jaringan. Lihat catatan di [roadmap.md](roadmap.md).
