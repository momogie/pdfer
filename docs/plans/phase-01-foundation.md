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
- [ ] **Box generation**: walk DOM → box tree, sisipkan **anonymous box** saat block & inline
      bercampur. Belum dikerjakan — `LayoutBox`/`ComputedStyle` baru ada sebagai tipe data,
      belum ada kode yang membangun pohon dari DOM.
- [x] **ComputedStyle** sebagai tipe terpisah di
      [ComputedStyle.cs](../../src/PdfEr.Core/Domain/Styles/ComputedStyle.cs) — membungkus
      `CssDeclarationBlock` hasil cascade + pre-resolve font-size (pt & mm, cocok dengan
      `LayoutEngine.GetFontSizePt`), `display`, `position`, `float`. **Catatan jujur**: ini
      *belum* memisahkan `CreateBlock`'s resolve+layout tergabung di
      [LayoutEngine.cs:145-191](../../src/PdfEr.Core/Application/HtmlProcessing/LayoutEngine.cs#L145-L191) —
      `ComputedStyle` ada sebagai tipe siap pakai, tapi belum dipanggil dari pipeline manapun.
      Juga belum meng-cover semua properti (width/height/margin/padding % masih perlu
      containing block, sengaja belum diresolusi di sini).
- [ ] **Pass 1 — intrinsic sizing**: tipe `IntrinsicSizes` sudah ada di `BoxTree.cs`, tapi
      **belum ada kode yang menghitungnya**. Tebakan `textLen * fontSize * 0.5f` di
      LayoutEngine masih dipakai apa adanya.
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

## Progres nyata sesi ini (bukan Fase 1 selesai — lihat status per item di atas)

Slice pertama yang dikerjakan: **tipe data box-tree murni tambahan**, tanpa mengubah satu
baris pun di pipeline streaming yang berjalan (`LayoutEngine`, `PdfConverterService`,
tag handler). Tidak ada regresi mungkin karena kode lama tidak disentuh — dikonfirmasi
lewat build solution penuh + 169/169 test `PdfEr.Core.Tests` (termasuk 19 test baru untuk
`ComputedStyle`/`LayoutBox`) lolos stabil 3x berturut-turut, plus 54 test Infrastructure
dan 46 test Integration tetap hijau.

Sebagai bagian dari verifikasi baseline sebelum menyentuh area ini, ditemukan dan diperbaiki
bug race-condition nyata (tidak terkait box-tree) di `CssMerger`: `CssParser.Parse()` meng-
cache `CssRule`/`CssDeclarationBlock` secara statis lintas proses, dan dua kode caller
memutasi objek yang sama itu di tempat — race di bawah eksekusi test paralel xUnit. Lihat
commit terpisah untuk detail; disebut di sini karena ditemukan selagi meng-audit `CssMerger`
untuk memahami alur `ResolveStyles` yang akan dipanggil `ComputedStyle.Resolve` nantinya.

**Belum dikerjakan (sengaja, untuk sesi terpisah)**: box generation dari DOM, Pass 1/2
sungguhan, migrasi tag handler, adapter paint. Fase 1 tetap XL; slice ini hanya fondasi
tipe data yang aman untuk dibangun di atasnya tanpa risiko regresi.

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
