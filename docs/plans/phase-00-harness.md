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

- [x] Proyek `tests/PdfEr.FidelityTests` (xUnit, net10.0, referenced from `PdfEr.slnx`).
- [x] Ground truth via **Playwright Chromium** (`page.PdfAsync`), bukan shell manual ke
      Chrome/Edge — lebih stabil lintas mesin (auto-download browser via `playwright.ps1`).
- [x] Rasterizer PDF→PNG via **PDFtoImage** (wrapper PDFium) — dipilih setelah percobaan
      screenshot PDF lewat Chromium headless gagal (`net::ERR_ABORTED`, PDF viewer plugin
      tidak stabil di mode headless).
- [x] Implementasi metrik **SSIM** (windowed 8×8, stride 4, luminance-based) di
      `SsimCalculator.cs` — catatan: ini aproksimasi kasar, bukan SSIM referensi penuh;
      cukup untuk regression signal, perlu diperhalus jika dipakai sebagai gate ketat.
- [ ] **Determinisme font**: belum dipaksa. Kasus uji saat ini pakai `DejaVu Sans` yang
      kemungkinan fallback ke font sistem berbeda di Chromium vs PdfEr — risiko skor
      SSIM tinggi secara **kebetulan** (dokumen sederhana, sedikit teks). Perlu diverifikasi
      dengan kasus teks-berat sebelum dipercaya sebagai baseline.
- [x] Korpus awal (5 file, bukan 10–15 target): `text-simple`, `text-alignment`,
      `block-headings-lists`, `visual-borders-colors`, `table-basic`. **Belum ada**
      `flex-*`, `grid-*`, `pagination-*`, `cascade-*` — kategori itu skornya akan jelek
      begitu ditambahkan (fitur belum diimplementasi penuh, lihat baseline-audit.md).
- [x] Toleransi ukuran halaman (±10px) sebelum crop-to-common-size untuk redam rounding
      DPI antar-renderer.
- [ ] Runner baru cetak **ringkasan teks** (console + JSON `fidelity-report.json` +
      `fidelity-summary.txt`) — **belum ada galeri HTML diff berdampingan**.
- [ ] **Belum ada CI gate** (regression terhadap baseline tersimpan) — saat ini cuma
      dijalankan manual via `dotnet test`.
- [ ] Baseline resmi belum disimpan sebagai file terpisah untuk dibandingkan; angka di
      bawah adalah hasil run pertama, bukan baseline yang dikunci.

## Hasil run pertama (bukan baseline resmi, lihat catatan font di atas)

```
block: 1/1 passed, avg SSIM=0.9989
table: 1/1 passed, avg SSIM=0.9990
text:  2/2 passed, avg SSIM=0.9986
visual: 1/1 passed, avg SSIM=0.9981
```

Skor ini **tidak boleh dibaca sebagai "PdfEr sudah 99% browser-grade"** — kasus ujinya
sengaja sederhana (satu halaman, tanpa flex/grid/pagination) sehingga cocok dengan gap
arsitektural yang didokumentasikan di `baseline-audit.md`. Nilainya berguna sebagai
smoke-test bahwa harness bekerja, bukan sebagai bukti fidelity keseluruhan.

## Catatan implementasi

- Normalkan ukuran halaman & margin antara Chromium dan PdfEr agar adil.
- Toleransi anti-aliasing: bandingkan pada blur ringan / SSIM window, jangan pixel-exact.
- Buat perbandingan **cepat** (subset "smoke" untuk tiap PR, full nightly).

## Kriteria selesai

- Satu perintah menghasilkan skor SSIM per kategori + galeri diff.
- Baseline tersimpan; CI menolak PR yang menurunkan skor kategori mana pun.
- Angka baseline nyata menggantikan tebakan di [roadmap.md](roadmap.md).
