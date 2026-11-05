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
      'no-restricted-syntax': ['error', ...FORBIDDEN_DOM_SYNTAX, ...FORBIDDEN_STORAGE_SYNTAX],
      'no-restricted-globals': ['error', ...FORBIDDEN_GLOBALS],
    },
  },
  {
    // The only place allowed to use web storage, for UI preferences (theme).
    files: ['src/app/core/theme/theme.service.ts', 'src/app/core/theme/theme.service.spec.ts'],
    rules: {
      'no-restricted-syntax': ['error', ...FORBIDDEN_DOM_SYNTAX],
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
