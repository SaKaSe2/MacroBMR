Spesifikasi Desain UI Presisi: Smart Macro Recorder (C# WPF/WinUI 3)

Dokumen ini adalah Design System dan Redline Specifications untuk aplikasi "Smart Macro Recorder". Semua ukuran menggunakan unit piksel referensi logis (px) yang kompatibel dengan sistem scaling DPI di WPF/WinUI. Desain ini dirancang untuk mencapai antarmuka yang bersih, "techie", dan profesional.

1. Sistem Warna (Color Tokens)

Warna tidak boleh di-hardcode di dalam UI. Gunakan Resource Dictionary untuk mendefinisikan color tokens ini dan panggil sebagai DynamicResource atau StaticResource.

1.1. Base Colors (Berdasarkan image_0d2075.png)

Base-900 (Blue Black): #021C1E

Base-700 (Cadet Blue): #004445

Accent-500 (Rain): #2C7873

Highlight-300 (Greenery): #6FB98F

1.2. Semantic Colors (Diturunkan dari Base Colors)

Background.Window: Base-900 (#021C1E)

Background.Panel: Base-700 (#004445) - Digunakan untuk sidebar, cards, modal.

Background.Hover: Accent-500 dengan Opacity 20% (rgba(44, 120, 115, 0.2))

Border.Subtle: Accent-500 dengan Opacity 50% (rgba(44, 120, 115, 0.5))

Border.Active: Accent-500 (#2C7873)

Control.Primary.Background: Accent-500 (#2C7873)

Control.Primary.Hover: Highlight-300 (#6FB98F)

Control.Action.Background: Highlight-300 (#6FB98F) - Untuk tombol "Record" yang krusial.

Text.Primary: #FFFFFF (Putih murni) - Untuk judul, teks utama, angka data.

Text.Secondary: #B3B9B9 (Abu-abu kebiruan terang) - Untuk label, subjudul, placeholder.

Status.Error/Delete: #FF6B6B (Merah pudar/Coral) - Tidak ada di palet, perlu ditambahkan untuk status kritis (opsional tapi disarankan).

2. Tipografi (Typography Hierarchy)

Font direkomendasikan: Inter atau Segoe UI Variable (bawaan Windows 11).

H1 (Page Title): Size: 24px | Weight: SemiBold (600) | Color: Text.Primary | Line Height: 32px

H2 (Section/Card Title): Size: 16px | Weight: SemiBold (600) | Color: Text.Primary | Line Height: 24px

Body.Main (Data Grid/Normal Text): Size: 14px | Weight: Regular (400) | Color: Text.Primary | Line Height: 20px

Body.Sub (Labels/Helpers): Size: 12px | Weight: Regular (400) | Color: Text.Secondary | Line Height: 16px

Data/Numbers (Dashboard Cards): Size: 32px | Weight: Bold (700) | Color: Highlight-300 | Line Height: 40px

3. Tata Letak (Layout & Grid System)

3.1. Struktur Window

Window State: Kustom (Tanpa title bar bawaan Windows / Chromeless Window).

Title Bar (Kustom): Height: 32px | Background: Background.Window (#021C1E). Berisi tombol minimize, maximize, close standar di kanan.

Corner Radius (Window): 8px (Jika menggunakan Windows 11, biarkan OS yang menangani. Jika kustom penuh, terapkan di border terluar).

3.2. Spasi & Margin (Grid 8px)

Gunakan kelipatan 8px untuk menjaga ritme visual.

Padding Main Content: 24px (Atas, Bawah, Kiri, Kanan).

Gap antar elemen kecil (seperti ikon dan teks): 8px.

Gap antar elemen sedang (seperti textbox berderet): 16px.

Gap antar seksi besar (seperti Card ke DataGrid): 32px.

3.3. Sidebar (Kiri)

Width: Fixed 240px.

Background: Background.Panel (#004445).

Separator (Batas Kanan): Solid, 1px, warna Background.Window (#021C1E) (atau biarkan kontras warna yang memisahkan).

Padding Internal: 16px atas/bawah, 16px kiri/kanan.

4. Komponen UI Spesifik & Dimensi

4.1. Sidebar Navigation Item

Height: 40px.

Padding: Left 16px, Right 16px.

Corner Radius: 6px (Bentuk pil / rounded rectangle pada item saat hover/aktif, margin dalam 8px dari batas sidebar).

Icon Size: 20px x 20px.

Gap (Icon ke Teks): 12px.

State Normal: Icon & Teks Text.Secondary. Background Transparent.

State Hover: Background Background.Hover.

State Active/Selected: Background Control.Primary.Background (#2C7873). Icon & Teks Text.Primary.

4.2. Tombol Utama (Button)

Height: 36px (Standar), 48px (Untuk tombol "Record" besar).

Padding: Horizontal 24px (kecuali tombol icon-only).

Corner Radius: 6px.

Font: Size 14px, Weight SemiBold.

Primary Button (misal: "Save Macro"): Background Accent-500, Text Text.Primary. Hover Background Highlight-300.

Action Button (misal: "Record"): Background Highlight-300, Text Background.Window (gelap untuk kontras).

4.3. Input Field (TextBox / ComboBox)

Height: 36px.

Background: Background.Window (#021C1E) di dalam form/modal, atau Background.Panel (#004445) jika berada di atas latar Base-900.

Border: Solid, 1px, warna Border.Subtle.

Corner Radius: 4px.

Padding Internal: Left 12px, Right 12px.

Text Color: Text.Primary. Placeholder: Text.Secondary.

State Focus: Border berubah menjadi Border.Active (#2C7873), ketebalan 2px (pastikan layout tidak bergeser, kurangi padding 1px jika border bertambah tebal).

4.4. Dashboard Cards

Min-Width: 200px.

Height: 100px.

Background: Background.Panel (#004445).

Corner Radius: 8px.

Padding: 16px keliling.

Efek (Shadow): Drop shadow sangat tipis (WPF: DropShadowEffect, Color: #000000, BlurRadius: 10, Direction: 270, Depth: 4, Opacity: 0.15) untuk memberi sedikit kedalaman tanpa terlihat norak.

Layout Card Internal: Grid. Baris 1: Title (Body.Sub). Baris 2: Data/Angka (Data/Numbers).

4.5. DataGrid (Macro List)

Header Height: 40px.

Header Background: Background.Panel (#004445).

Header Border Bottom: Solid, 2px, warna Accent-500 (#2C7873).

Row Height: 48px.

Row Background (Zebra Striping): Genap Background.Window (#021C1E), Ganjil sedikit lebih terang (misal: campur Base-900 dengan putih 2% atau gunakan #052325).

Row Hover: Latar Background.Hover.

Cell Padding: Horizontal 16px, Vertical 0 (Center Alignment).

4.6. Floating Action / Recording Overlay

Size: Lebar 250px, Tinggi 60px.

Background: Background.Panel (#004445).

Border: Solid, 2px, warna Highlight-300 (#6FB98F) agar sangat terlihat saat aplikasi lain terbuka.

Corner Radius: 30px (bentuk pil utuh).

Shadow: (WPF: DropShadowEffect, Color: #000000, BlurRadius: 15, Depth: 5, Opacity: 0.3).

5. Responsivitas (Layout Constraints)

Untuk aplikasi desktop, responsivitas berkaitan dengan perubahan ukuran window.

Window Min-Width: 800px.

Window Min-Height: 600px.

Sidebar (XAML Grid Column): Width="240". Tidak berubah saat window membesar.

Main Content (XAML Grid Column): Width="\*". Mengisi sisa ruang.

Dashboard Grid (WPF WrapPanel atau UniformGrid):

Jika WrapPanel: Beri kartu margin kanan 16px. Saat window mengecil, kartu paling kanan akan otomatis turun ke baris berikutnya.

DataGrid Columns:

Kolom "Name" & "Description": Width="\*".

Kolom "Shortcut", "Date", "Actions": Lebar tetap (misal: Width="120", Width="150"). Pastikan kolom aksi tidak pernah terpotong.

Settings Page: Gunakan ScrollViewer untuk area konten agar jika vertikal mengecil, user dapat men-scroll pengaturan tanpa memotong form.

6. Aset Premium / Ikon

Ikon (Wajib): Gunakan Lucide Icons (tersedia paket NuGet untuk WPF/C#) atau Material Design Icons XAML.

Hindari ikon berisi (filled), gunakan ikon garis (outlined) dengan ketebalan garis (stroke) 1.5px hingga 2px untuk tampilan UI bersih dan modern (mirip Fluent Design).

Toggle Switch: Jangan gunakan CheckBox default WPF. Buat ControlTemplate kustom untuk CheckBox agar terlihat seperti Toggle Switch ala iOS/Windows 11.

Track Lebar: 40px, Tinggi: 20px, Corner Radius: 10px.

Thumb Lebar: 16px, Tinggi: 16px, Corner Radius: 8px. Margin 2px dari tepi.

Spesifikasi ini dirancang agar siap diterjemahkan langsung menjadi XAML styling dan ControlTemplates di WPF/WinUI.
