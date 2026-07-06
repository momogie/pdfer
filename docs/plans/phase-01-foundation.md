# Fase 1 — Pondasi: Box Tree & Layout Multi-Pass

**Ukuran:** XL · **Prasyarat:** Fase 0 · **GERBANG untuk hampir semua fase lain.**

## Tujuan

Ganti *single-pass streaming layout* (`_currentY` global,
[baseline-audit.md](baseline-audit.md)) dengan pipeline **box tree + multi-pass** yang bisa
di-query dua arah. Ini prasyarat margin collapsing, tinggi `%`, tabel auto-layout,
flex/grid sejati, dan fragmentasi antar halaman.

## Arsitektur target

Pisahkan menjadi tahap eksplisit, masing-masing dengan tipe data sendiri:

```
DOM (AngleSharp)
  → StyleResolution   : pohon ComputedStyle per elemen (nilai absolut sejauh mungkin)
  → BoxGeneration     : LayoutBox tree (block / inline / anonymous boxes)
  → Layout (2 pass)   : (a) intrinsic sizing bottom-up (min/max-content)
                        (b) placement top-down (isi containing block / fragment)
  → Fragmentation     : potong box tree jadi halaman (Fase 6 memperluas ini)
  → Paint             : display list → PDF writer meng-emit
```

Konsep first-class yang diperkenalkan (mengganti kursor global):

- **Containing block** — referensi ukuran untuk `%`, absolute positioning.
- **Formatting context** — BFC (block) & IFC (inline) sebagai objek, bukan variabel global
  `_currentInlineX/_currentInlineY` ([LayoutEngine.cs:18-20](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L18-L20)).

## Checklist

- [ ] Tipe `LayoutBox` baru (block box tree) + `LineBox`/`InlineLevelBox` untuk IFC,
      terpisah dari `BlockBox`/`InlineBox` domain lama ([LayoutTypes.cs](../../src/PdfEr.Core/Domain/Layout/LayoutTypes.cs)).
- [ ] **Box generation**: walk DOM → box tree, sisipkan **anonymous box** saat block & inline
      bercampur (aturan CSS box generation).
- [ ] Pisahkan **ComputedStyle** sebagai tahap sendiri (nilai sudah diresolusi), pisah dari
      `CreateBlock` yang sekarang menggabung resolve+layout ([LayoutEngine.cs:145-191](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L145-L191)).
- [ ] **Pass 1 — intrinsic sizing**: hitung `min-content`/`max-content` width bottom-up
      (dibutuhkan shrink-to-fit, tabel, flex-basis). Ganti tebakan `textLen * fontSize * 0.5f`.
- [ ] **Pass 2 — placement**: tempatkan anak dalam containing block; tinggi dihitung dari isi,
      bukan `fontSize * 1.3f` ([LayoutEngine.cs:366-367](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L366-L367)).
- [ ] Objek **ContainingBlock** & **BlockFormattingContext/InlineFormattingContext**.
- [ ] Adapter **paint**: box tree → `BlockBox`/`InlineBox` lama supaya PDF writer belum berubah
      (kurangi blast radius; writer dirombak di Fase 4).
- [ ] **Feature flag** `UseBoxTreeLayout`; pipeline lama tetap ada sampai paritas tercapai.
- [ ] Satukan konversi unit di `IUnitConverter` (lihat Fase 8); hentikan `ParseLength` lokal
      yang tersebar ([LayoutEngine.cs:509-546](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L509-L546)).
      Pertahankan aturan unit: layout **mm**, `.Tf` **pt** (memory [[pdfer-layout-units]]).

## Migrasi tag handler

- Tag handler ([TagHandlers/](../../src/PdfEr.Core/Application/HtmlProcessing/TagHandlers/)) berhenti
  memanggil API streaming (`LayoutBlock`/`AdvanceY`); sebagai gantinya membangun node box tree.
- Kerjakan bertahap: satu keluarga tag (mis. block generik) dulu di belakang flag, uji paritas
  SSIM, baru lanjut.

## Risiko & mitigasi

- **Refactor terbesar di seluruh plan.** Mitigasi: flag + banding paralel halaman-demi-halaman
  vs pipeline lama (Fase 0), hapus pipeline lama hanya setelah semua kategori setara/lebih baik.
- Performa multi-pass lebih mahal — pasang benchmark sejak awal (Fase 0).

## Kriteria selesai

- Semua kasus uji existing lolos lewat pipeline box tree dengan skor SSIM **≥** pipeline lama.
- Margin collapse, tinggi dari isi, dan tinggi `%` relatif containing block **sudah mungkin**
  (diaktifkan penuh di Fase 2).
- Pipeline lama bisa dinonaktifkan tanpa regresi.
