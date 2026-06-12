# WinDock + Prism — Project Constitution

## Git Rules

### No Push Without Explicit Instruction
- **Default**: commit only. Do NOT push to remote unless the user explicitly says "push", "发布", or "上传".
- This applies to all branches including `master` and `v*` release branches.
- When asked to "commit", commit locally only. When asked to "release" or "publish", commit + tag + push.

### Branching
- `master` — active development
- `v*` (e.g., `v0.1.0`) — release branches (single squashed commit)

## Development Workflow

### For All New Features / Non-Trivial Fixes:
1. **Design** — write a brief plan (in conversation or as a markdown doc). Identify affected files, architectural approach, edge cases.
2. **Test** — write comprehensive test scripts FIRST. Cover edge cases, messy user behavior, boundary conditions. Use the project's existing test infrastructure (`test/*.cs` files compiled by `build-tests.ps1`).
3. **Develop** — implement the feature, keeping changes minimal and respecting existing code style.
4. **Iterate** — test → fix → test until all tests pass and manual verification confirms stability.

### Code Style
- Static classes with static state (no OOP framework). Match existing patterns.
- Single-source-file EXEs compiled with `csc.exe` (.NET Framework 4.x).
- No external NuGet dependencies.
- All diagnostic output goes to `C:\temp\_*.txt`.

## Project Layout

```
├── desktop-dock/          # WinDock — macOS-style desktop dock
│   ├── App.cs             # Entry point
│   ├── Core/              # DockManager, LayoutEngine, PinStore, DockBar, DockLine
│   ├── UI/                # DockIcon, GlassMenu, IconMenu
│   ├── Common/            # Theme, W, Version, DebugMode
│   ├── Common/Debug/      # EventLog, Dumper, Overlay
│   └── test/              # C# test harnesses + PowerShell integration tests
│       └── build-tests.ps1  # Compile all tests
│       └── run-all.ps1      # Run full suite
│
├── prism/                 # WinPrism — desktop widget panel suite
│   ├── Prism.cs           # Entry point
│   ├── Components/        # TopBar, Audio, System, Disk, Network, Battery, RecycleBin, WiFi
│   ├── Common/            # Theme, W, Settings, Version, DebugMode
│   └── test/              # PowerShell test pipeline
│
└── memory/                # Persistent session memory
```

## Diagnostic Files
- WinDock events: `C:\temp\_dock_events.txt`
- Prism events: `C:\temp\_prism_events.txt`
- Test results: `C:\temp\_test_*_result.txt`
