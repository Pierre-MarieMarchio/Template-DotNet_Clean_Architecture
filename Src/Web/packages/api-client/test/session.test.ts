import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  cookieModePaths,
  createApiClient,
  createBrowserSession,
  refreshTokenModeHeader,
  sessionMiddleware,
  type BrowserSession,
} from '../src/index.ts';

type Answer = Response | Promise<Response> | Error;

class FakeApi {
  readonly seen: Request[] = [];
  readonly bodies: string[] = [];
  private readonly answers = new Map<string, (() => Answer)[]>();

  answer(path: string, ...answers: (() => Answer)[]): this {
    this.answers.set(path, [...(this.answers.get(path) ?? []), ...answers]);
    return this;
  }

  readonly fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
    const request = new Request(input, init);
    this.seen.push(request);
    this.bodies.push(await request.clone().text());

    const path = new URL(request.url).pathname;
    const answer = this.answers.get(path)?.shift()?.() ?? json(404, { code: 'test.unexpected' });

    if (answer instanceof Error) {
      throw answer;
    }

    return answer;
  };

  to(path: string): Request[] {
    return this.seen.filter((request) => new URL(request.url).pathname === path);
  }
}

function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function tokens(accessToken: string) {
  return { accessToken, accessTokenExpiresAt: '2026-10-20T10:15:00Z', refreshTokenExpiresAt: '2026-11-03T10:00:00Z' };
}

const refreshed = (accessToken: string) => () => json(200, tokens(accessToken));
const refused = () => () => json(401, { code: 'auth.refreshToken.invalid', status: 401 });
const ok = (body: unknown = {}) => () => json(200, body);
const unauthorized = () => () => json(401, { code: 'auth.unauthorized', status: 401 });

function deferred() {
  let resolve: (response: Response) => void = () => undefined;
  const promise = new Promise<Response>((settle) => {
    resolve = settle;
  });
  return { promise, resolve };
}

function session(api: FakeApi): BrowserSession {
  return createBrowserSession({ baseUrl: 'http://site.test', fetch: api.fetch });
}

interface Operation {
  parameters: { query?: never; header?: never; path?: never; cookie?: never };
  requestBody?: { content: { 'application/json': Record<string, unknown> } };
  responses: {
    200: { headers: Record<string, unknown>; content: { 'application/json': Record<string, unknown> } };
    401: { headers: Record<string, unknown>; content: { 'application/json': Record<string, unknown> } };
  };
}

interface Route {
  parameters: { query?: never; header?: never; path?: never; cookie?: never };
  get: Operation;
  post: Operation;
  put?: never;
  delete?: never;
  options?: never;
  head?: never;
  patch?: never;
  trace?: never;
}

type TestPaths = Record<string, Route>;

function client(api: FakeApi, browserSession: BrowserSession) {
  return createApiClient<TestPaths>({
    baseUrl: 'http://site.test',
    fetch: (request) => api.fetch(request),
    middleware: [sessionMiddleware(browserSession)],
  });
}

