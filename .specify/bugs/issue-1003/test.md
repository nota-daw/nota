# Bug Verification: WAV alias

- **Slug**: issue-1003
- **Tested**: 2026-11-25
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: partial

The smoke test opened and decoded `.WAVE` and rejected malformed `.wave`; the full macOS build passed. Full desktop drag and browser preview were not exercised. Repeat the complete suite on the final tree.
