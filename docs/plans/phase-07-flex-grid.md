# Fase 7 — Flexbox, Grid, Float, Positioning

**Ukuran:** XL · **Prasyarat:** Fase 1, Fase 2, Fase 3 · **Paling kompleks — kerjakan terakhir.**

## Tujuan

Layout dua dimensi modern — area engine paling lemah. [baseline-audit.md](baseline-audit.md):
flex/grid **diparse tapi diterapkan parsial** (tak ada `flex-grow/shrink/basis`,
`justify-content`/`align-items` disimpan tapi tak dipakai). Butuh intrinsic sizing (Fase 1)
dan line box (Fase 2) agar anak berukuran benar — itu sebabnya fase ini terakhir.

## Checklist — Flexbox (spec penuh)

- [ ] Main/cross axis dari `flex-direction` (`row|row-reverse|column|column-reverse`).
- [ ] **`flex-grow` / `flex-shrink` / `flex-basis`** (shorthand `flex`) — algoritma resolusi
      ruang fleksibel. Ini yang **hilang** sekarang
      ([LayoutEngine.cs:548-589](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L548-L589)).
- [ ] **`justify-content`** (main axis) & **`align-items`/`align-self`** (cross axis) benar-benar
      diterapkan (sekarang hanya disimpan, [LayoutEngine.cs:207-208](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L207-L208)).
- [ ] **`flex-wrap`** + **`align-content`** untuk multi-baris.
- [ ] **`order`**, `gap`/`row-gap`/`column-gap`.
- [ ] `min-content` sebagai batas shrink (dari Fase 1).

## Checklist — CSS Grid (spec inti)

- [ ] **`grid-template-columns` & `grid-template-rows`** (`fr`, `repeat()`, `minmax()`, `auto`,
      `min/max-content`). Kolom sudah sebagian ([LayoutEngine.cs:615-698](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L615-L698));
      **rows belum ada**.
- [ ] **Auto-placement** item ke sel; `grid-auto-flow`.
- [ ] **`grid-column`/`grid-row` span** & penempatan eksplisit; `grid-template-areas`.
- [ ] `gap`, alignment (`justify-items/content`, `align-items/content`, `*-self`).

## Checklist — Float & clear

- [ ] **Float** yang benar: teks **mengalir mengelilingi** float (bukan pendekatan blok
      seperti sekarang, [LayoutEngine.cs:350-364](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L350-L364)).
- [ ] **`clear: left|right|both`**; float mempengaruhi tinggi BFC (clearfix).

## Checklist — Positioning

- [ ] **`relative`** (offset dari posisi normal) — perbaiki penerapan
      ([LayoutEngine.cs:443-448](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L443-L448)).
- [ ] **`absolute`** relatif **containing block terdekat yang positioned** (bukan selalu
      content box halaman seperti sekarang, [LayoutEngine.cs:407-418](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L407-L418)).
- [ ] **`fixed`** (ulang tiap halaman), **`sticky`** (berguna untuk header).
- [ ] `top/right/bottom/left` + interaksi dengan `z-index` (stacking, nyambung Fase 4).

## Kriteria selesai

- Layout dashboard/kartu berbasis flex & grid ter-render benar & konsisten antar halaman,
  cocok Chromium.
- Teks mengalir mengelilingi float; elemen absolute/fixed di posisi yang tepat.
- Kategori `flex-*` dan `grid-*` mencapai **SSIM ≥ 0.95** → **target keseluruhan tercapai**.
