import type { IncomingMessage, OutgoingHttpHeaders, ServerResponse } from 'node:http';
import { bearer, grantPaths, logoutPath, refreshPath } from '../protocol.ts';
import { answerUncached } from './answers.ts';
import { clearedCookie } from './cookies.ts';
import { apiOrigin, forwardedHeaders } from './relay.ts';
import { browserCredentials, sessionServer, type ServerSessions, type SessionRoute } from './router.ts';
import { sessionCookie, sessionStore, type Grant, type Renewal } from './store.ts';

export type { ServerSessions } from './router.ts';

export interface ServerSessionsOptions {
  readonly refreshAhead?: number;
  readonly now?: () => number;
}

interface LocalGrant extends Grant {
  readonly refreshToken: string;
}

const unrelayedAnswerHeaders = new Set(['connection', 'content-encoding', 'content-length', 'keep-alive', 'set-cookie', 'transfer-encoding']);

const unsentCallHeaders = new Set(['accept-encoding', 'content-length', 'host', ...browserCredentials]);

export function serverSessions(upstream: string, options: ServerSessionsOptions = {}): ServerSessions {
  const target = apiOrigin(upstream);

  function call(path: string, request: IncomingMessage, body: string, accessToken?: string): Promise<Response> {
    const headers = callHeaders(request);
    headers['content-type'] = 'application/json';

    if (accessToken !== undefined) {
      headers['authorization'] = bearer(accessToken);
    }

    return fetch(new URL(path, target), { method: 'POST', headers, body });
  }

  async function refresh(grant: LocalGrant, request: IncomingMessage): Promise<Renewal<LocalGrant>> {
    let answer: Response;

    try {
      answer = await call(refreshPath, request, JSON.stringify({ refreshToken: grant.refreshToken }));
    } catch {
      return 'kept';
    }

    if (answer.status >= 500) {
      return 'kept';
    }

    return (answer.ok ? readGrant(await readJson(answer)) : undefined) ?? 'ended';
  }

  const store = sessionStore(refresh, { refreshAhead: options.refreshAhead, now: options.now ?? Date.now });

  async function signIn(grantPath: string, request: IncomingMessage, response: ServerResponse): Promise<void> {
    const headers = callHeaders(request);
    const body = await readBody(request);
    let answer: Response;

    try {
      answer = await fetch(new URL(grantPath, target), { method: 'POST', headers, body });
    } catch {
      answerUncached(response, 502);
      return;
    }

    const answered = answerHeaders(answer);
    let text = await answer.text();
    const parsed = answer.ok ? parseJson(text) : undefined;
    const grant = readGrant(isRecord(parsed) && 'tokens' in parsed ? parsed['tokens'] : parsed);

    if (grant !== undefined) {
      answered['set-cookie'] = store.open(request, grant);
      text = JSON.stringify(withoutSecrets(parsed));
    }

    answered['content-length'] = Buffer.byteLength(text);
    response.writeHead(answer.status, answered);
    response.end(text);
  }

  async function signOut(request: IncomingMessage, response: ServerResponse): Promise<void> {
    await readBody(request);
    const grant = store.end(request);

    if (grant !== undefined) {
      await call(logoutPath, request, JSON.stringify({ refreshToken: grant.refreshToken }), grant.accessToken).catch(() => undefined);
    }

    answerUncached(response, 204, { 'set-cookie': clearedCookie(sessionCookie) });
  }

  const routes: Record<string, SessionRoute> = {
    [logoutPath]: { method: 'POST', handle: signOut },
  };

  for (const grantPath of grantPaths) {
    routes[grantPath] = { method: 'POST', handle: (request, response) => signIn(grantPath, request, response) };
  }

  return sessionServer(target, store, routes);
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

function readGrant(value: unknown): LocalGrant | undefined {
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
    expiresAt: Date.parse(refreshTokenExpiresAt),
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
