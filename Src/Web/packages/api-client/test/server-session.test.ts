import { createServer, type IncomingHttpHeaders, type Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { csrfRefusalCode, serverSessions, type ServerSessions } from '../src/server.ts';

interface Received {
  readonly method: string;
  readonly path: string;
  readonly headers: IncomingHttpHeaders;
  readonly body: string;
}

const minute = 60_000;
const cookiePattern = /^__Host-session=[A-Za-z0-9_-]{43}; Path=\/; HttpOnly; Secure; SameSite=Lax; Max-Age=\d+$/;
const servers: Server[] = [];

let clock: number;
let received: Received[];
let issued: number;
let refusingRefresh: boolean;

beforeEach(() => {
  clock = Date.parse('2026-10-29T12:00:00Z');
  received = [];
  issued = 0;
  refusingRefresh = false;
});

afterEach(async () => {
  await Promise.all(servers.splice(0).map((server) => new Promise((resolve) => server.close(resolve))));
});

async function listen(server: Server): Promise<string> {
  servers.push(server);
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));

  return `http://127.0.0.1:${String((server.address() as AddressInfo).port)}`;
}

function tokens() {
  issued++;

  return {
    accessToken: `access-${String(issued)}`,
    accessTokenExpiresAt: new Date(clock + minute).toISOString(),
    refreshToken: `refresh-${String(issued)}`,
    refreshTokenExpiresAt: new Date(clock + 24 * 60 * minute).toISOString(),
  };
}

