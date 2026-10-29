import type { Middleware } from 'openapi-fetch';

export const refreshTokenModeHeader: Readonly<Record<'X-Refresh-Token-Mode', 'cookie'>> = Object.freeze({
  'X-Refresh-Token-Mode': 'cookie',
});

export const refreshPath = '/api/v1/auth/refresh';
export const logoutPath = '/api/v1/auth/logout';

export const cookieModePaths: readonly string[] = Object.freeze([
  '/api/v1/auth/login',
  '/api/v1/auth/login/two-factor',
  '/api/v1/auth/login/external',
  '/api/v1/auth/passkeys/sign-in',
  refreshPath,
  logoutPath,
]);

export const csrfHeader: Readonly<Record<'X-CSRF', '1'>> = Object.freeze({ 'X-CSRF': '1' });

export function serverSessionMiddleware(): Middleware {
  return {
    onRequest({ request }) {
      request.headers.set('X-CSRF', csrfHeader['X-CSRF']);
      return request;
    },
  };
}

export interface SignedInTokens {
  accessToken: string;
  accessTokenExpiresAt: string;
}

export interface BrowserSession {
  accessToken(): string | undefined;
  isSignedIn(): boolean;
  signedIn(tokens: SignedInTokens): void;
  refresh(): Promise<boolean>;
  signOut(): Promise<void>;
  onChange(listener: (signedIn: boolean) => void): () => void;
}

export interface BrowserSessionOptions {
  readonly baseUrl?: string;
  readonly fetch?: typeof fetch;
}

export function createBrowserSession(options: BrowserSessionOptions = {}): BrowserSession {
  const baseUrl = (options.baseUrl ?? '').replace(/\/+$/, '');
  const send = options.fetch ?? ((input: RequestInfo | URL, init?: RequestInit) => globalThis.fetch(input, init));
  const listeners = new Set<(signedIn: boolean) => void>();

  let token: string | undefined;
  let generation = 0;
  let pending: Promise<boolean> | undefined;

  function hold(next: string | undefined): void {
    const wasSignedIn = token !== undefined;
    token = next;

    if (wasSignedIn !== (token !== undefined)) {
      for (const listener of [...listeners]) {
        listener(token !== undefined);
      }
    }
  }

  function post(path: string): Promise<Response> {
    return send(baseUrl + path, {
      method: 'POST',
      headers: { ...refreshTokenModeHeader },
      credentials: 'same-origin',
    });
  }

  async function exchange(): Promise<boolean> {
    const started = generation;
    let response: Response;

    try {
      response = await post(refreshPath);
    } catch {
      return false;
    }

    const accessToken = response.ok ? await readAccessToken(response) : undefined;

    if (started !== generation) {
      return token !== undefined;
    }

    if (response.status === 401) {
      hold(undefined);
      return false;
    }

    if (accessToken === undefined) {
      return false;
    }

    hold(accessToken);
    return true;
  }

  return {
    accessToken: () => token,
    isSignedIn: () => token !== undefined,
    signedIn(tokens) {
      if (tokens.accessToken === '') {
        throw new Error('A sign-in answer carries an access token; this one is empty.');
      }

      generation++;
      hold(tokens.accessToken);
    },
    refresh() {
      pending ??= exchange().finally(() => {
        pending = undefined;
      });

      return pending;
    },
    async signOut() {
      const inFlight = pending;
      generation++;
      hold(undefined);

      if (inFlight !== undefined) {
        await inFlight;
      }

      try {
        await post(logoutPath);
      } catch {
        return;
      }
    },
    onChange(listener) {
      listeners.add(listener);

      return () => {
        listeners.delete(listener);
      };
    },
  };
}

export function sessionMiddleware(session: BrowserSession): Middleware {
  const retries = new Map<string, { readonly request: Request; readonly token: string | undefined }>();

  return {
    onRequest({ request, schemaPath, id }) {
      if (cookieModePaths.includes(schemaPath)) {
        request.headers.set('X-Refresh-Token-Mode', refreshTokenModeHeader['X-Refresh-Token-Mode']);
        return request;
      }

      const token = session.accessToken();

      if (token !== undefined) {
        request.headers.set('Authorization', `Bearer ${token}`);
      }

      retries.set(id, { request: request.clone(), token });
      return request;
    },
    async onResponse({ response, id, options }) {
      const retry = retries.get(id);
      retries.delete(id);

      if (retry === undefined || response.status !== 401) {
        return response;
      }

      const current = session.accessToken();
      const renewed = current !== undefined && current !== retry.token ? true : await session.refresh();
      const token = session.accessToken();

      if (!renewed || token === undefined) {
        return response;
      }

      retry.request.headers.set('Authorization', `Bearer ${token}`);
      return options.fetch(retry.request);
    },
    onError({ id }) {
      retries.delete(id);
    },
  };
}

async function readAccessToken(response: Response): Promise<string | undefined> {
  try {
    const body: unknown = await response.json();

    const accessToken = typeof body === 'object' && body !== null && 'accessToken' in body ? body.accessToken : undefined;

    return typeof accessToken === 'string' && accessToken !== '' ? accessToken : undefined;
  } catch {
    return undefined;
  }
}
