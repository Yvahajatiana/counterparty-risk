// @ts-check
const eslint = require('@eslint/js');
const { defineConfig } = require('eslint/config');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

// Security rules (Secure by Design, #12): forbid DOM XSS sinks and sanitizer bypasses.
// Angular sanitization and Trusted Types (`trusted-types angular`) must never be circumvented.
const FORBIDDEN_TS_SYNTAX = [
  {
    selector: 'MemberExpression[property.name=/^(innerHTML|outerHTML)$/]',
    message: 'innerHTML/outerHTML are forbidden (XSS sink). Use Angular templates and bindings.',
  },
  {
    selector: 'CallExpression[callee.property.name="insertAdjacentHTML"]',
    message: 'insertAdjacentHTML is forbidden (XSS sink).',
  },
  {
    selector: 'MemberExpression[property.name=/^bypassSecurityTrust/]',
    message: 'DomSanitizer.bypassSecurityTrust* is forbidden: it disables Angular sanitization.',
  },
  {
    selector: 'CallExpression[callee.object.name="document"][callee.property.name=/^write(ln)?$/]',
    message: 'document.write is forbidden.',
  },
  {
    selector: 'MemberExpression[property.name=/^(localStorage|sessionStorage)$/]',
    message:
      'Web storage is restricted to UI preferences in core/theme. Never store tokens or business data.',
  },
];

const FORBIDDEN_GLOBALS = ['localStorage', 'sessionStorage'].map((name) => ({
  name,
  message:
    'Web storage is restricted to UI preferences in core/theme. Never store tokens or business data.',
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
      'no-restricted-syntax': ['error', ...FORBIDDEN_TS_SYNTAX],
      'no-restricted-globals': ['error', ...FORBIDDEN_GLOBALS],
    },
  },
  {
    // The only place allowed to use web storage, for UI preferences (theme).
    files: ['src/app/core/theme/theme.service.ts', 'src/app/core/theme/theme.service.spec.ts'],
    rules: {
      'no-restricted-syntax': [
        'error',
        ...FORBIDDEN_TS_SYNTAX.filter((rule) => !rule.selector.includes('localStorage')),
      ],
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
