import { randomBytes } from 'node:crypto';
import type { IncomingMessage, OutgoingHttpHeaders, ServerResponse } from 'node:http';
import { logoutPath, refreshPath } from '../session.ts';
import { apiOrigin, forward, forwardedHeaders, isRelayedToApi, pathOf, type ApiRelay } from './relay.ts';

export const sessionCookie = '__Host-session';

export const csrfRefusalCode = 'session.csrfHeaderRequired';

export interface ServerSessionsOptions {
  readonly refreshAhead?: number;
  readonly now?: () => number;
}

export interface ServerSessions {
  readonly relay: ApiRelay;
  accessToken(request: IncomingMessage, response?: ServerResponse): Promise<string | undefined>;
}

interface Grant {
  readonly accessToken: string;
  readonly accessTokenExpiresAt: number;
  readonly refreshToken: string;
  readonly refreshTokenExpiresAt: number;
}

interface Session {
  grant: Grant;
  refreshing: Promise<void> | undefined;
}

interface Found {
  readonly session: Session | undefined;
  readonly cookie: string | undefined;
}

const grantPaths = new Set([
  '/api/v1/auth/login',
  '/api/v1/auth/login/two-factor',
  '/api/v1/auth/login/external',
  '/api/v1/auth/passkeys/sign-in',
]);

const safeMethods = new Set(['GET', 'HEAD']);

const browserCredentials = ['authorization', 'cookie', 'x-refresh-token-mode', 'x-csrf'];

const unrelayedAnswerHeaders = new Set(['connection', 'content-encoding', 'content-length', 'keep-alive', 'set-cookie', 'transfer-encoding']);

const unsentCallHeaders = new Set(['accept-encoding', 'content-length', 'host', ...browserCredentials]);

export function serverSessions(upstream: string, options: ServerSessionsOptions = {}): ServerSessions {
  const target = apiOrigin(upstream);
  const refreshAhead = options.refreshAhead ?? 30_000;
  const now = options.now ?? Date.now;
  const sessions = new Map<string, Session>();

  function cookieFor(id: string, grant: Grant): string {
    const seconds = Math.max(0, Math.floor((grant.refreshTokenExpiresAt - now()) / 1000));

    return `${sessionCookie}=${id}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=${String(seconds)}`;
  }

  function call(path: string, request: IncomingMessage, body: string, accessToken?: string): Promise<Response> {
    const headers = callHeaders(request);
    headers['content-type'] = 'application/json';

    if (accessToken !== undefined) {
      headers['authorization'] = `Bearer ${accessToken}`;
    }

    return fetch(new URL(path, target), { method: 'POST', headers, body });
  }

  async function refresh(id: string, session: Session, request: IncomingMessage): Promise<void> {
    let answer: Response;

    try {
      answer = await call(refreshPath, request, JSON.stringify({ refreshToken: session.grant.refreshToken }));
    } catch {
      return;
    }

    if (answer.status >= 500) {
      return;
    }

    const grant = answer.ok ? readGrant(await readJson(answer)) : undefined;

    if (grant === undefined) {
      sessions.delete(id);
      return;
    }

    session.grant = grant;
  }

  async function find(request: IncomingMessage): Promise<Found> {
    const id = readSessionId(request);

    if (id === undefined) {
      return { session: undefined, cookie: undefined };
    }

    const session = sessions.get(id);

    if (session === undefined) {
      return { session: undefined, cookie: clearedCookie() };
    }

    if (session.grant.accessTokenExpiresAt - now() > refreshAhead) {
      return { session, cookie: undefined };
    }

    const before = session.grant;
    session.refreshing ??= refresh(id, session, request).finally(() => {
      session.refreshing = undefined;
    });
    await session.refreshing;

    if (!sessions.has(id)) {
      return { session: undefined, cookie: clearedCookie() };
    }

    return { session, cookie: session.grant === before ? undefined : cookieFor(id, session.grant) };
  }

  async function signIn(request: IncomingMessage, response: ServerResponse): Promise<void> {
    const headers = callHeaders(request);
    const body = await readBody(request);
    let answer: Response;

    try {
      answer = await fetch(new URL(request.url ?? '/', target), { method: 'POST', headers, body });
    } catch {
      response.writeHead(502, { 'cache-control': 'no-store' });
      response.end();
      return;
    }

    const replaced = readSessionId(request);
    const answered = answerHeaders(answer);
    let text = await answer.text();
    const parsed = answer.ok ? parseJson(text) : undefined;
    const grant = readGrant(isRecord(parsed) && 'tokens' in parsed ? parsed['tokens'] : parsed);

    if (grant !== undefined) {
      const id = randomBytes(32).toString('base64url');

      if (replaced !== undefined) {
        sessions.delete(replaced);
      }

      for (const [stale, session] of sessions) {
        if (session.grant.refreshTokenExpiresAt <= now()) {
          sessions.delete(stale);
        }
      }

      sessions.set(id, { grant, refreshing: undefined });
      answered['set-cookie'] = cookieFor(id, grant);
      text = JSON.stringify(withoutSecrets(parsed));
    }

    answered['content-length'] = Buffer.byteLength(text);
    response.writeHead(answer.status, answered);
    response.end(text);
  }

  async function signOut(request: IncomingMessage, response: ServerResponse): Promise<void> {
    await readBody(request);
    const id = readSessionId(request);
    const session = id === undefined ? undefined : sessions.get(id);

    if (id !== undefined) {
      sessions.delete(id);
    }

    if (session !== undefined) {
      await call(logoutPath, request, JSON.stringify({ refreshToken: session.grant.refreshToken }), session.grant.accessToken).catch(
        () => undefined,
      );
    }

    response.writeHead(204, { 'set-cookie': clearedCookie(), 'cache-control': 'no-store' });
    response.end();
  }

  async function relayCall(request: IncomingMessage, response: ServerResponse): Promise<void> {
    const found = await find(request);
    const answer: OutgoingHttpHeaders = found.cookie === undefined ? {} : { 'set-cookie': found.cookie };
    const add: Record<string, string> = found.session === undefined ? {} : { authorization: `Bearer ${found.session.grant.accessToken}` };

    forward(target, request, response, { drop: browserCredentials, add, answer });
  }

  const relay: ApiRelay = (request, response, next) => {
    const url = request.url ?? '/';

    if (!isRelayedToApi(url)) {
      next();
      return;
    }

    const method = (request.method ?? 'GET').toUpperCase();

    if (!safeMethods.has(method) && request.headers['x-csrf'] !== '1') {
      request.resume();
      refuseWithoutCsrfHeader(response);
      return;
    }

    const path = pathOf(url);
    const handled =
      method === 'POST' && grantPaths.has(path)
        ? signIn(request, response)
        : method === 'POST' && path === logoutPath
          ? signOut(request, response)
          : relayCall(request, response);

    handled.catch(next);
  };

  return {
    relay,
    async accessToken(request, response) {
      const found = await find(request);

      if (found.cookie !== undefined && response !== undefined && !response.headersSent) {
        response.appendHeader('set-cookie', found.cookie);
      }

      return found.session?.grant.accessToken;
    },
  };
}

