# ScreenshotQ Release Automation

## Quick Start

```powershell
# Build portable + zip (tự tăng patch version)
.\build-release.ps1 -SkipInstaller

# Build với tăng minor version
.\build-release.ps1 -Bump minor -SkipInstaller

# Build tất cả (portable + exe + installer) - cần Inno Setup
.\build-release.ps1
```

## Output được tạo

```
dist/
├── ScreenshotQ-portable-v1.0.2.zip  ← Bản portable nén
├── portable/                        ← Folder bản portable
├── exe/                             ← Folder dùng build installer
└── installer/                       ← Installer EXE (nếu có Inno Setup)
```

## Tính năng

✅ **Tự động tăng version** (major/minor/patch)  
✅ **Build bản portable** (win-x64 hoặc win-x86)  
✅ **Nén portable thành ZIP** với tên version  
✅ **Build installer exe** (tuỳ chọn, cần Inno Setup)  
✅ **Cập nhật version tự động** trong setup.iss  

## Yêu cầu

- .NET 8 SDK  
- PowerShell 5.1+
- Inno Setup 6 (tuỳ chọn, chỉ để build installer)

---

Xem [BUILD_GUIDE.md](BUILD_GUIDE.md) để chi tiết đầy đủ.
