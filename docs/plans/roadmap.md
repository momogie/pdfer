# Roadmap — Urutan, Dependensi, Estimasi

## Diagram dependensi

```
Fase 0 (Harness)  ─── prasyarat pengukuran, bangun minimal DULU ───┐
                                                                    │
Fase 1 (Box Tree + Multi-Pass)  ─── GERBANG: hampir semua butuh ini ┤
   │                                                                │
   ├─► Fase 2 (Block & Inline)                                      │
   │       │                                                        │
   │       ├─► Fase 3 (Teks)   ◄── lompatan kemiripan terbesar      │
   │       ├─► Fase 4 (Painting)                                    │
   │       ├─► Fase 5 (Tabel)                                       │
   │       └─► Fase 7 (Flex/Grid)  ◄── paling kompleks, paling akhir │
   │                                                                │
   └─► Fase 6 (Paginasi/Fragmentasi)  ◄── butuh box tree utuh       │
                                                                    │
Fase 8 (Cascade/Values)  ─── menyebar, kerjakan bertahap sepanjang ─┤
Fase 9 (Aset/Output)     ─── sebagian independen, bisa disisipkan ──┘
```

## Urutan eksekusi yang disarankan

1. **Fase 0 (minimal)** — cukup: 10–15 HTML uji + skrip render Chromium + diff SSIM. Baseline skor.
2. **Fase 1** — box tree di belakang flag; paritas visual dengan pipeline lama sebelum lanjut.
3. **Fase 2** — formatting normal-flow benar (margin collapse, box-sizing, line box).
4. **Fase 3** — pengukuran teks asli + line-breaking. *Di sini skor SSIM naik paling tajam.*
5. **Fase 5 (Tabel)** & **Fase 4 (Painting)** — bisa paralel; keduanya butuh Fase 1–2.
6. **Fase 6 (Paginasi)** — fragmentasi antar halaman untuk dokumen panjang.
7. **Fase 7 (Flex/Grid)** — terakhir dari layout; paling kompleks.
8. **Fase 8 & 9** — dikerjakan menyisip sepanjang jalan begitu ada kebutuhan nyata.

## Kenapa urutan ini

- **Fase 1 wajib pertama** setelah harness: [baseline-audit.md](baseline-audit.md) menunjukkan
  streaming layout memblokir margin collapse, tinggi `%`, fragmentasi, flex/grid multi-pass.
- **Fase 3 sebelum Fase 4/7**: teks yang lebarnya benar adalah fondasi semua ukuran box
  (shrink-to-fit, auto table width, flex basis). Memperbaiki teks lebih dulu membuat fase
  lain lebih akurat "gratis".
- **Fase 7 terakhir**: flex/grid butuh intrinsic sizing (min/max-content) dari Fase 1 dan
  line box dari Fase 2 agar anak berukuran benar.

## Estimasi ukuran (untuk prioritisasi, bukan komitmen)

| Fase | Ukuran | Catatan risiko |
|------|--------|----------------|
| 0 Harness | M | Perlu Chromium headless di CI; deterministik-kan font. |
| 1 Pondasi | XL | Refactor terbesar; jalankan di belakang flag, banding paralel. |
| 2 Block/Inline | L | Margin collapse & IFC banyak edge case. |
| 3 Teks | L | Shaping/UAX#14 rumit; bisa bertahap (advance width dulu, UAX#14 lalu shaping). |
| 4 Painting | L | Gradient & soft-mask butuh operator PDF lanjutan. |
| 5 Tabel | M | Auto-layout & thead-berulang butuh Fase 1 + Fase 6. |
| 6 Paginasi | L | Fragmentasi box adalah inti; break-* & @page turunannya. |
| 7 Flex/Grid | XL | Spec paling besar; auto-placement grid mahal. |
| 8 Cascade | M | Kebanyakan aditif; `calc()` & pseudo-element paling berbobot. |
| 9 Aset/Output | M | Subsetting + PDF/A butuh ketelitian format PDF. |

Ukuran: **S** ≈ hari, **M** ≈ 1–2 minggu, **L** ≈ 2–4 minggu, **XL** ≈ 1 bulan+ (satu pengembang).

## Milestone kemiripan (target SSIM kumulatif)

| Setelah fase | Ekspektasi rata-rata SSIM | Kategori yang "browser-grade" |
|--------------|---------------------------|-------------------------------|
| 0 (baseline) | ~0.60–0.75 (ukur dulu) | — (ini titik berangkat) |
| 1 + 2 | ~0.80 | Dokumen teks normal-flow, heading, list |
| 3 | ~0.88 | + Paragraf justified, multi-bahasa, spacing teks presisi |
| 4 + 5 | ~0.92 | + Kartu/komponen visual, tabel |
| 6 | ~0.94 | + Dokumen panjang multi-halaman |
| 7 | **≥ 0.95** | + Layout flex/grid → target tercapai menyeluruh |

Angka SSIM di atas adalah **target pemandu**, bukan janji; nilai riil dikalibrasi setelah
Fase 0 memberi baseline nyata.

## Di luar scope (sekarang)

- **JavaScript / DOM dinamis** — HTML→PDF umumnya statis.
- **CSS animations/transitions** — ambil *computed state* akhir saja.
- **Scroll & interaktivitas**, form input interaktif (di luar AcroForm dasar).
- **Web font remote via jaringan** — bisa ditambah nanti; bukan inti kemiripan layout.

Jika salah satu ternyata dibutuhkan, angkat jadi fase tersendiri, jangan selundupkan ke fase lain.
