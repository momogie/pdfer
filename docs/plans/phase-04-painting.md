# Fase 4 — Painting / Fidelitas Visual

**Ukuran:** L · **Prasyarat:** Fase 1 (paint stage), Fase 2

## Tujuan

Elemen tampil seperti di browser — bukan kotak polos. [baseline-audit.md](baseline-audit.md)
menemukan `border-radius`/`box-shadow`/`background-image` hanya stub parsial di writer, dan
gradient/transform/opacity-grup/clipping belum ada.

## Model paint

Layout menghasilkan **display list** (perintah gambar berurutan sesuai stacking context),
lalu writer meng-emit operator PDF. Ini menggantikan writer yang membaca style ad-hoc
([PdfBlockContentWriter.cs](../../src/PdfEr.Infrastructure/PdfWriters/PdfBlockContentWriter.cs)).

## Checklist — Border

- [ ] Per-sisi **style**: `solid | dashed | dotted | double | groove/ridge` (minimal 4 pertama).
- [ ] Warna & lebar berbeda per sisi; join sudut benar.
- [ ] **`border-radius`** penuh (per-sudut, elips) — naikkan dari stub
      ([PdfTextWriter.cs:458](../../src/PdfEr.Infrastructure/PdfWriters/PdfTextWriter.cs#L458));
      termasuk **meng-clip background & konten** ke bentuk membulat.

## Checklist — Background

- [ ] `background-color` dengan alpha.
- [ ] **`background-image`** (naikkan dari stub [PdfBlockContentWriter.cs:148](../../src/PdfEr.Infrastructure/PdfWriters/PdfBlockContentWriter.cs#L148)):
      `background-size` (`cover|contain|<len>|%`), `background-position`, `background-repeat`.
- [ ] Multiple backgrounds (layering).
- [ ] **Gradient**: `linear-gradient` & `radial-gradient` → PDF shading (type 2/3),
      termasuk color stops & sudut.

## Checklist — Efek

- [ ] **`box-shadow`** (offset, blur, spread, inset) — naikkan dari stub
      ([PdfBlockContentWriter.cs:111](../../src/PdfEr.Infrastructure/PdfWriters/PdfBlockContentWriter.cs#L111)).
- [ ] **`text-shadow`**.
- [ ] **`opacity`** level-grup & alpha via **ExtGState** + soft mask.

## Checklist — Transform, clipping, stacking

- [ ] **`transform`**: `translate/scale/rotate/skew/matrix` → matriks PDF `cm`
      (dengan `transform-origin`).
- [ ] **`overflow: hidden`** sebagai **clip path** sungguhan (lebih baik dari potong-tinggi Fase 2);
      dukung juga dengan `border-radius`.
- [ ] **Stacking context & z-index**: urutan paint benar (background → border → konten →
      positioned children sesuai z-index).
- [ ] **`visibility: hidden`**, **`outline`**, **`object-fit`** untuk `<img>`.

## Kriteria selesai

- Kartu/komponen (radius + shadow + gradient + border campuran) ter-render mirip Chromium.
- Elemen bertumpuk & ter-clip tampil dengan urutan/pemotongan benar.
- Kategori `visual-*` mencapai **SSIM ≥ 0.90**.
