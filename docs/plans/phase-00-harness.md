# Fase 0 — Harness & Fidelity Testing

**Ukuran:** M · **Prasyarat:** — · **Bangun paling awal (versi minimal).**

## Tujuan

Tanpa alat ukur, klaim "≥ 95% mirip browser" tak terbukti. Fase ini membangun cara
**mengukur kemiripan secara objektif** dan menjadikannya gerbang CI untuk semua fase lain.

## Rancangan

1. **Ground truth** = headless Chromium mencetak HTML uji → PDF (`--print-to-pdf`).
2. **Kandidat** = PdfEr mencetak HTML yang sama → PDF.
3. Rasterisasi kedua PDF ke PNG pada DPI sama (mis. 150), lalu bandingkan **per halaman**
   dengan **SSIM** + selisih perseptual. Simpan gambar diff untuk inspeksi mata.
4. Laporkan skor per file & per **kategori** (teks, tabel, flex, dst).

## Checklist

- [ ] Proyek `tests/PdfEr.FidelityTests` (atau skrip di `tests/fidelity/`).
- [ ] Skrip render Chromium headless (deteksi Chrome/Edge lokal atau Playwright) → PDF ground truth.
- [ ] Rasterizer PDF→PNG (pakai SkiaSharp/PDFium yang sudah ada, atau `pdftoppm`).
- [ ] Implementasi metrik **SSIM** + skor perseptual sederhana; hasil per halaman.
- [ ] **Determinisme font**: paksa set font yang sama untuk Chromium & PdfEr (bundel
      DejaVu/Noto) agar perbedaan bukan karena font berbeda.
- [ ] Korpus uji awal per kategori di `tests/fidelity/cases/` (mulai 10–15 file):
      `text-*`, `table-*`, `flex-*`, `grid-*`, `visual-*`, `pagination-*`, `cascade-*`.
- [ ] Runner menghasilkan **laporan skor** (JSON + HTML galeri diff berdampingan).
- [ ] Ambang CI: gagal bila skor kategori turun dari baseline tersimpan (regression gate).
- [ ] Simpan **baseline awal** skor apa adanya (titik berangkat, lihat milestone di roadmap).

## Catatan implementasi

- Normalkan ukuran halaman & margin antara Chromium dan PdfEr agar adil.
- Toleransi anti-aliasing: bandingkan pada blur ringan / SSIM window, jangan pixel-exact.
- Buat perbandingan **cepat** (subset "smoke" untuk tiap PR, full nightly).

## Kriteria selesai

- Satu perintah menghasilkan skor SSIM per kategori + galeri diff.
- Baseline tersimpan; CI menolak PR yang menurunkan skor kategori mana pun.
- Angka baseline nyata menggantikan tebakan di [roadmap.md](roadmap.md).
