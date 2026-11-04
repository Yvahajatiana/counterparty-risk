// Local smoke-test server for the production build under the target Content-Security-Policy.
//
// It mimics what the BFF (#10) must do for every request of index.html:
//   1. generate a fresh random nonce,
//   2. replace CSP_NONCE_PLACEHOLDER in index.html with it,
//   3. send the strict CSP header (script hashes come from the autoCsp <meta> tag).
//
// Usage: npm run build && npm run serve:csp  (then open http://127.0.0.1:4300)
// Development tool only: binds to the loopback interface, never deploy it.
import { randomBytes } from 'node:crypto';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join, normalize, resolve, sep } from 'node:path';

const ROOT = resolve(import.meta.dirname, '..', 'dist', 'counterparty-risk-web', 'browser');
const PORT = Number(process.env['CSP_PORT'] ?? 4300);
const HOST = '127.0.0.1';
const NONCE_PLACEHOLDER = 'CSP_NONCE_PLACEHOLDER';

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.woff2': 'font/woff2',
  '.ico': 'image/x-icon',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.json': 'application/json',
};

const indexTemplate = await readFile(join(ROOT, 'index.html'), 'utf8');
const scriptHashes = [...indexTemplate.matchAll(/'sha256-[A-Za-z0-9+/=]+'/g)].map((m) => m[0]);

/** Target policy documented in web/README.md, to be enforced by the BFF (#10). */
export function buildCsp(nonce) {
  return [
    "default-src 'self'",
    `script-src 'self' ${scriptHashes.join(' ')}`,
    `style-src 'self' 'nonce-${nonce}'`,
    "font-src 'self'",
    "img-src 'self' data:",
    "connect-src 'self'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
    "require-trusted-types-for 'script'",
    'trusted-types angular',
  ].join('; ');
}

function sendIndex(res) {
  const nonce = randomBytes(16).toString('base64');
  const html = indexTemplate.replaceAll(NONCE_PLACEHOLDER, nonce);
  res.writeHead(200, {
    'Content-Type': MIME['.html'],
    'Content-Security-Policy': buildCsp(nonce),
    'X-Content-Type-Options': 'nosniff',
    'Cache-Control': 'no-store',
  });
  res.end(html);
}

const server = createServer(async (req, res) => {
  const url = new URL(req.url ?? '/', `http://${HOST}`);
  const filePath = normalize(join(ROOT, decodeURIComponent(url.pathname)));
  // Reject path traversal outside the build output.
  if (!filePath.startsWith(ROOT + sep)) {
    sendIndex(res);
    return;
  }
  const type = MIME[extname(filePath)];
  if (!type || extname(filePath) === '.html') {
    // SPA fallback: every route returns index.html with a fresh nonce.
    sendIndex(res);
    return;
  }
  try {
    const body = await readFile(filePath);
    res.writeHead(200, { 'Content-Type': type, 'X-Content-Type-Options': 'nosniff' });
    res.end(body);
  } catch {
    res.writeHead(404, { 'Content-Type': 'text/plain' });
    res.end('Not found');
  }
});

server.listen(PORT, HOST, () => {
  console.log(`Serving ${ROOT} with strict CSP on http://${HOST}:${PORT}`);
});
