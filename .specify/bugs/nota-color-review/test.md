# Verification: Nota Color review

- **Slug**: nota-color-review
- **Result**: partial

The native engine rebuilt. The focused `--preset-state` and `--color` suites passed, including the new FFT, Gate and Pre preset regression. Two full smoke runs failed the overloaded render-pool timing checks (164/4125 late blocks, burst 22; then 42/4125, burst 12). The UI keyboard undo and midpoint cases were checked against the implementation and engine rounding, but were not exercised interactively.
