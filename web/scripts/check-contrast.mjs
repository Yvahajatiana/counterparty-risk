// WCAG 2.1 AA contrast gate for the design tokens (light and dark themes).
// Parses the hex literals of src/styles/_tokens.scss and src/styles/_dark.scss.
//   - text tokens on every surface: >= 4.5:1 (SC 1.4.3)
//   - status and chart series tokens on every surface: >= 3:1 (SC 1.4.11, non-text contrast)
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const STYLES = resolve(import.meta.dirname, '..', 'src', 'styles');

async function readTokens(file) {
  const source = await readFile(resolve(STYLES, file), 'utf8');
  const tokens = {};
  for (const [, name, hex] of source.matchAll(/--(cr-[\w-]+):\s*(#[0-9a-fA-F]{6})\b/g)) {
    tokens[name] = hex;
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
    fg: ['cr-color-text', 'cr-color-text-muted'],
    bg: ['cr-color-surface', 'cr-color-surface-alt'],
    min: 4.5,
  },
  { fg: ['cr-color-on-primary'], bg: ['cr-color-primary'], min: 4.5 },
  { fg: ['cr-color-on-topbar'], bg: ['cr-color-topbar'], min: 4.5 },
  {
    fg: [
      'cr-status-ok',
      'cr-status-warn',
      'cr-status-breach',
      ...[1, 2, 3, 4, 5, 6].map((i) => `cr-series-${i}`),
    ],
    bg: ['cr-color-surface', 'cr-color-surface-alt'],
    min: 3,
  },
];

const light = await readTokens('_tokens.scss');
const dark = { ...light, ...(await readTokens('_dark.scss')) };
let failures = 0;

for (const [theme, tokens] of Object.entries({ light, dark })) {
  for (const rule of RULES) {
    for (const fg of rule.fg) {
      for (const bg of rule.bg) {
        if (!tokens[fg] || !tokens[bg]) {
          console.error(`[${theme}] missing token ${tokens[fg] ? bg : fg}`);
          failures++;
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
