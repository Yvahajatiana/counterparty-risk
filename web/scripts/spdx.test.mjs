// Unit tests for the SPDX evaluator, run with the Node.js built-in test runner: npm run test:scripts
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { isSpdxAllowed, parseSpdx } from './spdx.mjs';

const ALLOWED = new Set(['MIT', 'ISC', 'Apache-2.0', 'BSD-3-Clause']);
const allowed = (expression) => isSpdxAllowed(expression, ALLOWED);

describe('isSpdxAllowed', () => {
  it('accepts a single allowed identifier', () => {
    assert.equal(allowed('MIT'), true);
  });

  it('rejects a single forbidden identifier', () => {
    assert.equal(allowed('GPL-3.0-only'), false);
  });

  it('accepts OR when at least one branch is allowed', () => {
    assert.equal(allowed('MIT OR GPL-3.0-only'), true);
    assert.equal(allowed('GPL-3.0-only OR Apache-2.0'), true);
  });

  it('rejects OR when no branch is allowed', () => {
    assert.equal(allowed('GPL-3.0-only OR AGPL-3.0-only'), false);
  });

  it('rejects AND when one branch is forbidden (review case)', () => {
    assert.equal(allowed('GPL-3.0-only AND (MIT OR ISC)'), false);
    assert.equal(allowed('(MIT OR ISC) AND GPL-3.0-only'), false);
  });

  it('accepts AND when every branch is allowed', () => {
    assert.equal(allowed('MIT AND (ISC OR GPL-3.0-only)'), true);
    assert.equal(allowed('(MIT AND BSD-3-Clause)'), true);
  });

  it('gives AND precedence over OR', () => {
    // Parsed as MIT OR (GPL-3.0-only AND ISC): satisfiable with MIT.
    assert.equal(allowed('MIT OR GPL-3.0-only AND ISC'), true);
    // Parsed as (GPL-3.0-only AND MIT) OR AGPL-3.0-only: not satisfiable.
    assert.equal(allowed('GPL-3.0-only AND MIT OR AGPL-3.0-only'), false);
  });

  it('evaluates WITH on the base license', () => {
    assert.equal(allowed('Apache-2.0 WITH LLVM-exception'), true);
    assert.equal(allowed('GPL-2.0-only WITH Classpath-exception-2.0'), false);
    assert.equal(allowed('GPL-2.0-only WITH Classpath-exception-2.0 OR MIT'), true);
  });

  it('accepts lowercase operators', () => {
    assert.equal(allowed('mit or gpl-3.0-only'), false); // identifiers stay case-sensitive
    assert.equal(allowed('MIT or GPL-3.0-only'), true);
  });

  it('rejects invalid or non-SPDX values', () => {
    for (const value of [
      '',
      '   ',
      undefined,
      null,
      'MIT OR',
      '(MIT',
      'MIT)',
      'AND MIT',
      'MIT ISC',
      'MIT WITH',
      'https://www.highcharts.com/license',
    ]) {
      assert.equal(allowed(value), false, `expected rejection of ${JSON.stringify(value)}`);
    }
  });
});

describe('parseSpdx', () => {
  it('builds the expected tree', () => {
    assert.deepEqual(parseSpdx('GPL-3.0-only AND (MIT OR ISC)'), {
      type: 'AND',
      branches: [
        { type: 'LICENSE', id: 'GPL-3.0-only' },
        {
          type: 'OR',
          branches: [
            { type: 'LICENSE', id: 'MIT' },
            { type: 'LICENSE', id: 'ISC' },
          ],
        },
      ],
    });
  });
});
