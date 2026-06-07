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
2. **Test** — write comprehensive test scripts FIRST. Cover edge cases, messy user behavior, boundary conditions. Use the project existing test infrastructure (`test/*.cs` files compiled by `build-tests.ps1`).
3. **Develop** — implement the feature, keeping changes minimal and respecting existing code style.
4. **Iterate** — test → fix → test until all tests pass and manual verification confirms stability.

### Code Style
- Static classes with static state (no OOP framework). Match existing patterns.
- Single-source-file EXEs compiled with `csc.exe` (.NET Framework 4.x).
- No external NuGet dependencies.
- All diagnostic output goes to `$env:TEMP\_*.txt`.

## Project Layout

```
├── dock/                  # WinDock — macOS-style desktop dock
│   ├── App.cs             # Entry point
│   ├── Core/              # DockManager, LayoutEngine, PinStore, DockBar, DockLine
│   ├── UI/                # DockIcon, GlassMenu, IconMenu
│   ├── Common/            # Theme, W, Version, DebugMode
│   ├── Common/Debug/      # EventLog, Dumper, Overlay
│   └── test/              # C# test harnesses + PowerShell integration tests
│       ├── build-tests.ps1       # Compile all C# test-*.cs → test-*.exe
│       ├── run-all.ps1           # Master test runner (run full suite)
│       ├── line-align.ps1        # Verify line endpoints == icon centers
│       ├── identify-icons.ps1    # Debug: list dock icon windows
│       ├── test-*.cs             # 16 C# test source files (compiled to exe)
│       ├── archive/              # Retired legacy tests (TestContextMenu, etc.)
│       └── integration/          # PowerShell integration tests
│           ├── test-startup-dump.ps1
│           ├── test-toggle-cycle.ps1
│           ├── test-theme-detect.ps1
│           ├── test-badge-count.ps1
│           └── test-pin-end-to-end.ps1
│
├── prism/                 # WinPrism — desktop widget panel suite
│   ├── Prism.cs           # Entry point
│   ├── Components/        # TopBar, Audio, System, Disk, Network, Battery, RecycleBin, WiFi
│   ├── Common/            # Theme, W, Settings, Version, DebugMode
│   └── test/              # PowerShell test suite
│       ├── run-all.ps1           # Master test runner
│       ├── test.ps1              # Main: compile → launch → verify widgets
│       ├── pipeline.ps1          # CI pipeline (compile + full suite)
│       ├── codecheck.ps1         # Static analysis: hardcoded paths, magic numbers
│       ├── security.ps1          # Security audit: passwords, tokens, COM elevation
│       ├── polling.ps1           # Timer interval reasonability checks
│       ├── resources.ps1         # GDI/Font handle leak detection
│       ├── artifacts.ps1         # Debug leftover detection in production code
│       ├── perf.ps1              # Performance benchmarks (CPU, memory, timing)
│       ├── auto-startup.ps1      # Registry auto-start verification
│       ├── xpu_load.py           # Intel XPU stress test (PyTorch, 30 iter)
│       └── xpu_load2.py          # Intel XPU stress test variant (20 iter)
│
└── memory/                # Persistent session memory
```

## Testing

### WinDock (dock)
```powershell
cd dock
.\test\build-tests.ps1     # Compile C# test harnesses
.\test\run-all.ps1          # Run full test suite
```

### Prism (prism)
```powershell
cd prism
.\test\run-all.ps1          # Run static analysis + runtime tests
.\test\run-all.ps1 -Full    # Include pipeline (compile + full suite)
.\test\test.ps1             # Quick: compile → launch → verify widgets
```

## Diagnostic Files
- WinDock events: `$env:TEMP\_dock_events.txt`
- Prism events: `$env:TEMP\_prism_events.txt`
- Test results: `$env:TEMP\_test_*_result.txt`
