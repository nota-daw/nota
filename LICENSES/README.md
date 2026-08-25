# Licensing

Nota is licensed under [`AGPL-3.0-only`](AGPL-3.0-only.txt) (open source).

Every source file carries an SPDX identifier:

```
SPDX-License-Identifier: AGPL-3.0-only
```

## Why AGPL and not GPL

Nota hosts VST3 and AU plugins through [JUCE](https://juce.com), which is available
under **AGPLv3** (among other terms). JUCE is statically linked into the
`nota_pluginhost` module, so the combined build inherits AGPLv3 terms — including
the §13 network-use clause. Declaring GPL-3.0 while linking AGPL code would be a license
mismatch, so the whole project is AGPL.

The core audio engine stays deliberately JUCE-free (see `ARCHITECTURE.md`); JUCE is
confined to the plugin-hosting module.

## Files

| File | What it is |
|---|---|
| [`AGPL-3.0-only.txt`](AGPL-3.0-only.txt) | Full GNU AGPLv3 text (verbatim from the FSF) |
| [`third-party.md`](third-party.md) | Register of every dependency and its license |
| [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) | Copyright & attribution notices for bundled/linked third-party software |

The repository root also carries a copy of the AGPLv3 as `LICENSE`, so hosting platforms
detect the license correctly.

## Contributions

Contributions are accepted under the same license — see `CONTRIBUTING.md`.
