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

- [x] Tipe `LayoutBox` baru (block box tree) di
      [BoxTree.cs](../../src/PdfEr.Core/Domain/Layout/BoxTree.cs), terpisah dari
      `BlockBox`/`InlineBox` domain lama ([LayoutTypes.cs](../../src/PdfEr.Core/Domain/Layout/LayoutTypes.cs)
      tidak disentuh). **Belum ada** `LineBox`/`InlineLevelBox` khusus untuk IFC — baru
      `LayoutBoxKind.Inline` sebagai penanda, belum ada struktur baris sungguhan.
- [x] **Box generation**: `BoxTreeBuilder` di
      [BoxTreeBuilder.cs](../../src/PdfEr.Core/Application/HtmlProcessing/BoxTreeBuilder.cs)
      walks DOM → `LayoutBox` tree, menyisipkan **anonymous block box** saat children
      block-level & inline-level bercampur (CSS 2.1 §9.2.1.1), sesuai `display:none`
      dan skip `<style>/<script>/<noscript>` seperti `PdfConverterService.WalkDom`.
      **Belum lengkap**: anonymous-wrapping hanya diterapkan untuk kontainer `Block`
      biasa — inline container, table row/cell, dan flex/grid container punya aturan
      children masing-masing yang belum diimplementasikan (dilewati apa adanya di slice
      ini). Belum ada dispatch ke `ITagHandler` (builder tidak tahu soal list counter,
      tabel, dsb — itu tanggung jawab Pass 1/2 & migrasi tag handler nanti).
      **Efek samping yang ditemukan & diperbaiki**: default UA stylesheet di
      `CssMerger.LoadDefaultStyles()` tidak pernah men-declare `display:block` untuk
      `p`/`h1-h6`/`blockquote`/`pre`/`hr`/`section`/`article`/`header`/`footer`/`nav`/`main`
      — pipeline streaming tidak masalah karena `LayoutEngine.LayoutBlock` memakai
      allowlist nama tag, bukan computed `display`. Box generation yang baru justru
      *harus* baca `display` asli, jadi menambahkan deklarasi itu ke stylesheet default
      adalah perbaikan kebenaran (bukan cuma demi test), diverifikasi tidak mengubah
      output SSIM harness (skor identik sebelum/sesudah).
- [x] **ComputedStyle** sebagai tipe terpisah di
      [ComputedStyle.cs](../../src/PdfEr.Core/Domain/Styles/ComputedStyle.cs) — membungkus
      `CssDeclarationBlock` hasil cascade + pre-resolve font-size (pt & mm, cocok dengan
      `LayoutEngine.GetFontSizePt`), `display`, `position`, `float`. **Catatan jujur**: ini
      *belum* memisahkan `CreateBlock`'s resolve+layout tergabung di
      [LayoutEngine.cs:145-191](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L145-L191) —
      `ComputedStyle` ada sebagai tipe siap pakai, tapi belum dipanggil dari pipeline manapun.
      Juga belum meng-cover semua properti (width/height/margin/padding % masih perlu
      containing block, sengaja belum diresolusi di sini).
- [x] **Pass 1 — intrinsic sizing**: `IntrinsicSizeCalculator` di
      [IntrinsicSizeCalculator.cs](../../src/PdfEr.Core/Application/HtmlProcessing/IntrinsicSizeCalculator.cs)
      menghitung `min-content`/`max-content` width bottom-up pakai `IFontRegistry.GetMetrics`
      (advance width nyata per glyph), bukan tebakan `textLen * fontSize * 0.5f`. Min-content
      = kata terpanjang tak-terpisah (split whitespace — aproksimasi UAX#14 penuh, itu
      pekerjaan Fase 3); max-content = seluruh teks dalam satu baris. Container block
      = max lebar antar-child; container inline/anonymous = max-content dijumlah
      (children mengalir di baris sama), min-content tetap kata terlebar. Padding+border
      ikut ditambahkan (margin sengaja tidak, konsisten dengan definisi shrink-to-fit
      terhadap border box). **Catatan jujur**: fallback tanpa font metrics masih pakai
      heuristik lama (`* 0.5f`) — hanya dipakai kalau font benar-benar tidak terdaftar,
      bukan jalur utama. **Belum dikonsumsi** LayoutEngine sama sekali — tebakan
      `textLen * fontSize * 0.5f` di LayoutEngine.cs masih jalan apa adanya sampai
      Pass 2 (placement) menggantikan pemanggil-pemanggilnya.
