# Silica Gel

A sleek, modern IDE for **Gel** and **Silica** — built in C# with Avalonia 11. Companion to the Axion game engine.

> Status: **v0.1** — full editor with file browser, tabbed editing, syntax highlighting for both languages, dark theme, and one-click cross-language conversion (Gel ↔ Silica ↔ C#). Hot completion popup is wired (`Completions` catalog) and ready to plug in when AvaloniaEdit's `CompletionWindow` is opened on `.` keystrokes (v0.2).

---

## Features

| | |
|---|---|
| 📁 | File explorer (recursively walks any folder) |
| ⌘  | Tabbed editor with dirty-state markers |
| 🎨 | Custom syntax highlighting for `.gel` and `.sil` (XSHD via AvaloniaEdit) |
| 🌑 | Hand-crafted dark theme — slate background, electric mint accent |
| 🔄 | One-click conversion: Gel → Silica, Silica → Gel, either → C# |
| 🧠 | Engine-aware completion catalog (Time, Input, Transform, Vec, Mathf…) |
| 🪶 | Avalonia 11 — runs on Windows, Linux, macOS |

---

## Build & run

**Requirements:** Visual Studio 2022 (17.8+) **or** the .NET 8 SDK.

```bash
cd silica-gel-ide
dotnet build -c Release
dotnet run --project src/SilicaGel
```

In Visual Studio: open `SilicaGel.sln`, press **F5**.

When launched, the IDE auto-discovers `axion-engine/samples/` if the engine is checked out alongside the IDE — so you'll see `cube.gel`, `cube.sil`, and `cube.blocks.json` ready to edit. Use **Open Folder…** to point it at any project.

---

## Cross-language conversion

The IDE links the engine's parser/converter source directly (`Languages/Engine/*.cs` are linked from `axion-engine/src/Axion.Scripting/`), so the IDE and engine share **one** AST and conversion path. Click **Convert → Silica** while editing a `.gel` file and a new tab opens with the Silica equivalent. Same for `Convert → C#` — useful for understanding what the engine actually compiles via Roslyn.

If the engine isn't checked out next to the IDE, the build still succeeds — those linked files are conditional. You'll lose conversion until you check out the engine repo.

---

## Project layout

```
silica-gel-ide/
├── SilicaGel.sln
└── src/SilicaGel/
    ├── Program.cs                 ← Avalonia app entry
    ├── App.axaml(.cs)             ← Application root + theming hookup
    ├── MainWindow.axaml(.cs)      ← The IDE shell — tree, tabs, console, status
    ├── MainWindowViewModel.cs     ← Commands, file ops, conversion
    ├── EditorTabViewModel.cs      ← Per-tab state (document, dirty flag, save)
    ├── ViewModelBase.cs
    ├── Themes/
    │   └── DarkTheme.axaml        ← Slate + mint-accent palette
    └── Languages/
        ├── Gel.xshd               ← AvaloniaEdit syntax highlighting (Gel)
        ├── Silica.xshd            ← AvaloniaEdit syntax highlighting (Silica)
        ├── SilicaGelHighlighting.cs  ← XSHD registration
        ├── CompletionProvider.cs     ← Engine-aware completion catalog
        └── Engine/                   ← Linked source from axion-engine/src/Axion.Scripting/
```

---

## Roadmap (v0.2)

- Live completion popup triggered on `.` and identifier prefix
- "Run in Axion" — launch the engine on the active scene file
- Multi-file project support (`silica-gel-project.json`)
- Diagnostics squiggles from on-the-fly parse errors
- Format-on-save (AST round-trip)
- Light theme + theme picker
- Settings panel

---

© 2026 — MIT licensed.
