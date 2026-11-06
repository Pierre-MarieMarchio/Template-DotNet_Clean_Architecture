import type { IncomingMessage } from 'node:http';
import { clearedCookie, cookie, newId, readCookie } from './cookies.ts';

export const sessionCookie = '__Host-session';

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
  readonly refreshAhead?: number | undefined;
  readonly now: () => number;
}

interface Session<G extends Grant> {
  grant: G;
  renewing: Promise<void> | undefined;
}

const defaultRefreshAhead = 30_000;

export function sessionStore<G extends Grant>(renew: Renew<G>, options: SessionStoreOptions): SessionStore<G> {
  const { refreshAhead = defaultRefreshAhead, now } = options;
  const sessions = new Map<string, Session<G>>();

  function cookieFor(id: string, grant: G): string {
    return cookie(sessionCookie, id, Math.max(0, Math.floor((grant.expiresAt - now()) / 1000)));
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
      const id = newId();

      if (replaced !== undefined) {
        sessions.delete(replaced);
      }

      evictExpired(sessions, now(), (session) => session.grant.expiresAt);

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

export function evictExpired<V>(entries: Map<string, V>, now: number, expiresAt: (entry: V) => number, limit = Infinity): void {
  for (const [key, entry] of entries) {
    if (expiresAt(entry) <= now || entries.size >= limit) {
      entries.delete(key);
    }
  }
}