describe('the browser session', () => {
  it('starts signed out and holds the access token a sign-in answered', () => {
    const browserSession = session(new FakeApi());
    const changes: boolean[] = [];
    browserSession.onChange((signedIn) => changes.push(signedIn));

    expect(browserSession.isSignedIn()).toBe(false);

    browserSession.signedIn(tokens('access-1'));

    expect(browserSession.accessToken()).toBe('access-1');
    expect(browserSession.isSignedIn()).toBe(true);
    expect(changes).toEqual([true]);
  });

  it('refreshes through the cookie: a bodiless POST carrying the mode header, same-origin credentials', async () => {
    const api = new FakeApi().answer('/api/v1/auth/refresh', refreshed('access-2'));
    const browserSession = session(api);

    await expect(browserSession.refresh()).resolves.toBe(true);

    const [request] = api.to('/api/v1/auth/refresh');
    expect(request?.method).toBe('POST');
    expect(request?.headers.get('X-Refresh-Token-Mode')).toBe('cookie');
    expect(request?.credentials).toBe('same-origin');
    expect(api.bodies).toEqual(['']);
    expect(browserSession.accessToken()).toBe('access-2');
  });

  it('recovers a session from the cookie after a page reload, with nothing kept from before', async () => {
    const api = new FakeApi().answer('/api/v1/auth/refresh', refreshed('access-after-reload'));
    const reloaded = session(api);

    expect(reloaded.isSignedIn()).toBe(false);
    await expect(reloaded.refresh()).resolves.toBe(true);
    expect(reloaded.accessToken()).toBe('access-after-reload');
  });

  it('shares one refresh request between concurrent callers', async () => {
    const pending = deferred();
    const api = new FakeApi().answer('/api/v1/auth/refresh', () => pending.promise, refreshed('access-4'));
    const browserSession = session(api);

    const first = browserSession.refresh();
    const second = browserSession.refresh();
    pending.resolve(json(200, tokens('access-3')));

    await expect(Promise.all([first, second])).resolves.toEqual([true, true]);
    expect(api.to('/api/v1/auth/refresh')).toHaveLength(1);

    await browserSession.refresh();
    expect(api.to('/api/v1/auth/refresh')).toHaveLength(2);
    expect(browserSession.accessToken()).toBe('access-4');
  });

  it('signs out locally when the refresh is refused', async () => {
    const api = new FakeApi().answer('/api/v1/auth/refresh', refused());
    const browserSession = session(api);
    browserSession.signedIn(tokens('access-1'));
    const changes: boolean[] = [];
    browserSession.onChange((signedIn) => changes.push(signedIn));

    await expect(browserSession.refresh()).resolves.toBe(false);

    expect(browserSession.isSignedIn()).toBe(false);
    expect(browserSession.accessToken()).toBeUndefined();
    expect(changes).toEqual([false]);
  });

  it('keeps the session through a refresh that never reached the API', async () => {
    const api = new FakeApi().answer('/api/v1/auth/refresh', () => new TypeError('Failed to fetch'));
    const browserSession = session(api);
    browserSession.signedIn(tokens('access-1'));

    await expect(browserSession.refresh()).resolves.toBe(false);

    expect(browserSession.accessToken()).toBe('access-1');
  });

  it('keeps a sign-in that lands while an older refresh is being refused', async () => {
    const pending = deferred();
    const api = new FakeApi().answer('/api/v1/auth/refresh', () => pending.promise);
    const browserSession = session(api);

    const refresh = browserSession.refresh();
    browserSession.signedIn(tokens('access-signed-in'));
    pending.resolve(json(401, { code: 'auth.refreshToken.invalid' }));

    await expect(refresh).resolves.toBe(true);
    expect(browserSession.accessToken()).toBe('access-signed-in');
  });

  it('signs out through a bodiless logout carrying the mode header', async () => {
    const api = new FakeApi().answer('/api/v1/auth/logout', () => new Response(null, { status: 204 }));
    const browserSession = session(api);
    browserSession.signedIn(tokens('access-1'));

    await browserSession.signOut();

    const [request] = api.to('/api/v1/auth/logout');
    expect(request?.method).toBe('POST');
    expect(request?.headers.get('X-Refresh-Token-Mode')).toBe('cookie');
    expect(request?.credentials).toBe('same-origin');
    expect(api.bodies).toEqual(['']);
    expect(browserSession.isSignedIn()).toBe(false);
  });

  it('clears the local session even when the logout call fails', async () => {
    const api = new FakeApi()
      .answer('/api/v1/auth/logout', () => new TypeError('Failed to fetch'), () => json(500, { code: 'server.error' }));
    const browserSession = session(api);

    browserSession.signedIn(tokens('access-1'));
    await expect(browserSession.signOut()).resolves.toBeUndefined();
    expect(browserSession.isSignedIn()).toBe(false);

    browserSession.signedIn(tokens('access-2'));
    await expect(browserSession.signOut()).resolves.toBeUndefined();
    expect(browserSession.isSignedIn()).toBe(false);
  });

  it('lets a refresh in flight finish before logging out, without signing back in', async () => {
    const pending = deferred();
    const api = new FakeApi()
      .answer('/api/v1/auth/refresh', () => pending.promise)
      .answer('/api/v1/auth/logout', () => new Response(null, { status: 204 }));
    const browserSession = session(api);
    browserSession.signedIn(tokens('access-1'));

    const refresh = browserSession.refresh();
    const signOut = browserSession.signOut();
    pending.resolve(json(200, tokens('access-2')));
    await Promise.all([refresh, signOut]);

    expect(api.seen.map((request) => new URL(request.url).pathname)).toEqual(['/api/v1/auth/refresh', '/api/v1/auth/logout']);
    expect(browserSession.isSignedIn()).toBe(false);
  });

  it('stops telling a listener once it unsubscribed', () => {
    const browserSession = session(new FakeApi());
    const changes: boolean[] = [];
    const unsubscribe = browserSession.onChange((signedIn) => changes.push(signedIn));

    unsubscribe();
    browserSession.signedIn(tokens('access-1'));

    expect(changes).toEqual([]);
  });

  describe('storage', () => {
    const writes: string[] = [];

    function recordingStorage(name: string): Storage {
      return {
        length: 0,
        clear: () => writes.push(`${name}.clear`),
        getItem: () => null,
        key: () => null,
        removeItem: (key: string) => writes.push(`${name}.removeItem(${key})`),
        setItem: (key: string) => writes.push(`${name}.setItem(${key})`),
      };
    }

    beforeEach(() => {
      writes.length = 0;
      vi.stubGlobal('localStorage', recordingStorage('localStorage'));
      vi.stubGlobal('sessionStorage', recordingStorage('sessionStorage'));
    });

    afterEach(() => {
      vi.unstubAllGlobals();
    });

    it('never writes a token to web storage', async () => {
      const api = new FakeApi()
        .answer('/api/v1/auth/refresh', refreshed('access-2'), refused())
        .answer('/api/v1/auth/logout', () => new Response(null, { status: 204 }));
      const browserSession = session(api);

      browserSession.signedIn(tokens('access-1'));
      await browserSession.refresh();
      await browserSession.refresh();
      browserSession.signedIn(tokens('access-3'));
      await browserSession.signOut();

      expect(writes).toEqual([]);
    });
  });
});

