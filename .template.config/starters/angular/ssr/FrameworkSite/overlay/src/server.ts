import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import { relayToApi } from '@app-template/api-client/server';
import express from 'express';
import { createHash, randomBytes } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

const browserDistFolder = join(import.meta.dirname, '../browser');
const pageTemplate = join(import.meta.dirname, 'index.server.html');
const fingerprinted = /-(?=[A-Za-z0-9_-]*[A-Z0-9])[A-Za-z0-9_-]{8,}\.[a-z0-9]+$/;

const app = express();
const angularApp = new AngularNodeAppEngine();

const templateScriptHashes = existsSync(pageTemplate)
  ? [...readFileSync(pageTemplate, 'utf8').matchAll(/<script\b(?![^>]*\ssrc\s*=)[^>]*>([\s\S]*?)<\/script\s*>/gi)].map(
      ([, script]) => `'sha256-${createHash('sha256').update(script ?? '', 'utf8').digest('base64')}'`,
    )
  : [];

const contentSecurityPolicy = (nonce: string) =>
  [
    "default-src 'self'",
    "base-uri 'self'",
    "object-src 'none'",
    "form-action 'self'",
    "frame-ancestors 'none'",
    "img-src 'self' data:",
    "style-src 'self' 'unsafe-inline'",
    ["script-src 'self'", ...templateScriptHashes, `'nonce-${nonce}'`].join(' '),
    "connect-src 'self'",
  ].join('; ');

app.disable('x-powered-by');

app.use(relayToApi(process.env['API_UPSTREAM'] ?? 'http://localhost:5187'));

app.use((_request, response, next) => {
  response.setHeader('X-Content-Type-Options', 'nosniff');
  response.setHeader('Referrer-Policy', 'no-referrer');
  response.setHeader('Cross-Origin-Opener-Policy', 'same-origin');
  next();
});

app.use(
  express.static(browserDistFolder, {
    index: false,
    redirect: false,
    cacheControl: false,
    setHeaders: (response, path) => {
      response.setHeader('Cache-Control', fingerprinted.test(path) ? 'public, max-age=31536000, immutable' : 'no-cache');
    },
  }),
);

app.use((request, response, next) => {
  const nonce = randomBytes(16).toString('base64');

  response.setHeader('Content-Security-Policy', contentSecurityPolicy(nonce));
  response.setHeader('Cache-Control', 'no-store');

  angularApp
    .handle(request, { nonce })
    .then((answer) => (answer ? writeResponseToNodeResponse(answer, response) : next()))
    .catch(next);
});

if (isMainModule(import.meta.url) || process.env['pm_id']) {
  const port = process.env['PORT'] || 4000;
  app.listen(port, (error) => {
    if (error) {
      throw error;
    }

    console.log(`Node Express server listening on http://localhost:${port}`);
  });
}

export const reqHandler = createNodeRequestHandler(app);
