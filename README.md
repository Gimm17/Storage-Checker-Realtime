<div align="center">

# 🛡️ Storage Checker Realtime

### Tahu persis apa yang membuat storage laptop kamu penuh — secara realtime.

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D6?style=for-the-badge&logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![WPF](https://img.shields.io/badge/UI-WPF-1572B6?style=for-the-badge)](https://learn.microsoft.com/dotnet/desktop/wpf/)
[![License](https://img.shields.io/badge/License-MIT-green?style=for-the-badge)](#-lisensi)

*Pernah bingung kenapa storage tiba-tiba berkurang 1–5 GB dalam sehari?*
*Aplikasi ini menjawabnya — file apa, di mana, dari mana, dan apakah aman dihapus.*

</div>

---

## 📖 Latar Belakang

Storage penuh tanpa sebab yang jelas adalah masalah klasik. Cache browser membengkak, `node_modules` menumpuk saat ngoding, Windows Update mengunduh file di belakang layar, atau ada file misterius yang entah dari mana. **Storage Checker Realtime** memantau setiap penulisan file ke disk secara langsung dan memberi tahu kamu persis apa yang terjadi — dari laptop menyala hingga dimatikan.

---

## ✨ Fitur Utama

| | Fitur | Deskripsi |
|---|-------|-----------|
| ⚡ | **Monitoring Realtime** | Memantau disk **C, D, E** lewat NTFS USN Journal — sangat ringan & tidak melewatkan satu perubahan pun |
| 🏷️ | **Kategorisasi Cerdas** | Otomatis mengelompokkan: cache browser, dependencies dev, Windows Update, installer, log, media, game, OneDrive, dll |
| 🚦 | **Penanda Keamanan** | Setiap file ditandai **Aman / Hati-hati / Berbahaya** sebelum dihapus — file sistem terlindungi |
| 🗑️ | **Hapus Aman** | Tombol Delete (ke Recycle Bin, bisa di-undo) dengan konfirmasi ganda untuk file berbahaya |
| 📂 | **Open in Explorer** | Langsung buka lokasi file dengan satu klik |
| 📅 | **Riwayat Harian** | Investigasi "kenapa kemarin nambah 5 GB" — lihat rincian per tanggal dari database lokal |
| 🪶 | **Ringan & Senyap** | Berjalan di system tray, CPU nyaris 0% saat idle, hemat RAM |
| 🚀 | **Auto-start** | Opsi jalan otomatis saat Windows menyala (elevated, tanpa prompt UAC berulang) |

---

## 🖼️ Tampilan

> 💡 *Tambahkan screenshot aplikasi di sini setelah dijalankan — letakkan gambar di folder `docs/` lalu sematkan dengan `![Dashboard](docs/dashboard.png)`.*

```
┌─ Storage Checker Realtime ───────────────────────────────────────┐
│ Status: Memantau C:, D:, E:     Total pertambahan sesi: 2.4 GB    │
│ [Open in Explorer] [Delete]  ☑ Jalankan saat Windows menyala      │
├──────────────────────────────────────────────────────────────────┤
│ Waktu    │Drive│ Nama File      │Δ Tambah│Kategori     │Keamanan   │
│ 14:02:11 │ C:  │ chunk-abc.js   │ 320 MB │Dependencies │Hati-hati  │
│ 14:01:50 │ C:  │ f_004512       │  88 MB │Cache Browser│Aman       │
│ 14:00:03 │ C:  │ update.cab     │ 1.2 GB │Windows Update│BERBAHAYA │
└──────────────────────────────────────────────────────────────────┘
```

---

## 🚀 Cara Pakai

### Pengguna (langsung jalan)

1. Unduh `StorageChecker.App.exe` dari [**Releases**](https://github.com/Gimm17/Storage-Checker-Realtime/releases) (atau dari folder `publish/`).
2. **Klik kanan → Run as administrator** (wajib, untuk akses USN Journal & folder tersembunyi).
3. Aplikasi langsung menyembunyikan diri ke **system tray** dan mulai memantau.
4. **Double-click ikon tray** untuk membuka dashboard kapan saja.

> ⚠️ File `.exe` bersifat *self-contained* — tidak perlu menginstal .NET di komputer.

### Jika app berjalan tapi tidak terlihat di tray

Beberapa kondisi (mis. tray gagal terbuat saat auto-start) bisa membuat aplikasi berjalan sebagai "proses hantu" — jalan di latar belakang tapi tidak bisa dibuka. Solusinya:

1. Buka **Task Manager** (`Ctrl + Shift + Esc`).
2. Pilih tab **Details**.
3. Cari `StorageChecker.App.exe`.
4. Klik kanan → **End task**.
5. Jalankan ulang `StorageChecker.App.exe`.

Setelah perbaikan versi terbaru, klik ganda pada `.exe` saat aplikasi sudah berjalan akan memunculkan dashboard yang ada — tanpa perlu membuka Task Manager.

### Build dari source

```bash
# Butuh .NET 8 SDK
git clone https://github.com/Gimm17/Storage-Checker-Realtime.git
cd Storage-Checker-Realtime

# Build & test
dotnet build StorageChecker.sln -c Release
dotnet test

# Hasilkan satu file .exe (self-contained)
dotnet publish src/StorageChecker.App/StorageChecker.App.csproj \
  -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

---

## ⚙️ Cara Kerja

Aplikasi membaca **NTFS USN Change Journal** — log perubahan bawaan Windows di level kernel. Pendekatan ini dipilih ketimbang `FileSystemWatcher` karena:

- 🪶 **Ringan** — kernel yang mencatat, aplikasi tinggal membaca
- 🎯 **Tidak ada yang terlewat** — log persisten, tahan beban tinggi
- ⏮️ **Catch-up** — menyimpan posisi terakhir (USN cursor), sehingga perubahan sejak boot tetap tertangkap meski aplikasi baru jalan

```
USN Journal (C/D/E) → Parse → Resolve Path → Kategorisasi → Cek Keamanan
                                                  ↓
                                    SQLite (riwayat)  +  UI Realtime (tray)
```

---

## 🧱 Arsitektur

| Proyek | Peran |
|--------|-------|
| `StorageChecker.Core` | Logika inti: USN reader, kategorisasi, keamanan, SQLite, agregasi (tanpa UI) |
| `StorageChecker.App` | Aplikasi WPF: dashboard, system tray, file operations, auto-start |
| `StorageChecker.Tests` | Unit test (xUnit) — 33 test mencakup kategorisasi, keamanan, database, agregasi |

**Stack:** C# • .NET 8 • WPF • MVVM (CommunityToolkit) • SQLite • P/Invoke (kernel32)

---

## 🔒 Keamanan & Privasi

- Semua data **disimpan lokal** di `%LOCALAPPDATA%\StorageChecker\` — tidak ada yang dikirim ke mana pun.
- File sistem kritis (Windows, Program Files, pagefile, dll) ditandai **Berbahaya** dan dilindungi konfirmasi ganda.
- Penghapusan default ke **Recycle Bin** sehingga selalu bisa dibatalkan.
- Butuh hak Administrator **hanya** untuk membaca journal & folder tersembunyi.

---

## 📋 Catatan

- Hanya mendukung volume **NTFS** (default Windows). Volume exFAT/FAT32 akan dilewati.
- Tab Riwayat terisi seiring aplikasi berjalan — wajar bila kosong saat pertama dipakai.

---

## 📄 Lisensi

Dirilis di bawah lisensi **MIT** — bebas digunakan, dimodifikasi, dan disebarkan.

<div align="center">

**Dibuat untuk menjawab satu pertanyaan sederhana: "Ke mana perginya storage saya?"**

⭐ *Star repo ini jika bermanfaat!*

</div>

