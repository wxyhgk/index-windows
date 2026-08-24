# Windows installer

Run from PowerShell at the repository root:

```powershell
.\scripts\build-windows-installer.ps1
```

If the machine blocks local PowerShell scripts, use a process-scoped bypass without changing the
system policy:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-windows-installer.ps1
```

The script publishes a self-contained x64 build, including the Windows App SDK runtime, and
creates two per-user installer packages under `artifacts\installer`. Inno Setup 6 is required:

```powershell
winget install --id JRSoftware.InnoSetup --exact
```

The generated packages are:

- `Index-Setup-<version>-win-x64.exe`: the normal single-file installer. Its loader starts from
  the current user's `%TEMP%` directory.
- `Index-Setup-<version>-win-x64-no-temp.zip`: a scripted fallback for machines whose `%TEMP%`
  directory is not writable. Extract the complete ZIP and run `Install-Index.cmd`. It copies the
  bundled app directly to the per-user program directory without starting Inno Setup.

Both installers add an Index entry to the Start menu, offer an optional desktop shortcut, and
register a normal Windows uninstall entry. Build outputs are intentionally ignored by Git.
