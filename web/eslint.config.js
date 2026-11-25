// @ts-check
const eslint = require('@eslint/js');
const { defineConfig } = require('eslint/config');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

// Security rules (Secure by Design, #12): forbid DOM XSS sinks and sanitizer bypasses.
// Angular sanitization and Trusted Types (`trusted-types angular`) must never be circumvented.
// Each sink is matched in dot form (el.innerHTML) and in computed form (el['innerHTML'],
// el[`innerHTML`]), whatever the object. A computed key held in a variable cannot be detected.
const forbidMember = (pattern, message) =>
  ['property.name', 'property.value', 'property.quasis.0.value.raw'].map((path) => ({
    selector: `MemberExpression[${path}=/${pattern}/]`,
    message,
  }));

const STORAGE_MESSAGE =
  'Web storage is restricted to UI preferences in core/theme. Never store tokens or business data.';

const FORBIDDEN_DOM_SYNTAX = [
  ...forbidMember(
    '^(innerHTML|outerHTML)$',
    'innerHTML/outerHTML are forbidden (XSS sink). Use Angular templates and bindings.',
  ),
  ...forbidMember('^insertAdjacentHTML$', 'insertAdjacentHTML is forbidden (XSS sink).'),
  ...forbidMember(
    '^bypassSecurityTrust',
    'DomSanitizer.bypassSecurityTrust* is forbidden: it disables Angular sanitization.',
  ),
  // Any object: this.document.write(...) is the inject(DOCUMENT) idiom.
  ...['callee.property.name', 'callee.property.value'].map((path) => ({
    selector: `CallExpression[${path}=/^write(ln)?$/]`,
    message: 'document.write is forbidden.',
  })),
  // Renderer2.setProperty/setAttribute(el, name, value) and Element.setAttribute(name, value).
  {
    selector:
      'CallExpression[callee.property.name="setProperty"][arguments.1.value=/^(innerHTML|outerHTML|srcdoc)$/]',
    message: 'Renderer2.setProperty on innerHTML/outerHTML/srcdoc is forbidden (XSS sink).',
  },
  {
    selector: 'CallExpression[callee.property.name="setAttribute"][arguments.1.value="srcdoc"]',
    message: 'Setting the srcdoc attribute is forbidden (XSS sink).',
  },
  {
    selector: 'CallExpression[callee.property.name="setAttribute"][arguments.0.value="srcdoc"]',
    message: 'Setting the srcdoc attribute is forbidden (XSS sink).',
  },
];

const FORBIDDEN_STORAGE_SYNTAX = forbidMember('^(localStorage|sessionStorage)$', STORAGE_MESSAGE);

// Chart rules of the EDB (#14): no pie or donut chart, a single Y axis per chart.
const PIE_MESSAGE = 'Pie and donut charts are forbidden (EDB): use bars or a 100 % stacked bar.';
const PIE_TYPES = '/^(pie|variablepie|variable-pie)$/';
const FORBIDDEN_CHART_SYNTAX = [
  { selector: `Property[key.name='type'][value.value=${PIE_TYPES}]`, message: PIE_MESSAGE },
  { selector: `Property[key.value='type'][value.value=${PIE_TYPES}]`, message: PIE_MESSAGE },
  { selector: 'ImportDeclaration[source.value=/variable-pie/]', message: PIE_MESSAGE },
  { selector: 'ImportExpression[source.value=/variable-pie/]', message: PIE_MESSAGE },
  {
    selector: "Property[key.name='yAxis'] > ArrayExpression.value",
    message: 'A chart has a single Y axis (EDB): compare different units with rebase100.',
  },
];

