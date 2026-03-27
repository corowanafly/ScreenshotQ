# ScreenshotQ Build & Release Guide

## Tự động Build + Tăng Version + Zip Portable + Tạo Installer

Script `build-release.ps1` tự động xe toàn bộ quy trình release:

1. **Tăng version** (tự động update vào ScreenshotQ.csproj)
2. **Build portable** (self-contained)
3. **Nén portable** thành `.zip`
4. **Build installer EXE** (tuỳ chọn)
5. **Cập nhật version** trong setup.iss cho installer

---

## Cách sử dụng

### Dùng mặc định (tăng patch version, build tất cả):
```powershell
.\build-release.ps1
```

### Tăng minor version:
```powershell
.\build-release.ps1 -Bump minor
```

### Tăng major version:
```powershell
.\build-release.ps1 -Bump major
```

### Skip installer (chỉ build portable, không cần Inno Setup):
```powershell
.\build-release.ps1 -SkipInstaller
```

### Build cho architecture khác (mặc định: win-x64):
```powershell
.\build-release.ps1 -Runtime win-x86
```

### Kết hợp các option:
```powershell
.\build-release.ps1 -Bump minor -SkipInstaller
```

---

## Output

Sau khi build, bạn sẽ có:

```
dist/
├── portable/                      # Folder bản portable
├── ScreenshotQ-portable-v1.0.2.zip  # Zip bản portable
├── exe/                           # Folder dùng cho installer
└── installer/                     # Installer EXE (nếu không dùng -SkipInstaller)
    └── ScreenshotQ-Setup.exe
```

---

## Yêu cầu

### Bắt buộc:
- **.NET 8 SDK** (để chạy `dotnet publish`)
- PowerShell 5.1+ (Windows mặc định có)

### Tuỳ chọn:
- **Inno Setup 6** (chỉ cần nếu muốn build installer `.exe`)
  - Download: https://jrsoftware.org/isdl.php
  - Nếu chưa cài, dùng option `-SkipInstaller`

---

## Cách hoạt động

### 1. Tăng Version
Script đọc version hiện tại từ `ScreenshotQ.csproj`, tính toán version mới dựa trên `$Bump`, rồi cập nhật:
- `<Version>`
- `<AssemblyVersion>`
- `<FileVersion>`

### 2. Build Portable
```powershell
dotnet publish -c Release -f net8.0-windows7.0 -r win-x64 --self-contained false -o dist\portable
```

### 3. Zip Portable
Nén tất cả file từ `dist\portable` thành `ScreenshotQ-portable-v{VERSION}.zip`

### 4. Build Installer (nếu có Inno Setup)
Gọi `ISCC.exe` với `/DAppVersion=X.Y.Z` để override version trong setup.iss:
```powershell
iscc.exe /DAppVersion=1.0.2 .\setup.iss
```

Setup.iss giờ hỗ trợ `#ifndef AppVersion` nên version từ script sẽ được dùng, không bị hardcode `1.0.0`.

---

## Ví dụ workflow

### Build bản phát hành chính thức:
```powershell
# Build portable + zip (không cần Inno Setup)
.\build-release.ps1 -SkipInstaller

# Hoặc nếu cài Inno Setup, build cả installer:
.\build-release.ps1
```

### Build với bump version khác:
```powershell
# Tăng minor (1.0.2 → 1.1.0)
.\build-release.ps1 -Bump minor

# Tăng major (1.1.0 → 2.0.0)
.\build-release.ps1 -Bump major
```

---

## Lưu ý

- Script **tỉnh táo thay đổi version** mỗi lần chạy, ngay cả nếu build thất bại sau bước version bump.
  Giải pháp: Restore file `ScreenshotQ.csproj` từ git nếu muốn reset.
  
- Nếu dùng **git**, commit `ScreenshotQ.csproj` sau mỗi phát hành để track version.

- Build directory (`dist/`) sẽ được **xoá và tái tạo** mỗi lần script chạy.

---

## Troubleshooting

### Lỗi: "Restore failed" hoặc "Dependencies not found"
```powershell
# Chạy restore thủ công trước
dotnet restore ScreenshotQ.csproj -r win-x64
```

### Lỗi: "Inno Setup not found"
- Cài Inno Setup 6 từ https://jrsoftware.org/isdl.php
- Hoặc dùng `-SkipInstaller` flag

### Lỗi: ExecutionPolicy
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope Process
```

---

## Chi tiết script

File: `build-release.ps1`
- Quản lý version automation
- Ưu tiên rebuild toàn bộ mỗi lần (tránh cache issues)
- Xuất thông tin rõ ràng từng bước

File: `setup.iss`
- Sửa để chiều `AppVersion` từ command-line
- Không còn bị hardcode `1.0.0`
