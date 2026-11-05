// WCAG 2.1 AA contrast gate for the design tokens (light and dark themes).
// Parses the hex literals of src/styles/_tokens.scss and src/styles/_dark.scss.
//   - text, primary and breach tokens on every surface: >= 4.5:1 (SC 1.4.3)
//   - other status and chart series tokens on every surface: >= 3:1 (SC 1.4.11, non-text contrast)
// Every color token must be a #rrggbb literal and be declared in both themes, otherwise the check
// fails: a dark value is never replaced by its light fallback.
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const STYLES = resolve(import.meta.dirname, '..', 'src', 'styles');

const COLOR_TOKEN = /^cr-(color|status|series)-/;
const HEX_COLOR = /^#[0-9a-fA-F]{6}$/;
let failures = 0;

function fail(message) {
  console.error(message);
  failures++;
}

async function readTokens(file) {
  const source = await readFile(resolve(STYLES, file), 'utf8');
  const tokens = {};
  for (const [, name, rawValue] of source.matchAll(/--(cr-[\w-]+):\s*([^;]+);/g)) {
    const value = rawValue.trim();
    if (!COLOR_TOKEN.test(name)) continue;
    if (HEX_COLOR.test(value)) {
      tokens[name] = value;
    } else {
      fail(`[${file}] ${name} is not a #rrggbb literal: ${value}`);
    }
  }
  return tokens;
}

function luminance(hex) {
  const channels = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255);
  const [r, g, b] = channels.map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function ratio(a, b) {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

const RULES = [
  {
    fg: ['cr-color-text', 'cr-color-text-muted', 'cr-color-primary', 'cr-status-breach'],
    bg: ['cr-color-surface', 'cr-color-surface-alt'],
    min: 4.5,
  },
  { fg: ['cr-color-on-primary'], bg: ['cr-color-primary'], min: 4.5 },
  { fg: ['cr-color-on-topbar'], bg: ['cr-color-topbar'], min: 4.5 },
  {
    fg: ['cr-status-ok', 'cr-status-warn', ...[1, 2, 3, 4, 5, 6].map((i) => `cr-series-${i}`)],
    bg: ['cr-color-surface', 'cr-color-surface-alt'],
    min: 3,
  },
];

const light = await readTokens('_tokens.scss');
const dark = await readTokens('_dark.scss');

for (const name of new Set([...Object.keys(light), ...Object.keys(dark)])) {
  if (!light[name] || !dark[name]) {
    fail(`${name} must be declared with a valid value in both themes`);
  }
}

for (const [theme, tokens] of Object.entries({ light, dark })) {
  for (const rule of RULES) {
    for (const fg of rule.fg) {
      for (const bg of rule.bg) {
        if (!tokens[fg] || !tokens[bg]) {
          fail(`[${theme}] missing token ${tokens[fg] ? bg : fg}`);
          continue;
        }
        const value = ratio(tokens[fg], tokens[bg]);
        const ok = value >= rule.min;
        if (!ok) failures++;
        console.log(
          `${ok ? 'ok  ' : 'FAIL'} [${theme}] ${fg} on ${bg}: ${value.toFixed(2)}:1 (min ${rule.min})`,
        );
      }
    }
  }
}

if (failures > 0) {
  console.error(`${failures} contrast check(s) failed.`);
  process.exit(1);
}
console.log('All token contrasts meet WCAG 2.1 AA.');