function api(): Promise<string> {
  return listen(
    createServer((request, response) => {
      const chunks: Buffer[] = [];

      request.on('data', (chunk: Buffer) => chunks.push(chunk));
      request.on('end', () => {
        const body = Buffer.concat(chunks).toString();
        const path = request.url ?? '/';
        received.push({ method: request.method ?? 'GET', path, headers: request.headers, body });

        const answer = (status: number, value?: unknown) => {
          response.writeHead(status, { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json' });
          response.end(value === undefined ? undefined : JSON.stringify(value));
        };

        if (path === '/api/v1/auth/login') {
          const { password } = JSON.parse(body) as { password: string };
          answer(200, password === 'second-factor' ? { status: 'twoFactorRequired', challengeToken: 'challenge' } : { status: 'authenticated', tokens: tokens() });
        } else if (path === '/api/v1/auth/refresh') {
          setTimeout(() => {
            answer(refusingRefresh ? 401 : 200, refusingRefresh ? { status: 401, code: 'auth.required' } : tokens());
          }, 50);
        } else if (path === '/api/v1/auth/logout') {
          answer(204);
        } else {
          answer(200, { authorization: request.headers.authorization ?? null });
        }
      });
    }),
  );
}

async function site(sessions: ServerSessions): Promise<string> {
  return listen(
    createServer((request, response) => {
      sessions.relay(request, response, () => {
        void sessions.accessToken(request, response).then((token) => {
          response.writeHead(200, { 'Content-Type': 'text/plain' });
          response.end(token ?? 'signed out');
        });
      });
    }),
  );
}

async function signedIn(base: string): Promise<string> {
  const answer = await fetch(`${base}/api/v1/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-CSRF': '1' },
    body: JSON.stringify({ email: 'ada@example.com', password: 'secret' }),
  });

  return (answer.headers.getSetCookie()[0] ?? '').split(';', 1)[0] ?? '';
}

async function stack(): Promise<string> {
  return site(serverSessions(await api(), { now: () => clock }));
}

describe('serverSessions', () => {
  it('opens a session on sign-in in the __Host-session cookie alone, and answers without the tokens', async () => {
    const base = await stack();

    const answer = await fetch(`${base}/api/v1/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-CSRF': '1', 'User-Agent': 'browser/1', 'X-Refresh-Token-Mode': 'cookie' },
      body: JSON.stringify({ email: 'ada@example.com', password: 'secret' }),
    });
    const text = await answer.text();

    expect(answer.status).toBe(200);
    expect(answer.headers.getSetCookie()).toHaveLength(1);
    expect(answer.headers.getSetCookie()[0]).toMatch(cookiePattern);
    expect(text).not.toContain('access-1');
    expect(text).not.toContain('refresh-1');
    expect(JSON.parse(text)).toEqual({
      status: 'authenticated',
      tokens: { accessTokenExpiresAt: new Date(clock + minute).toISOString(), refreshTokenExpiresAt: new Date(clock + 24 * 60 * minute).toISOString() },
    });
    expect(received[0]?.body).toBe('{"email":"ada@example.com","password":"secret"}');
    expect(received[0]?.headers['user-agent']).toBe('browser/1');
    expect(received[0]?.headers['x-refresh-token-mode']).toBeUndefined();
  });

  it('opens no session on a two-factor challenge, which it passes through', async () => {
    const base = await stack();

    const answer = await fetch(`${base}/api/v1/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-CSRF': '1' },
      body: JSON.stringify({ email: 'ada@example.com', password: 'second-factor' }),
    });

    expect(await answer.json()).toEqual({ status: 'twoFactorRequired', challengeToken: 'challenge' });
    expect(answer.headers.getSetCookie()).toEqual([]);
  });

  it("sends the session's access token on each call, and never the browser's credentials", async () => {
    const base = await stack();
    const cookie = await signedIn(base);

    const answer = await fetch(`${base}/api/v1/notes`, { headers: { Cookie: `${cookie}; other=1`, Authorization: 'Bearer forged' } });

    expect(await answer.json()).toEqual({ authorization: 'Bearer access-1' });
    expect(received.at(-1)?.headers.cookie).toBeUndefined();
  });

  it('refuses a request that changes state without X-CSRF, before the API sees it', async () => {
    const base = await stack();
    const cookie = await signedIn(base);

    const refused = await fetch(`${base}/api/v1/notes`, { method: 'POST', headers: { Cookie: cookie }, body: '{}' });
    const control = await fetch(`${base}/api/v1/notes`, { method: 'POST', headers: { Cookie: cookie, 'X-CSRF': '1' }, body: '{}' });
    const read = await fetch(`${base}/api/v1/notes`, { headers: { Cookie: cookie } });

    expect(refused.status).toBe(403);
    expect(refused.headers.get('content-type')).toBe('application/problem+json');
    expect(await refused.json()).toMatchObject({ status: 403, code: csrfRefusalCode });
    expect(received.filter((call) => call.path === '/api/v1/notes' && call.method === 'POST')).toHaveLength(1);
    expect(control.status).toBe(200);
    expect(read.status).toBe(200);
  });

  it('refreshes once for two concurrent calls of a session near expiry, and both carry the new token', async () => {
    const base = await stack();
    const cookie = await signedIn(base);
    clock += minute - 10_000;

    const answers = await Promise.all(
      [1, 2].map(() => fetch(`${base}/api/v1/notes`, { headers: { Cookie: cookie, 'User-Agent': 'browser/2' } })),
    );

    const refreshes = received.filter((call) => call.path === '/api/v1/auth/refresh');
    expect(refreshes).toHaveLength(1);
    expect(refreshes[0]?.body).toBe('{"refreshToken":"refresh-1"}');
    expect(refreshes[0]?.headers['user-agent']).toBe('browser/2');
    expect(await Promise.all(answers.map((answer) => answer.json()))).toEqual([
      { authorization: 'Bearer access-2' },
      { authorization: 'Bearer access-2' },
    ]);
  });

  it('ends the session when the API refuses its refresh, and clears the cookie', async () => {
    const base = await stack();
    const cookie = await signedIn(base);
    clock += minute;
    refusingRefresh = true;

    const answer = await fetch(`${base}/api/v1/notes`, { headers: { Cookie: cookie } });
    const after = await fetch(`${base}/api/v1/notes`, { headers: { Cookie: cookie } });

    expect(await answer.json()).toEqual({ authorization: null });
    expect(answer.headers.getSetCookie()).toEqual(['__Host-session=; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=0']);
    expect(await after.json()).toEqual({ authorization: null });
    expect(received.filter((call) => call.path === '/api/v1/auth/refresh')).toHaveLength(1);
  });

  it('revokes the grant on sign-out, ends the session and clears the cookie', async () => {
    const base = await stack();
    const cookie = await signedIn(base);

    const answer = await fetch(`${base}/api/v1/auth/logout`, { method: 'POST', headers: { Cookie: cookie, 'X-CSRF': '1' } });
    const after = await fetch(`${base}/api/v1/notes`, { headers: { Cookie: cookie } });

    const logout = received.find((call) => call.path === '/api/v1/auth/logout');
    expect(answer.status).toBe(204);
    expect(answer.headers.getSetCookie()).toEqual(['__Host-session=; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=0']);
    expect(logout?.body).toBe('{"refreshToken":"refresh-1"}');
    expect(logout?.headers.authorization).toBe('Bearer access-1');
    expect(await after.json()).toEqual({ authorization: null });
  });

  it('replaces the session a browser had when it signs in again', async () => {
    const base = await stack();
    const first = await signedIn(base);

    const second = await fetch(`${base}/api/v1/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-CSRF': '1', Cookie: first },
      body: JSON.stringify({ email: 'ada@example.com', password: 'secret' }),
    });
    const after = await fetch(`${base}/api/v1/notes`, { headers: { Cookie: first } });

    expect(second.headers.getSetCookie()[0]?.split(';', 1)[0]).not.toBe(first);
    expect(await after.json()).toEqual({ authorization: null });
  });

  it("gives a page rendered for a signed-in browser the session's token, and none otherwise", async () => {
    const base = await stack();
    const cookie = await signedIn(base);

    expect(await (await fetch(`${base}/page`, { headers: { Cookie: cookie } })).text()).toBe('access-1');
    expect(await (await fetch(`${base}/page`)).text()).toBe('signed out');

    const unknown = await fetch(`${base}/page`, { headers: { Cookie: '__Host-session=unknown' } });
    expect(await unknown.text()).toBe('signed out');
    expect(unknown.headers.getSetCookie()).toEqual(['__Host-session=; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=0']);
  });
});
