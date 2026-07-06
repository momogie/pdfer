# Fase 2 — Block & Inline Formatting yang Benar

**Ukuran:** L · **Prasyarat:** Fase 1

## Tujuan

Dokumen "normal flow" (heading, paragraf, list, blockquote, div bersarang) ter-render
dengan **spacing setara Chrome dalam beberapa px**. Ini menutup gap dari
[baseline-audit.md](baseline-audit.md): tak ada margin collapse, tak ada `box-sizing`,
tak ada line box sungguhan.

> **Catatan penting (setelah Fase 1 selesai)**: checklist di bawah aslinya ditulis
> dengan asumsi memperbaiki `LayoutEngine` (pipeline streaming lama), makanya banyak
> referensi baris kode ke `LayoutEngine.cs`. Sejak Fase 1, **box-tree pipeline**
> (`BoxTreeBuilder`/`IntrinsicSizeCalculator`/`BlockPlacer`) sudah menggantikan
> sebagian besar tanggung jawab itu, dan beberapa item di bawah **sudah selesai di sana**
> selama Fase 1 (margin collapsing adjacent-sibling, width `auto`/`%`, line box
> sungguhan/real word-wrap). Pekerjaan Fase 2 sekarang menargetkan `BlockPlacer`, bukan
> `LayoutEngine` — `LayoutEngine` tetap tidak disentuh (jalur default,
> `UseBoxTreeLayout=false`) sampai box-tree pipeline benar-benar menggantikannya.

## Checklist — Block formatting

- [~] **Margin collapsing**: adjacent-sibling **sudah selesai di Fase 1**
      ([BlockPlacer.cs `CollapseMargins`](../../src/PdfEr.Core/Application/HtmlProcessing/BlockPlacer.cs)).
      Parent↔first/last child & blok kosong **masih belum** (butuh border/padding
      context lintas rekursi, perubahan struktural terpisah).
- [x] **`box-sizing`** `content-box` (default) & `border-box` — diterapkan konsisten ke
      width **dan** height eksplisit di `BlockPlacer.ResolveWidth`/`Place` (properti CSS
      `width`/`height` berarti content-box secara default, ditambah padding+border untuk
      jadi border-box geometry; `box-sizing:border-box` memakai nilai apa adanya).
      Diverifikasi: 4 test baru (`Place_ExplicitWidth/Height_DefaultBoxSizing_*`,
      `Place_ExplicitWidth/Height_BorderBoxSizing_*`) + harness fidelity (div
      `box-sizing:border-box` dengan padding+border → **SSIM 0.9997** vs Chromium).
- [x] **Width `auto` & `%`** — **sudah selesai di Fase 1** (`BlockPlacer.ResolveWidth`,
      dihitung dari `ContainingBlock`, bukan lebar halaman).
- [x] **Height `auto`** dari isi; **`height: %`** — **sudah selesai di Fase 1**
      (`BlockPlacer.ResolveExplicitLength`, hanya diresolusi kalau containing block
      `HeightIsDefinite`, sesuai CSS 2.1 §10.5).
- [ ] **`min/max-width/height`** — belum diterapkan di `BlockPlacer` sama sekali.
- [ ] **Margin auto** untuk centering horizontal (`margin: 0 auto`) — belum dikerjakan.

## Checklist — Inline formatting (IFC)

- [x] **Line box** sungguhan — **sudah selesai di Fase 1**
      (`BlockPlacer.PlaceInlineContent`: greedy word-wrap dibatasi lebar konten nyata,
      bukan satu `InlineBox` per blok). Diverifikasi SSIM 0.9998 pada paragraf yang
      sengaja dibuat lebar untuk memaksa wrap.
- [ ] **Baseline alignment** & **`vertical-align`** — belum dikerjakan di box-tree pipeline.
- [x] **`line-height`** sebagai jarak antar-baseline — pakai `FontSizeMm * 1.3` (auto
      line-height factor), sama seperti streaming pipeline. **Belum** membaca CSS
      `line-height` eksplisit (`1.5`, `20px`, dst.) — masih hardcoded 1.3.
- [~] **`inline-block`** — shrink-to-fit width **sudah ada** dari Fase 1
      (`BlockPlacer.ResolveWidth`, pakai `Intrinsic.MaxContentWidth`), tapi **belum**
      benar-benar ikut aliran baris IFC (inline-block masih diperlakukan seperti block
      biasa di percabangan utama `Place`, bukan sebagai atomic inline item di
      `PlaceInlineContent`/`CollectInlineItems`).
- [ ] **`white-space`**: `normal | nowrap | pre | pre-wrap | pre-line` — belum
      dikerjakan; `PlaceInlineContent` selalu berperilaku seperti `normal`.
- [x] **`text-align`** termasuk **`justify`** (distribusi ruang antar-kata, baris
      terakhir tidak di-justify sesuai CSS 2.1 §16.2) & centering/right — diverifikasi
      lewat 5 unit test (`Place_TextAlignLeft/Right/Center/Justify_*`) + harness
      fidelity (kombinasi center/right/justify dalam satu dokumen → **SSIM 0.9997**
      vs Chromium). **`text-indent`** belum dikerjakan.
- [ ] **`text-transform`, `text-decoration`** — belum dikerjakan di box-tree pipeline
      (streaming pipeline sudah punya sebagian via tag handler `UnderlineHandler` dll,
      tapi itu tidak dipakai jalur box-tree).

## Checklist — Overflow dasar

- [ ] `overflow: visible | hidden` — belum dikerjakan di box-tree pipeline.

## Kriteria selesai

- Kategori uji `text-*` dan dokumen normal-flow campuran mencapai **SSIM ≥ 0.85** vs Chromium.
  **Sudah terlampaui secara empiris** untuk kasus yang diuji sejauh ini (0.9997–1.0000),
  tapi korpus uji Fase 0 baru 5+7 kasus — belum representasi luas.
- Jarak vertikal antar-blok (dengan margin collapse) cocok Chrome dalam toleransi kecil.
  ✅ untuk adjacent-sibling; parent/child masih belum.
- Paragraf multi-baris membungkus di titik yang sama dengan Chrome (untuk font yang sama).
  ✅ diverifikasi (SSIM 0.9998 pada kasus wrap paksa).

## Progres nyata (1 sesi)

**Sesi 1**: `box-sizing` (content-box/border-box) dan `text-align` (left/right/center/
justify) ditambahkan ke `BlockPlacer`. Keduanya diverifikasi dobel: unit test dengan
nilai tepat dihitung tangan, **dan** harness fidelity langsung ke Chromium
(0.9997 untuk kedua kasus). 256/256 `PdfEr.Core.Tests` stabil 3x run, 54
Infrastructure + 54 Integration tetap hijau, harness fidelity utama Fase 0 (streaming)
**identik** (`LayoutEngine` tidak disentuh). Build solution penuh tanpa error.

**Belum dikerjakan**: min/max-width/height, margin auto centering, vertical-align,
line-height eksplisit dari CSS, inline-block sebagai atomic inline item sungguhan,
white-space, text-indent, text-transform/text-decoration, overflow. Fase 2 sudah
mulai, jauh dari selesai.
