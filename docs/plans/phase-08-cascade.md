# Fase 8 — Cascade & CSS Values Lengkap

**Ukuran:** M · **Prasyarat:** Fase 1 (untuk pseudo-element via box generation) ·
Kerjakan **bertahap sepanjang** fase lain begitu ada kebutuhan.

## Tujuan

Tutup sisa lubang mesin CSS. Cascade inti sudah matang (memory [[pdfer-css-cascade]]:
specificity per-selector, kombinator turunan, `var()`, ekspansi shorthand). Yang kurang:
fungsi nilai (`calc()`), unit viewport, kata kunci global, dan selektor/pseudo lanjutan.

## Checklist — Unit & fungsi nilai

- [ ] **Satukan konversi unit** ke `IUnitConverter` — hentikan tiga tabel unit terpisah
      (`ParseLength` [LayoutEngine.cs:509-530](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L509-L530),
      `ParseCssLength` [:532-546](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L532-L546),
      `GetFontSizePt` [:728-754](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L728-L754)).
      Pertahankan aturan: layout **mm**, `.Tf` **pt** ([[pdfer-layout-units]]).
- [ ] **`calc()`**, **`min()` / `max()` / `clamp()`** (dengan campuran unit & `%`).
- [ ] Unit **`vw/vh/vmin/vmax/ch/ex`**; `rem` konsisten terhadap root font-size.
- [ ] `em` relatif font-size **elemen induk** yang benar (bukan konstanta 10px seperti sekarang,
      [LayoutEngine.cs:524-525](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L524-L525)).

## Checklist — Nilai global & inheritance

- [ ] **`initial` / `inherit` / `unset` / `revert`** eksplisit per properti.
- [ ] Daftar properti inherited vs non-inherited yang benar (sebagian ada di
      [CssMerger.cs:19-28](../../src/PdfEr.Core/Application/HtmlProcessing/CssMerger.cs#L19-L28)); lengkapi.
- [ ] `currentColor`, `color-mix()` (opsional).

## Checklist — Selektor & pseudo

- [ ] **Pseudo-element `::before` / `::after`** dengan `content` (butuh box generation Fase 1;
      `content: counter(...)` nyambung Fase 6).
- [ ] **`:nth-child()` / `:nth-of-type()`**, `:first/last/only-child`, `:not()`, `:is()`/`:where()`.
- [ ] Attribute selector penuh (`[attr^=]`, `[attr*=]`, dst).

## Checklist — At-rules & ketahanan

- [ ] **`@media`** evaluasi query benar untuk konteks cetak (print/screen sudah ada; lengkapi
      `width`, `orientation`, dsb).
- [ ] **`@supports`** (evaluasi aman; minimal jangan buang blok yang didukung).
- [ ] **Error recovery** parser: CSS rusak tidak membuat engine menyerah (tahan banting seperti browser).

## Kriteria selesai

- Stylesheet dunia-nyata (mis. output framework CSS) tidak membuat engine gagal total.
- Nilai computed cocok Chromium pada properti yang didukung (spot-check numerik).
- `::before/::after` & `:nth-child` yang umum ter-render benar.
