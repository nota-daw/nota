#!/usr/bin/env node
// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Renders a release's poster: the Nota-styled 16:9 image that goes with the release posts.
//
//   node scripts/release-poster.mjs <version> [--src <dir>] [--out <png>] [--browser <path>]
//                                   [--fresh-leds] [--no-render]
//
// In:  release-notes/<version>.poster.json — what the poster says and shows:
//        { "kind": "…" (optional, beside the logo), "subtitle": "…", "tags": ["…"],
//          "hero":  { "title": "…", "label": "…", "image": "<file in the src folder>" },
//          "leds":  { "title": "…", "label": "…",
//                     "items": [ { "device": "builtin:fx:reverb", "page": 1, "caption": "Reverb" } ] },
//          "footer": { "left": ["…"], "right": ["…"] } }
//      release-notes/poster/poster.html — the template (the look; shared by every release).
//      The hero image (a real screenshot, e.g. from nota-docs' shots harness) in the src folder.
// Out: <src> (default ../media/news/<version>-src): the template, fonts, logo, poster-data.js and
//      the LED frames; the PNG (default ../media/news/<version>.png), 1280 × 720 at 2×.
//
// The LED panels are the real frames Nota draws for Nota Remote: the smoke test's `--displays`
// dumps every built-in device page (NOTA_DISPLAY_DUMP), once per src folder unless --fresh-leds.
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const opt = (name) => { const i = argv.indexOf(name); return i >= 0 ? argv.splice(i, 2)[1] : undefined; };
const flag = (name) => { const i = argv.indexOf(name); if (i >= 0) argv.splice(i, 1); return i >= 0; };

const srcOpt = opt('--src'), outOpt = opt('--out'), browserOpt = opt('--browser');
const freshLeds = flag('--fresh-leds'), noRender = flag('--no-render');
const version = argv[0];
if (!version || !/^\d+\.\d+\.\d+$/.test(version)) {
  console.error('usage: node scripts/release-poster.mjs <X.Y.Z> [--src dir] [--out png] [--browser path] [--fresh-leds] [--no-render]');
  process.exit(2);
}
const fail = (msg) => { console.error(`error: ${msg}`); process.exit(1); };

const configFile = path.join(repo, 'release-notes', `${version}.poster.json`);
if (!fs.existsSync(configFile)) fail(`${path.relative(repo, configFile)} is missing — write what the poster says first`);
const config = JSON.parse(fs.readFileSync(configFile, 'utf8'));

const news = path.resolve(repo, '..', 'media', 'news');
const src = path.resolve(srcOpt ?? path.join(news, `${version}-src`));
const out = path.resolve(outOpt ?? path.join(news, `${version}.png`));
fs.mkdirSync(path.join(src, 'fonts'), { recursive: true });
fs.mkdirSync(path.dirname(out), { recursive: true });

// The template, Geist and the logo, fresh every run so the look always comes from the repo.
fs.copyFileSync(path.join(repo, 'release-notes', 'poster', 'poster.html'), path.join(src, 'poster.html'));
for (const w of ['Regular', 'Medium', 'SemiBold', 'Bold'])
  fs.copyFileSync(path.join(repo, 'assets', 'fonts', `Geist-${w}.ttf`), path.join(src, 'fonts', `Geist-${w}.ttf`));
fs.copyFileSync(path.join(repo, 'assets', 'icons', 'svg', 'nota.svg'), path.join(src, 'logo.svg'));

if (config.hero?.image && !fs.existsSync(path.join(src, config.hero.image)))
  fail(`the hero image ${config.hero.image} isn't in ${src} — shoot it first (see the release skill, P7)`);

// LED frames: dump every page once, then pick the ones the poster names.
const ledDir = path.join(src, 'led');
const items = config.leds?.items ?? [];
if (items.length && (freshLeds || !fs.existsSync(ledDir) || fs.readdirSync(ledDir).length === 0)) {
  fs.mkdirSync(ledDir, { recursive: true });
  console.log('dumping the LED frames (smoke test --displays)…');
  const run = spawnSync('dotnet', ['run', '--project', path.join(repo, 'tests', 'Nota.SmokeTest'), '-c', 'Release', '--nologo', '--', '--displays'],
    { cwd: repo, env: { ...process.env, NOTA_DISPLAY_DUMP: ledDir }, encoding: 'utf8' });
  if (run.status !== 0) fail(`the LED dump failed:\n${(run.stdout ?? '').split('\n').filter((l) => /FAIL|error/i.test(l)).join('\n')}`);
}
const leds = items.map((it) => {
  const stem = `${it.device.replaceAll(':', '_')}__${it.page ?? 1}_`;
  const file = fs.existsSync(ledDir) ? fs.readdirSync(ledDir).find((f) => f.startsWith(stem) && f.endsWith('.rle')) : undefined;
  if (!file) fail(`no LED frame for ${it.device} page ${it.page ?? 1} (looked for ${stem}*.rle in ${ledDir})`);
  return { caption: it.caption, page: it.pageName, rle: fs.readFileSync(path.join(ledDir, file), 'utf8').trim() };
});

const data = { ...config, version, leds: config.leds ? { ...config.leds, items: leds } : undefined };
fs.writeFileSync(path.join(src, 'poster-data.js'), `window.POSTER = ${JSON.stringify(data, null, 2)};\n`);
console.log(`poster source: ${src}`);
if (noRender) process.exit(0);

// Any Chromium renders it: Chrome, Chromium or Edge, headless, at 2×.
const candidates = [
  browserOpt, process.env.CHROME,
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  '/Applications/Chromium.app/Contents/MacOS/Chromium',
  '/usr/bin/google-chrome', '/usr/bin/chromium', '/usr/bin/chromium-browser',
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
].filter(Boolean);
const browser = candidates.find((b) => fs.existsSync(b));
if (!browser) fail('no Chrome, Chromium or Edge found — pass --browser <path> or set CHROME');

const profile = fs.mkdtempSync(path.join(src, '.profile-'));
const shot = spawnSync(browser, [
  '--headless=new', '--hide-scrollbars', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
  `--user-data-dir=${profile}`, '--allow-file-access-from-files', '--virtual-time-budget=4000',
  '--window-size=1280,720', '--force-device-scale-factor=2', `--screenshot=${out}`,
  pathToFileURL(path.join(src, 'poster.html')).href,
], { encoding: 'utf8' });
fs.rmSync(profile, { recursive: true, force: true });
if (!fs.existsSync(out) || shot.status !== 0) fail(`the browser didn't write ${out}\n${shot.stderr ?? ''}`);
console.log(`poster: ${out}`);
