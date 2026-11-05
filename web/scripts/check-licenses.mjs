// License gate for the npm dependency tree, based on package-lock.json (no extra dependency).
// Fails when a package has a license outside the allow-list and without a documented exception.
// Exceptions must be justified in THIRD_PARTY_LICENSES.md and ADR-001 (#3).
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { isSpdxAllowed } from './spdx.mjs';

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

// SPDX exceptions accepted after a WITH operator. Anything else is refused, because an exception
// can restrict the base license (e.g. Commons-Clause).
const ALLOWED_EXCEPTIONS = new Set(['LLVM-exception']);

// Weak copyleft accepted only for lightningcss, a build-time tool of Tailwind CSS that is never
// shipped in the bundle. It does not extend to other development dependencies.
const BUILD_TOOL_PACKAGES = /^lightningcss(-.+)?$/;
const ALLOWED_FOR_BUILD_TOOLS = new Set([...ALLOWED, 'MPL-2.0']);

// Documented exceptions: package name -> reason (see THIRD_PARTY_LICENSES.md).
const EXCEPTIONS = new Map([
  ['highcharts', 'Highsoft proprietary license: free for non-commercial use only (ADR-001, #3)'],
]);

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
  // Full SPDX evaluation: OR needs one allowed branch, AND needs all of them (see spdx.mjs).
  const allowed = meta.dev && BUILD_TOOL_PACKAGES.test(name) ? ALLOWED_FOR_BUILD_TOOLS : ALLOWED;
  if (!isSpdxAllowed(license, allowed, ALLOWED_EXCEPTIONS)) {
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
