// CSV / formula injection guard (CWE-1236, SEC-009) for the client-side CSV fallback.

/** Characters that make a spreadsheet read a cell as a formula. */
const FORMULA_TRIGGER = /^[=+\-@\t\r]/;

/**
 * Text of a CSV cell. A string starting with = + - @ tab or carriage return is prefixed with a
 * single quote so that spreadsheets show it as text; numbers are exported as plain numbers.
 */
export function neutralizeCsvCell(value: unknown): string {
  if (value == null) {
    return '';
  }
  if (typeof value === 'number' || typeof value === 'boolean') {
    return String(value);
  }
  const text = String(value);
  return FORMULA_TRIGGER.test(text) ? `'${text}` : text;
}

/** Safe file name: letters, digits, dot, dash and underscore only. */
export function safeFileName(name: string): string {
  return name.replace(/[^A-Za-z0-9._-]/g, '_').slice(0, 120) || 'export';
}
