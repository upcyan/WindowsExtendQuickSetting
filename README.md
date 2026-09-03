# WindowsExtendQuickSetting

`native/` is the new dependency-free Win32/C++ implementation. Build it with:

```powershell
powershell -ExecutionPolicy Bypass -File .\native\build-release.ps1
```

Its single executable is written to `release\native\WindowsExtendQuickSetting.Native.exe`.

`winui3/` retains the previous WinUI 3 implementation and its Full/Lite publishing pipeline.
Run `powershell -ExecutionPolicy Bypass -File .\winui3\publish-release.ps1` to write:

- `release\winui3\full\WindowsExtendQuickSetting.exe`
- `release\winui3\lite\WindowsExtendQuickSetting.Lite.exe`