function refuseWithoutCsrfHeader(response: ServerResponse): void {
  const body = JSON.stringify({
    type: 'about:blank',
    title: 'Forbidden',
    status: 403,
    detail: 'A request that changes state carries the X-CSRF: 1 header.',
    code: csrfRefusalCode,
  });

  response.writeHead(403, {
    'content-type': 'application/problem+json',
    'content-length': Buffer.byteLength(body),
    'cache-control': 'no-store',
  });
  response.end(body);
}

function clearedCookie(): string {
  return `${sessionCookie}=; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=0`;
}

function readSessionId(request: IncomingMessage): string | undefined {
  for (const pair of (request.headers.cookie ?? '').split(';')) {
    const [name, value] = pair.trim().split('=', 2);

    if (name === sessionCookie && value !== undefined && value !== '') {
      return value;
    }
  }

  return undefined;
}

function callHeaders(request: IncomingMessage): Record<string, string> {
  return Object.fromEntries(
    Object.entries(forwardedHeaders(request))
      .filter(([name, value]) => !unsentCallHeaders.has(name) && value !== undefined)
      .map(([name, value]) => [name, Array.isArray(value) ? value.join(', ') : String(value)]),
  );
}

function answerHeaders(answer: Response): OutgoingHttpHeaders {
  const headers: OutgoingHttpHeaders = {};

  answer.headers.forEach((value, name) => {
    if (!unrelayedAnswerHeaders.has(name)) {
      headers[name] = value;
    }
  });

  return headers;
}

async function readBody(request: IncomingMessage): Promise<Uint8Array<ArrayBuffer>> {
  const chunks: Buffer[] = [];

  for await (const chunk of request) {
    chunks.push(chunk as Buffer);
  }

  return new Uint8Array(Buffer.concat(chunks));
}

async function readJson(answer: Response): Promise<unknown> {
  return parseJson(await answer.text());
}

function parseJson(text: string): unknown {
  try {
    return JSON.parse(text) as unknown;
  } catch {
    return undefined;
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function readGrant(value: unknown): Grant | undefined {
  if (!isRecord(value)) {
    return undefined;
  }

  const { accessToken, accessTokenExpiresAt, refreshToken, refreshTokenExpiresAt } = value;

  if (
    typeof accessToken !== 'string' ||
    accessToken === '' ||
    typeof refreshToken !== 'string' ||
    refreshToken === '' ||
    typeof accessTokenExpiresAt !== 'string' ||
    typeof refreshTokenExpiresAt !== 'string'
  ) {
    return undefined;
  }

  return {
    accessToken,
    accessTokenExpiresAt: Date.parse(accessTokenExpiresAt),
    refreshToken,
    refreshTokenExpiresAt: Date.parse(refreshTokenExpiresAt),
  };
}

function withoutSecrets(answer: unknown): unknown {
  if (!isRecord(answer)) {
    return answer;
  }

  if (isRecord(answer['tokens'])) {
    return { ...answer, tokens: withoutSecrets(answer['tokens']) };
  }

  return Object.fromEntries(Object.entries(answer).filter(([name]) => name !== 'accessToken' && name !== 'refreshToken'));
}
