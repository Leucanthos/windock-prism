# WinDock Test Suite

## Quick Start
```powershell
.\build-tests.ps1   # Compile C# test-*.cs → test-*.exe
.\run-all.ps1       # Run full test suite
```

## Test Categories

### C# Unit Tests (test-*.cs → test-*.exe)
Compiled by `build-tests.ps1`, run by `run-all.ps1`. Each writes a result file to `$env:TEMP\`.

| Test | Area |
|---|---|
| test-icon-lifecycle | Icon create/destroy/refresh |
| test-layout | Layout algorithm |
| test-magnification | Hover magnification |
| test-pin-unpin | Pin/unpin (standalone, no dock deps) |
| test-theme-switch | Theme toggling |
| test-taskbar-toggle | Taskbar visibility |
| test-context-menu | Right-click menu |
| test-badge | Badge count |
| test-mutex-singleton | Single-instance mutex |
| test-coordinates | Screen coordinate calculations |
| test-dock-pin | Dock pin logic |
| test-edge-cases | Edge/boundary conditions |
| test-ui-inspect | UI control inspection |
| test-realtime-sync | Real-time sync |
| test-appbar | AppBar integration |
| test-messy-user | Messy user behavior |

### PowerShell Integration Tests (integration/)
| Script | Verifies |
|---|---|
| test-startup-dump.ps1 | Dock starts cleanly with debug output |
| test-toggle-cycle.ps1 | Toggle cycle doesn't leak or crash |
| test-theme-detect.ps1 | Auto theme detection |
| test-badge-count.ps1 | Badge count accuracy |
| test-pin-end-to-end.ps1 | End-to-end pin/unpin workflow |

### Utility Scripts
| Script | Purpose |
|---|---|
| identify-icons.ps1 | List dock icon windows (debug) |
| line-align.ps1 | Verify line endpoints match icon centers |

### Archive (archive/)
Retired legacy tests — TestContextMenu.cs, TestGlow.cs, TestIcon.cs

## Temp Files
All test output goes to `$env:TEMP\` (e.g., `_dock_dump.txt`, `_test_*_result.txt`).
