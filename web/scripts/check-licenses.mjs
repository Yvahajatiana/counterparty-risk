// License gate for the npm dependency tree, based on package-lock.json (no extra dependency).
// Fails when a package has a license outside the allow-list and without a documented exception.
// Exceptions must be justified in THIRD_PARTY_LICENSES.md and ADR-001 (#3).
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const ROOT = resolve(import.meta.dirname, '..');

const ALLOWED = new Set([
  'MIT',
  'MIT-0',
  'ISC',
  'BSD-2-Clause',
  'BSD-3-Clause',
  '0BSD',
  'Apache-2.0',
  'OFL-1.1',
  'CC0-1.0',
  'CC-BY-4.0',
  'BlueOak-1.0.0',
  'Python-2.0',
  'Unlicense',
]);

// Weak copyleft accepted only for build-time tools that are never shipped in the bundle
// (e.g. lightningcss, used by Tailwind CSS at build time).
const ALLOWED_DEV_ONLY = new Set(['MPL-2.0']);

// Documented exceptions: package name -> reason (see THIRD_PARTY_LICENSES.md).
const EXCEPTIONS = new Map([
  ['highcharts', 'Highsoft proprietary license: free for non-commercial use only (ADR-001, #3)'],
]);

/** Accepts SPDX expressions made only of allowed identifiers ("MIT OR Apache-2.0", "(MIT AND CC-BY-4.0)"). */
function isAllowed(license) {
  if (typeof license !== 'string' || license.length === 0) return false;
  const ids = license
    .replace(/[()]/g, ' ')
    .split(/\s+(?:OR|AND)\s+|\s+/)
    .filter(Boolean);
  if (/\sOR\s/.test(license)) return ids.some((id) => ALLOWED.has(id));
  return ids.every((id) => ALLOWED.has(id));
}

const lock = JSON.parse(await readFile(resolve(ROOT, 'package-lock.json'), 'utf8'));
const violations = [];
const exceptionsUsed = [];

for (const [path, meta] of Object.entries(lock.packages ?? {})) {
  if (path === '' || meta.link) continue;
  const name = path.slice(path.lastIndexOf('node_modules/') + 'node_modules/'.length);
  const license = meta.license;
  if (EXCEPTIONS.has(name)) {
    exceptionsUsed.push(`${name}@${meta.version} (${license ?? 'n/a'}): ${EXCEPTIONS.get(name)}`);
    continue;
  }
  if (meta.dev && ALLOWED_DEV_ONLY.has(license)) continue;
  if (!isAllowed(license)) {
    violations.push(`${name}@${meta.version}: ${license ?? 'UNKNOWN'}${meta.dev ? ' (dev)' : ''}`);
  }
}

for (const line of exceptionsUsed) console.log(`exception ${line}`);
if (violations.length > 0) {
  console.error('Packages with a license outside the allow-list:');
  for (const line of violations) console.error(`  ${line}`);
  process.exit(1);
}
console.log(`License check passed (${Object.keys(lock.packages ?? {}).length - 1} packages).`);
