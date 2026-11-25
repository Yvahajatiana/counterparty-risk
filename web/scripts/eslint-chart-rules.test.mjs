// Checks that the lint rules of #14 block what the chart and grid kit forbids (EDB, ADR-001).
// Run with: npm run test:scripts
import assert from 'node:assert/strict';
import { resolve } from 'node:path';
import { test } from 'node:test';
import { ESLint } from 'eslint';

const cwd = resolve(import.meta.dirname, '..');
const eslint = new ESLint({ cwd });

async function messages(code, file = 'src/app/features/sample/sample.ts') {
  const [result] = await eslint.lintText(code, { filePath: resolve(cwd, file) });
  return result.messages.map((m) => m.message);
}

test('a pie or donut chart fails the lint', async () => {
  for (const code of [
    "export const o = { chart: { type: 'pie' } };",
    "export const o = { series: [{ 'type': 'variablepie' }] };",
    "import 'highcharts/esm/modules/variable-pie';",
  ]) {
    const found = await messages(code);
    assert.ok(
      found.some((m) => m.includes('Pie and donut charts are forbidden')),
      `${code} -> ${found.join(' | ')}`,
    );
  }
});

test('a second Y axis fails the lint', async () => {
  const found = await messages('export const o = { yAxis: [{}, { opposite: true }] };');
  assert.ok(
    found.some((m) => m.includes('single Y axis')),
    found.join(' | '),
  );
});

test('Highcharts and AG Grid are only used through the wrappers', async () => {
  const charts = await messages(
    "import type { Options } from 'highcharts';\nexport type O = Options;",
  );
  assert.ok(
    charts.some((m) => m.includes('ChartAdapter')),
    charts.join(' | '),
  );

  const grid = await messages(
    "import { AgGridAngular } from 'ag-grid-angular';\nexport const g = AgGridAngular;",
  );
  assert.ok(
    grid.some((m) => m.includes('AG Grid wrapper')),
    grid.join(' | '),
  );

  const inAdapter = await messages(
    "import type { Options } from 'highcharts';\nexport type O = Options;",
    'src/app/shared/charts/highcharts/sample.ts',
  );
  assert.deepEqual(inAdapter, []);
});

test('AG Grid Enterprise is forbidden everywhere, wrapper included', async () => {
  const found = await messages("import 'ag-grid-enterprise';", 'src/app/shared/grid/sample.ts');
  assert.ok(
    found.some((m) => m.includes('AG Grid Enterprise is forbidden')),
    found.join(' | '),
  );
});
