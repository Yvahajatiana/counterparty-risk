// Minimal SPDX license expression evaluator (no dependency).
// Grammar (SPDX spec, annex D), operators are case-insensitive:
//   or-expr  := and-expr ( "OR" and-expr )*
//   and-expr := atom ( "AND" atom )*
//   atom     := "(" or-expr ")" | license-id [ "WITH" exception-id ]
// AND binds tighter than OR, as in the specification.

const OPERATORS = new Set(['AND', 'OR', 'WITH']);

function tokenize(expression) {
  const tokens = expression.match(/\(|\)|[^\s()]+/g) ?? [];
  return tokens.map((value) => {
    const upper = value.toUpperCase();
    return OPERATORS.has(upper)
      ? { type: upper }
      : value === '(' || value === ')'
        ? { type: value }
        : { type: 'ID', value };
  });
}

/** Parses an SPDX expression into an AST. Throws on any syntax error. */
export function parseSpdx(expression) {
  if (typeof expression !== 'string' || expression.trim() === '') {
    throw new SyntaxError('Empty license expression');
  }
  const tokens = tokenize(expression);
  let position = 0;
  const peek = () => tokens[position];
  const next = () => tokens[position++];

  function parseOr() {
    const branches = [parseAnd()];
    while (peek()?.type === 'OR') {
      next();
      branches.push(parseAnd());
    }
    return branches.length === 1 ? branches[0] : { type: 'OR', branches };
  }

  function parseAnd() {
    const branches = [parseAtom()];
    while (peek()?.type === 'AND') {
      next();
      branches.push(parseAtom());
    }
    return branches.length === 1 ? branches[0] : { type: 'AND', branches };
  }

  function parseAtom() {
    const token = next();
    if (!token) throw new SyntaxError(`Unexpected end of expression: "${expression}"`);
    if (token.type === '(') {
      const inner = parseOr();
      if (next()?.type !== ')') throw new SyntaxError(`Missing ")" in "${expression}"`);
      return inner;
    }
    if (token.type !== 'ID') throw new SyntaxError(`Unexpected "${token.type}" in "${expression}"`);
    const node = { type: 'LICENSE', id: token.value };
    if (peek()?.type === 'WITH') {
      next();
      const exception = next();
      if (exception?.type !== 'ID')
        throw new SyntaxError(`Missing exception after WITH in "${expression}"`);
      node.exception = exception.value;
    }
    return node;
  }

  const ast = parseOr();
  if (position !== tokens.length)
    throw new SyntaxError(`Unexpected trailing tokens in "${expression}"`);
  return ast;
}

function evaluate(node, allowed) {
  switch (node.type) {
    case 'OR':
      return node.branches.some((branch) => evaluate(branch, allowed));
    case 'AND':
      return node.branches.every((branch) => evaluate(branch, allowed));
    default:
      // An exception (WITH) only grants extra permissions: the base license decides.
      return allowed.has(node.id);
  }
}

/**
 * True when the expression can be satisfied with allowed licenses only:
 * OR needs at least one allowed branch, AND needs every branch allowed.
 * Invalid or non-SPDX values (e.g. a URL) are rejected.
 */
export function isSpdxAllowed(expression, allowed) {
  try {
    return evaluate(parseSpdx(expression), allowed);
  } catch {
    return false;
  }
}