// Licences and wrappers (ADR-001 §6, conventions §5): Highcharts only behind the ChartAdapter,
// AG Grid only through the grid wrapper, never AG Grid Enterprise.
const ENTERPRISE_IMPORTS = {
  regex: '^(ag-grid-enterprise|@ag-grid-enterprise/)',
  message: 'AG Grid Enterprise is forbidden (commercial licence): AG Grid Community only.',
};
const HIGHCHARTS_IMPORTS = {
  regex: '^highcharts(-angular)?(/|$)',
  message: 'Charts go through the ChartAdapter: use the components of shared/charts (ADR-001).',
};
const AG_GRID_IMPORTS = {
  regex: '^ag-grid-(community|angular)(/|$)',
  message: 'Tables go through the AG Grid wrapper: use app-data-grid from shared/grid.',
};
const restrictedImports = (...patterns) => ['error', { patterns }];

const FORBIDDEN_GLOBALS = ['localStorage', 'sessionStorage'].map((name) => ({
  name,
  message: STORAGE_MESSAGE,
}));

// Template equivalents: [innerHTML], [outerHTML] and their attribute forms.
const FORBIDDEN_TEMPLATE_SYNTAX = [
  {
    selector: 'BoundAttribute[name=/^(innerHTML|outerHTML|srcdoc)$/i]',
    message: 'Binding to innerHTML/outerHTML/srcdoc is forbidden (XSS sink).',
  },
  {
    selector: 'TextAttribute[name=/^(innerHTML|outerHTML|srcdoc)$/i]',
    message: 'innerHTML/outerHTML/srcdoc attributes are forbidden (XSS sink).',
  },
];

module.exports = defineConfig([
  {
    ignores: ['dist/**', 'coverage/**', '.angular/**', 'node_modules/**'],
  },
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      tseslint.configs.recommended,
      tseslint.configs.stylistic,
      angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        {
          type: 'attribute',
          prefix: 'app',
          style: 'camelCase',
        },
      ],
      '@angular-eslint/component-selector': [
        'error',
        {
          type: 'element',
          prefix: 'app',
          style: 'kebab-case',
        },
      ],
      '@angular-eslint/prefer-on-push-component-change-detection': 'error',
      '@angular-eslint/prefer-signals': 'error',
      'no-eval': 'error',
      'no-implied-eval': 'error',
      'no-new-func': 'error',
      'no-script-url': 'error',
      'no-restricted-syntax': [
        'error',
        ...FORBIDDEN_DOM_SYNTAX,
        ...FORBIDDEN_STORAGE_SYNTAX,
        ...FORBIDDEN_CHART_SYNTAX,
      ],
      'no-restricted-globals': ['error', ...FORBIDDEN_GLOBALS],
      'no-restricted-imports': restrictedImports(
        ENTERPRISE_IMPORTS,
        HIGHCHARTS_IMPORTS,
        AG_GRID_IMPORTS,
      ),
    },
  },
  {
    // The Highcharts implementation of the ChartAdapter, and its lazy loading.
    files: ['src/app/shared/charts/highcharts/**/*.ts', 'src/app/app.config.ts'],
    rules: { 'no-restricted-imports': restrictedImports(ENTERPRISE_IMPORTS, AG_GRID_IMPORTS) },
  },
  {
    // The AG Grid Community wrapper.
    files: ['src/app/shared/grid/**/*.ts'],
    rules: { 'no-restricted-imports': restrictedImports(ENTERPRISE_IMPORTS, HIGHCHARTS_IMPORTS) },
  },
  {
    // The only place allowed to use web storage, for UI preferences (theme).
    files: ['src/app/core/theme/theme.service.ts', 'src/app/core/theme/theme.service.spec.ts'],
    rules: {
      'no-restricted-syntax': ['error', ...FORBIDDEN_DOM_SYNTAX, ...FORBIDDEN_CHART_SYNTAX],
      'no-restricted-globals': 'off',
    },
  },
  {
    files: ['**/*.html'],
    extends: [angular.configs.templateRecommended, angular.configs.templateAccessibility],
    rules: {
      '@angular-eslint/template/no-inline-styles': [
        'error',
        { allowNgStyle: false, allowBindToStyle: false },
      ],
      'no-restricted-syntax': ['error', ...FORBIDDEN_TEMPLATE_SYNTAX],
    },
  },
]);