describe('the session middleware', () => {
  it('sends the held access token as a bearer token, and none while signed out', async () => {
    const api = new FakeApi().answer('/api/v1/notes', ok(), ok());
    const browserSession = session(api);
    const notes = client(api, browserSession);

    await notes.GET('/api/v1/notes');
    browserSession.signedIn(tokens('access-1'));
    await notes.GET('/api/v1/notes');

    expect(api.seen.map((request) => request.headers.get('Authorization'))).toEqual([null, 'Bearer access-1']);
  });

  it.each(cookieModePaths)('asks for cookie mode on %s', async (path) => {
    const api = new FakeApi().answer(path, ok());
    const browserSession = session(api);
    browserSession.signedIn(tokens('access-1'));

    await client(api, browserSession).POST(path, { body: {} });

    expect(api.seen[0]?.headers.get('X-Refresh-Token-Mode')).toBe(refreshTokenModeHeader['X-Refresh-Token-Mode']);
    expect(api.seen[0]?.headers.has('Authorization')).toBe(false);
  });

  it.each(['/api/v1/notes', '/api/v1/auth/me', '/api/v1/auth/passkeys/sign-in/options', '/api/v1/auth/logout-all'])(
    'leaves the mode header off %s',
    async (path) => {
      const api = new FakeApi().answer(path, ok());

      await client(api, session(api)).POST(path, { body: {} });

      expect(api.seen[0]?.headers.has('X-Refresh-Token-Mode')).toBe(false);
    },
  );

  it('refreshes once on a 401 and replays the request with the new token', async () => {
    const api = new FakeApi()
      .answer('/api/v1/notes', unauthorized(), ok({ id: 'n-1' }))
      .answer('/api/v1/auth/refresh', refreshed('access-2'));
    const browserSession = session(api);
    browserSession.signedIn(tokens('access-1'));

    const { data, response } = await client(api, browserSession).POST('/api/v1/notes', { body: { title: 'Groceries' } });

    expect(response.status).toBe(200);
    expect(data).toEqual({ id: 'n-1' });
    const calls = api.to('/api/v1/notes');
    expect(calls.map((request) => request.headers.get('Authorization'))).toEqual(['Bearer access-1', 'Bearer access-2']);
    expect(api.bodies.filter((body) => body.includes('Groceries'))).toHaveLength(2);
  });

  it('retries once, not twice, when the replayed request is refused again', async () => {
    const api = new FakeApi()
      .answer('/api/v1/notes', unauthorized(), unauthorized(), ok())
      .answer('/api/v1/auth/refresh', refreshed('access-2'), refreshed('access-3'));
    const browserSession = session(api);
    browserSession.signedIn(tokens('access-1'));

    const { response } = await client(api, browserSession).GET('/api/v1/notes');

    expect(response.status).toBe(401);
    expect(api.to('/api/v1/notes')).toHaveLength(2);
    expect(api.to('/api/v1/auth/refresh')).toHaveLength(1);
  });

  it('answers the original 401 and signs out when the refresh is refused', async () => {
    const api = new FakeApi()
      .answer('/api/v1/notes', unauthorized())
      .answer('/api/v1/auth/refresh', refused());
    const browserSession = session(api);
    browserSession.signedIn(tokens('access-1'));

    const { response, error } = await client(api, browserSession).GET('/api/v1/notes');

    expect(response.status).toBe(401);
    expect(error).toMatchObject({ code: 'auth.unauthorized' });
    expect(api.to('/api/v1/notes')).toHaveLength(1);
    expect(browserSession.isSignedIn()).toBe(false);
  });

  it('never refreshes on a 401 from a sign-in, refresh or logout call', async () => {
    const api = new FakeApi().answer('/api/v1/auth/login', unauthorized());
    const browserSession = session(api);

    const { response } = await client(api, browserSession).POST('/api/v1/auth/login', { body: {} });

    expect(response.status).toBe(401);
    expect(api.to('/api/v1/auth/refresh')).toHaveLength(0);
  });

  it('shares one refresh between calls refused at the same time', async () => {
    const pending = deferred();
    const api = new FakeApi()
      .answer('/api/v1/notes', unauthorized(), unauthorized(), ok(), ok())
      .answer('/api/v1/auth/refresh', () => pending.promise);
    const browserSession = session(api);
    browserSession.signedIn(tokens('access-1'));
    const notes = client(api, browserSession);

    const calls = Promise.all([notes.GET('/api/v1/notes'), notes.GET('/api/v1/notes')]);
    await vi.waitFor(() => {
      expect(api.to('/api/v1/auth/refresh')).toHaveLength(1);
    });
    pending.resolve(json(200, tokens('access-2')));
    const answers = await calls;

    expect(answers.map(({ response }) => response.status)).toEqual([200, 200]);
    expect(api.to('/api/v1/auth/refresh')).toHaveLength(1);
  });

  it('replays without refreshing when another call already renewed the token', async () => {
    const api = new FakeApi().answer('/api/v1/notes', () => {
      browserSession.signedIn(tokens('access-2'));
      return json(401, { code: 'auth.unauthorized' });
    }, ok());
    const browserSession = session(api);
    browserSession.signedIn(tokens('access-1'));

    const { response } = await client(api, browserSession).GET('/api/v1/notes');

    expect(response.status).toBe(200);
    expect(api.to('/api/v1/auth/refresh')).toHaveLength(0);
    expect(api.to('/api/v1/notes').map((request) => request.headers.get('Authorization')))
      .toEqual(['Bearer access-1', 'Bearer access-2']);
  });
});
