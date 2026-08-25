# Licensing

Nota is **dual-licensed**. One codebase produces two editions:

- **Free Edition** — [`AGPL-3.0-only`](AGPL-3.0-only.txt) (open source).
- **Pro Edition** — [`LicenseRef-Nota-Commercial`](LicenseRef-Nota-Commercial.txt)
  (proprietary; requires a signed agreement).

Every source file carries an SPDX identifier:

```
SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
```

You may use each file under **either** the AGPLv3 **or** the Nota commercial license.
Without a signed commercial agreement, the AGPLv3 is the only license available to you.

## Why AGPL and not GPL

Nota hosts VST3 and AU plugins through [JUCE](https://juce.com), which is dual-licensed
under **AGPLv3** or a paid commercial license. JUCE is statically linked into the
`nota_pluginhost` module, so the combined Free build inherits AGPLv3 terms — including
the §13 network-use clause. Declaring GPL-3.0 while linking AGPL code would be a license
mismatch, so the whole project is AGPL.

The core audio engine stays deliberately JUCE-free (see `ARCHITECTURE.md`); JUCE is
confined to the plugin-hosting module.

## Files

| File | What it is |
|---|---|
| [`AGPL-3.0-only.txt`](AGPL-3.0-only.txt) | Full GNU AGPLv3 text (verbatim from the FSF) |
| [`LicenseRef-Nota-Commercial.txt`](LicenseRef-Nota-Commercial.txt) | Commercial license — **DRAFT, needs legal review** |
| [`third-party.md`](third-party.md) | Register of every dependency and its license |

The repository root also carries a copy of the AGPLv3 as `LICENSE`, so hosting platforms
detect the license correctly.

## Before shipping a Pro build

The Pro edition may **not** be distributed until:

1. A **JUCE commercial license** is purchased (JUCE's AGPL terms otherwise apply).
2. The commercial license draft is **reviewed by a lawyer** and its version bumped
   out of draft.
3. Every ⚠️ row in [`third-party.md`](third-party.md) is resolved.

## Contributions

Contributions are accepted under the same dual license — see `CONTRIBUTING.md`.

## Contact

Commercial licensing: khek@ambertape.ru
