# Prism Test Suite

## Quick Start
```powershell
.\run-all.ps1          # Run static analysis + runtime tests
.\run-all.ps1 -Full    # Include pipeline (compile + full suite)
.\test.ps1             # Quick: compile → launch → verify widgets
```

## Test Categories

### Static Analysis (no runtime dependencies)
| Script | Checks |
|---|---|
| codecheck.ps1 | Hardcoded paths, unused usings, magic numbers |
| security.ps1 | Passwords, tokens, COM elevation, shell execution |
| polling.ps1 | Timer intervals are reasonable |
| resources.ps1 | GDI/Font handle leaks |
| artifacts.ps1 | Debug leftovers in production code |

### Runtime Tests
| Script | Verifies |
|---|---|
| test.ps1 | Compile → launch → all widget windows appear |
| auto-startup.ps1 | Registry auto-start key is written correctly |
| perf.ps1 | CPU, memory, window creation timing |

### CI / Full
| Script | Purpose |
|---|---|
| pipeline.ps1 | Compile + full test suite (one-click CI) |
| run-all.ps1 | Master runner (runs all above, skips pipeline by default) |

### XPU Load Tests
| Script | Purpose |
|---|---|
| xpu_load.py | Intel XPU 2GB matrix multiply, 30 iterations |
| xpu_load2.py | Intel XPU 2GB matrix multiply, 20 iterations |

## Usage Notes
- `run-all.ps1` runs codecheck, security, polling, resources, artifacts, auto-startup, test, and perf.
- Pass `-Full` to also run `pipeline.ps1` (which recompiles from scratch).
- XPU load tests require PyTorch with Intel XPU support (`import torch`).
