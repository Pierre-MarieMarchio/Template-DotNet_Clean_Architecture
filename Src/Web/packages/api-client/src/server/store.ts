import { randomBytes } from 'node:crypto';
import type { IncomingMessage, OutgoingHttpHeaders, ServerResponse } from 'node:http';
import { forward } from './relay.ts';

export const sessionCookie = '__Host-session';

export const csrfRefusalCode = 'session.csrfHeaderRequired';

export interface Grant {
  readonly accessToken: string;
  readonly accessTokenExpiresAt: number;
  readonly expiresAt: number;
}

export type Renewal<G extends Grant> = G | 'ended' | 'kept';

export type Renew<G extends Grant> = (grant: G, request: IncomingMessage) => Promise<Renewal<G>>;

export interface Found<G extends Grant> {
  readonly grant: G | undefined;
  readonly cookie: string | undefined;
}

export interface SessionStore<G extends Grant> {
  open(request: IncomingMessage, grant: G): string;
  find(request: IncomingMessage): Promise<Found<G>>;
  end(request: IncomingMessage): G | undefined;
}

export interface SessionStoreOptions {
  readonly refreshAhead: number;
  readonly now: () => number;
}

interface Session<G extends Grant> {
  grant: G;
  renewing: Promise<void> | undefined;
}

export const safeMethods = new Set(['GET', 'HEAD']);

export const browserCredentials = ['authorization', 'cookie', 'x-refresh-token-mode', 'x-csrf'];

export function sessionStore<G extends Grant>(renew: Renew<G>, options: SessionStoreOptions): SessionStore<G> {
  const { refreshAhead, now } = options;
  const sessions = new Map<string, Session<G>>();

  function cookieFor(id: string, grant: G): string {
    const seconds = Math.max(0, Math.floor((grant.expiresAt - now()) / 1000));

    return `${sessionCookie}=${id}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=${String(seconds)}`;
  }

  async function renewed(id: string, session: Session<G>, request: IncomingMessage): Promise<void> {
    const renewal = await renew(session.grant, request);

    if (renewal === 'ended') {
      sessions.delete(id);
    } else if (renewal !== 'kept') {
      session.grant = renewal;
    }
  }

  return {
    open(request, grant) {
      const replaced = readCookie(request, sessionCookie);
      const id = randomBytes(32).toString('base64url');

      if (replaced !== undefined) {
        sessions.delete(replaced);
      }

      for (const [stale, session] of sessions) {
        if (session.grant.expiresAt <= now()) {
          sessions.delete(stale);
        }
      }

      sessions.set(id, { grant, renewing: undefined });

      return cookieFor(id, grant);
    },

    async find(request) {
      const id = readCookie(request, sessionCookie);

      if (id === undefined) {
        return { grant: undefined, cookie: undefined };
      }

      const session = sessions.get(id);

      if (session === undefined || session.grant.expiresAt <= now()) {
        sessions.delete(id);
        return { grant: undefined, cookie: clearedCookie(sessionCookie) };
      }

      if (session.grant.accessTokenExpiresAt - now() > refreshAhead) {
        return { grant: session.grant, cookie: undefined };
      }

      const before = session.grant;
      session.renewing ??= renewed(id, session, request).finally(() => {
        session.renewing = undefined;
      });
      await session.renewing;

      if (!sessions.has(id)) {
        return { grant: undefined, cookie: clearedCookie(sessionCookie) };
      }

      return { grant: session.grant, cookie: session.grant === before ? undefined : cookieFor(id, session.grant) };
    },

    end(request) {
      const id = readCookie(request, sessionCookie);
      const session = id === undefined ? undefined : sessions.get(id);

      if (id !== undefined) {
        sessions.delete(id);
      }

      return session?.grant;
    },
  };
}

export function relayWithSession<G extends Grant>(
  target: URL,
  store: SessionStore<G>,
  request: IncomingMessage,
  response: ServerResponse,
): Promise<void> {
  return store.find(request).then((found) => {
    const answer: OutgoingHttpHeaders = found.cookie === undefined ? {} : { 'set-cookie': found.cookie };
    const add: Record<string, string> = found.grant === undefined ? {} : { authorization: `Bearer ${found.grant.accessToken}` };

    forward(target, request, response, { drop: browserCredentials, add, answer });
  });
}

export async function accessTokenOf<G extends Grant>(
  store: SessionStore<G>,
  request: IncomingMessage,
  response?: ServerResponse,
): Promise<string | undefined> {
  const found = await store.find(request);

  if (found.cookie !== undefined && response !== undefined && !response.headersSent) {
    response.appendHeader('set-cookie', found.cookie);
  }

  return found.grant?.accessToken;
}

export function changesState(request: IncomingMessage): boolean {
  return !safeMethods.has((request.method ?? 'GET').toUpperCase());
}

export function refusedWithoutCsrfHeader(request: IncomingMessage, response: ServerResponse): boolean {
  if (!changesState(request) || request.headers['x-csrf'] === '1') {
    return false;
  }

  request.resume();
  answerProblem(response, 403, 'Forbidden', 'A request that changes state carries the X-CSRF: 1 header.', csrfRefusalCode);

  return true;
}

export function answerProblem(
  response: ServerResponse,
  status: number,
  title: string,
  detail: string,
  code: string,
  headers: OutgoingHttpHeaders = {},
): void {
  const body = JSON.stringify({ type: 'about:blank', title, status, detail, code });

  response.writeHead(status, {
    ...headers,
    'content-type': 'application/problem+json',
    'content-length': Buffer.byteLength(body),
    'cache-control': 'no-store',
  });
  response.end(body);
}

export function clearedCookie(name: string): string {
  return `${name}=; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=0`;
}

export function readCookie(request: IncomingMessage, cookie: string): string | undefined {
  for (const pair of (request.headers.cookie ?? '').split(';')) {
    const [name, value] = pair.trim().split('=', 2);

    if (name === cookie && value !== undefined && value !== '') {
      return value;
    }
  }

  return undefined;
}