- [ ] **Pass 2 — placement**: tipe `BoxGeometry` sudah ada, **belum ada placement pass**.
      `fontSize * 1.3f` di [LayoutEngine.cs:366-367](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L366-L367)
      masih jalan seperti sebelumnya.
- [x] Objek **ContainingBlock** (struct, `Width`/`Height`/`HeightIsDefinite`) dan enum
      **FormattingContextKind** (Block/Inline/Flex/Grid/Table) sebagai penanda — keduanya
      tipe data saja, **belum dipakai** untuk resolusi `%` atau pemisahan BFC/IFC nyata.
- [ ] Adapter **paint**: box tree → `BlockBox`/`InlineBox` lama. Belum dikerjakan — tidak
      relevan sampai ada Pass 1/2 yang menghasilkan geometri box tree untuk diadaptasi.
- [x] **Feature flag** `UseBoxTreeLayout` ditambahkan di `PdfConverterConfiguration`
      (default `false`). **Belum ada percabangan** yang membacanya di `PdfConverterService`
      atau `LayoutEngine` — flag ada tapi belum "hidup".
- [ ] Satukan konversi unit di `IUnitConverter`; `ParseLength`/`ParseCssLength` lokal di
      [LayoutEngine.cs:509-546](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L509-L546)
      masih tersebar seperti semula. `ComputedStyle.ResolveFontSizePt` sengaja
      **menduplikasi** (bukan memanggil) `LayoutEngine.GetFontSizePt` untuk menghindari
      coupling dua arah antar proyek Domain/Application saat ini — perlu disatukan saat
      `IUnitConverter` dibereskan.

## Progres nyata (2 sesi — bukan Fase 1 selesai, lihat status per item di atas)

**Sesi 1**: tipe data box-tree tambahan (`LayoutBox`, `ComputedStyle`, dll.), tanpa
mengubah pipeline streaming. Ditemukan & diperbaiki bug race-condition di `CssMerger`
(cache statis `CssParser` dimutasi bersama lintas test paralel) selagi mengaudit alur
`ResolveStyles` — lihat commit terpisah.

**Sesi 2**: `BoxTreeBuilder` — box generation DOM → `LayoutBox` tree sungguhan, termasuk
anonymous block wrapping. Menemukan & memperbaiki gap default-stylesheet (`display:block`
hilang untuk beberapa tag block-level) yang tidak kelihatan di pipeline lama tapi jadi
masalah nyata begitu kode baru membaca computed `display`. Diverifikasi lewat:
- 177/177 `PdfEr.Core.Tests` (termasuk 8 test baru `BoxTreeBuilderTests`) stabil 3x run.
- 54 Infrastructure + 46 Integration tetap hijau.
- Fidelity harness (Fase 0): skor SSIM **identik** sebelum/sesudah perubahan stylesheet
  default (0.998–0.999 di semua kategori) — bukti empiris pipeline streaming benar-benar
  tidak terpengaruh, bukan cuma asumsi.

**Sesi 3**: `IntrinsicSizeCalculator` — Pass 1 sungguhan, min/max-content width dari font
metrics nyata. Tidak menyentuh `LayoutEngine`/`PdfConverterService` sama sekali (murni
tipe+kalkulator baru dikonsumsi lewat unit test langsung), jadi verifikasi cukup lewat
187/187 `PdfEr.Core.Tests` (10 test baru `IntrinsicSizeCalculatorTests`, expected value
dihitung tangan dengan advance width tetap 5pt/karakter) stabil 3x run, plus 54
Infrastructure + 46 Integration tetap hijau. Build solution penuh tanpa error.

**Belum dikerjakan (sengaja, untuk sesi terpisah)**: Pass 2 (placement — ini yang
sebenarnya menghasilkan `BoxGeometry`/X,Y/lebar-tinggi final), migrasi tag handler,
adapter paint, anonymous-wrapping untuk table/flex/grid container. Fase 1 tetap XL;
tiga slice ini baru tipe data + pohon box + lebar intrinsik — **belum ada satu pun
kotak yang benar-benar ditempatkan** (X/Y/Height tetap default/kosong di `BoxGeometry`).

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
