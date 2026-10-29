import { createHash, generateKeyPairSync, sign } from 'node:crypto';
import { createServer, type IncomingHttpHeaders, type Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import {
  checkProviderSettings,
  csrfRefusalCode,
  providerSessions,
  providerUnreachableCode,
  signInFailedCode,
  signInStateInvalidCode,
  type ProviderSessionsOptions,
  type ProviderSettings,
  type ServerSessions,
} from '../src/server.ts';

interface Received {
  readonly path: string;
  readonly headers: IncomingHttpHeaders;
}

interface Authorized {
  readonly challenge: string;
  readonly nonce: string;
}

const issuer = 'https://provider.test';
const origin = 'http://localhost:5173';
const minute = 60_000;
const sessionCookiePattern = /^__Host-session=[A-Za-z0-9_-]{43}; Path=\/; HttpOnly; Secure; SameSite=Lax; Max-Age=\d+$/;
const signInCookiePattern = /^__Host-sign-in=[A-Za-z0-9_-]{43}; Path=\/; HttpOnly; Secure; SameSite=Lax; Max-Age=600$/;
const clearedSession = '__Host-session=; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=0';
const clearedSignIn = '__Host-sign-in=; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=0';
const keys = generateKeyPairSync('rsa', { modulusLength: 2048 });
const settings: ProviderSettings = {
  EXTERNAL_AUTH_AUTHORITY: issuer,
  EXTERNAL_AUTH_INTERACTIVE_CLIENT_ID: 'web-app',
  PUBLIC_ORIGIN: origin,
};
const servers: Server[] = [];

let clock: number;
let received: Received[];
let providerCalls: string[];
let codes: Map<string, Authorized>;
let issued: number;
let issuingRefreshTokens: boolean;
let refusingRefresh: boolean;
let offeringEndSession: boolean;
let forgingNonce: boolean;

beforeEach(() => {
  clock = Date.now();
  received = [];
  providerCalls = [];
  codes = new Map();
  issued = 0;
  issuingRefreshTokens = true;
  refusingRefresh = false;
  offeringEndSession = true;
  forgingNonce = false;
});

afterEach(async () => {
  await Promise.all(servers.splice(0).map((server) => new Promise((resolve) => server.close(resolve))));
});

async function listen(server: Server): Promise<string> {
  servers.push(server);
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));

  return `http://127.0.0.1:${String((server.address() as AddressInfo).port)}`;
}

function encoded(value: unknown): string {
  return Buffer.from(JSON.stringify(value)).toString('base64url');
}

function idToken(nonce: string): string {
  const seconds = Math.floor(Date.now() / 1000);
  const signed = `${encoded({ alg: 'RS256', typ: 'JWT', kid: 'stub' })}.${encoded({
    iss: issuer,
    aud: 'web-app',
    sub: 'ada',
    nonce: forgingNonce ? 'forged' : nonce,
    iat: seconds,
    exp: seconds + 300,
  })}`;

  return `${signed}.${sign('sha256', Buffer.from(signed), keys.privateKey).toString('base64url')}`;
}

function tokens(nonce?: string) {
  issued++;

  return {
    access_token: `access-${String(issued)}`,
    token_type: 'Bearer',
    expires_in: 60,
    ...(issuingRefreshTokens ? { refresh_token: `refresh-${String(issued)}` } : {}),
    ...(nonce === undefined ? {} : { id_token: idToken(nonce) }),
  };
}

function provider(): Promise<string> {
  return listen(
    createServer((request, response) => {
      const chunks: Buffer[] = [];

      request.on('data', (chunk: Buffer) => chunks.push(chunk));
      request.on('end', () => {
        const path = request.url ?? '/';
        const form = new URLSearchParams(Buffer.concat(chunks).toString());
        providerCalls.push(path === '/token' ? `/token ${form.get('grant_type') ?? ''}` : path);

        const answer = (status: number, value: unknown) => {
          response.writeHead(status, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' });
          response.end(JSON.stringify(value));
        };

        if (path === '/.well-known/openid-configuration') {
          answer(200, {
            issuer,
            authorization_endpoint: `${issuer}/authorize`,
            token_endpoint: `${issuer}/token`,
            jwks_uri: `${issuer}/jwks`,
            ...(offeringEndSession ? { end_session_endpoint: `${issuer}/logout` } : {}),
            response_types_supported: ['code'],
            subject_types_supported: ['public'],
            id_token_signing_alg_values_supported: ['RS256'],
          });
        } else if (path === '/jwks') {
          answer(200, { keys: [{ ...keys.publicKey.export({ format: 'jwk' }), kid: 'stub', use: 'sig', alg: 'RS256' }] });
        } else if (path === '/token' && form.get('grant_type') === 'authorization_code') {
          const authorized = codes.get(form.get('code') ?? '');
          const verifier = form.get('code_verifier') ?? '';
          codes.delete(form.get('code') ?? '');

          if (authorized?.challenge === createHash('sha256').update(verifier).digest('base64url')) {
            answer(200, tokens(authorized.nonce));
          } else {
            answer(400, { error: 'invalid_grant' });
          }
        } else if (path === '/token' && form.get('grant_type') === 'refresh_token') {
          setTimeout(() => {
            if (refusingRefresh) {
              answer(400, { error: 'invalid_grant' });
            } else {
              answer(200, tokens());
            }
          }, 50);
        } else {
          answer(404, { error: 'not_found' });
        }
      });
    }),
  );
}

function api(): Promise<string> {
  return listen(
    createServer((request, response) => {
      request.resume();
      request.on('end', () => {
        received.push({ path: request.url ?? '/', headers: request.headers });
        response.writeHead(200, { 'Content-Type': 'application/json' });
        response.end(JSON.stringify({ authorization: request.headers.authorization ?? null }));
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

function towards(reached: string): NonNullable<ProviderSessionsOptions['fetch']> {
  return (url, { body, ...init }) =>
    fetch(url.replace(issuer, reached), { ...init, body: body instanceof URLSearchParams || typeof body === 'string' ? body : null });
}

async function stack(options: ProviderSessionsOptions = {}): Promise<string> {
  const reached = await provider();

  return site(
    providerSessions(await api(), settings, {
      now: () => clock,
      onError: () => undefined,
      fetch: towards(reached),
      ...options,
    }),
  );
}

function cookieOf(answer: Response, name: string): string {
  const cookie = answer.headers.getSetCookie().find((candidate) => candidate.startsWith(`${name}=`)) ?? '';

  return cookie.split(';', 1)[0] ?? '';
}

interface SignInStarted {
  readonly location: URL;
  readonly cookie: string;
  readonly answer: Response;
}

async function startSignIn(base: string, returnTo = '/notes'): Promise<SignInStarted> {
  const answer = await fetch(`${base}/session/sign-in?returnTo=${encodeURIComponent(returnTo)}`, { redirect: 'manual' });

  return { location: new URL(answer.headers.get('location') ?? '/', base), cookie: cookieOf(answer, '__Host-sign-in'), answer };
}

function authorize(location: URL): string {
  const code = `code-${String(codes.size + 1)}`;
  codes.set(code, { challenge: location.searchParams.get('code_challenge') ?? '', nonce: location.searchParams.get('nonce') ?? '' });

  return `/session/callback?code=${code}&state=${location.searchParams.get('state') ?? ''}`;
}

async function signedIn(base: string): Promise<string> {
  const { location, cookie } = await startSignIn(base);
  const answer = await fetch(`${base}${authorize(location)}`, { headers: { Cookie: cookie }, redirect: 'manual' });

  return cookieOf(answer, '__Host-session');
}

describe('providerSessions', () => {
  it('fetches nothing from the provider until the first sign-in, then sends the browser there with PKCE, state and nonce', async () => {
    const base = await stack();

    expect(providerCalls).toEqual([]);

    const { location, answer } = await startSignIn(base);

    expect(answer.status).toBe(302);
    expect(answer.headers.get('cache-control')).toBe('no-store');
    expect(answer.headers.getSetCookie()).toHaveLength(1);
    expect(answer.headers.getSetCookie()[0]).toMatch(signInCookiePattern);
    expect(`${location.origin}${location.pathname}`).toBe(`${issuer}/authorize`);
    expect(location.searchParams.get('response_type')).toBe('code');
    expect(location.searchParams.get('client_id')).toBe('web-app');
    expect(location.searchParams.get('redirect_uri')).toBe(`${origin}/session/callback`);
    expect(location.searchParams.get('scope')).toBe('openid profile email offline_access');
    expect(location.searchParams.get('code_challenge_method')).toBe('S256');
    expect(location.searchParams.get('code_challenge')).toMatch(/^[A-Za-z0-9_-]{43}$/);
    expect(location.searchParams.get('state')).toMatch(/^[A-Za-z0-9_-]{43}$/);
    expect(location.searchParams.get('nonce')).toMatch(/^[A-Za-z0-9_-]{43}$/);

    await startSignIn(base);

    expect(providerCalls).toEqual(['/.well-known/openid-configuration']);
  });

  it('adds the authorization parameters a provider needs, such as its audience', async () => {
    const base = await stack({ authorizationParameters: { audience: 'https://api.example.com' } });

    const { location } = await startSignIn(base);

    expect(location.searchParams.get('audience')).toBe('https://api.example.com');
    expect(location.searchParams.get('code_challenge_method')).toBe('S256');
  });

  it('opens the session on the callback in the __Host-session cookie alone, and sends the browser back where it was', async () => {
    const base = await stack();
    const { location, cookie } = await startSignIn(base, '/notes?page=2');

    const answer = await fetch(`${base}${authorize(location)}`, { headers: { Cookie: cookie }, redirect: 'manual' });
    const body = await answer.text();
    const head = JSON.stringify([...answer.headers]);

    expect(answer.status).toBe(302);
    expect(answer.headers.get('location')).toBe('/notes?page=2');
    expect(answer.headers.getSetCookie()).toHaveLength(2);
    expect(answer.headers.getSetCookie()[0]).toMatch(sessionCookiePattern);
    expect(answer.headers.getSetCookie()[1]).toBe(clearedSignIn);
    expect(providerCalls).toContain('/token authorization_code');
    for (const secret of ['access-1', 'refresh-1', 'eyJ']) {
      expect(body).not.toContain(secret);
      expect(head).not.toContain(secret);
    }
  });

  it('refuses a callback whose state is not the one this browser was sent with, before the provider sees the code', async () => {
    const base = await stack();
    const { location, cookie } = await startSignIn(base);
    const callback = authorize(location);

    const forgedState = callback.replace(/state=[^&]*/, 'state=forged');

    const forged = await fetch(`${base}${forgedState}`, { headers: { Cookie: cookie }, redirect: 'manual' });

    expect(forged.status).toBe(400);
    expect(await forged.json()).toMatchObject({ status: 400, code: signInStateInvalidCode });
    expect(forged.headers.getSetCookie()).toEqual([clearedSignIn]);
    expect(providerCalls).not.toContain('/token authorization_code');
  });

  it('refuses a callback from another browser, or one already spent', async () => {
    const base = await stack();
    const { location, cookie } = await startSignIn(base);
    const callback = authorize(location);

    const elsewhere = await fetch(`${base}${callback}`, { redirect: 'manual' });
    const first = await fetch(`${base}${callback}`, { headers: { Cookie: cookie }, redirect: 'manual' });
    const again = await fetch(`${base}${callback}`, { headers: { Cookie: cookie }, redirect: 'manual' });

    expect(elsewhere.status).toBe(400);
    expect(first.status).toBe(302);
    expect(again.status).toBe(400);
    expect(await again.json()).toMatchObject({ code: signInStateInvalidCode });
  });

  it('refuses an identity token that does not carry the nonce of this sign-in', async () => {
    const base = await stack();
    forgingNonce = true;
    const { location, cookie } = await startSignIn(base);

    const answer = await fetch(`${base}${authorize(location)}`, { headers: { Cookie: cookie }, redirect: 'manual' });

    expect(answer.status).toBe(400);
    expect(await answer.json()).toMatchObject({ code: signInFailedCode });
    expect(answer.headers.getSetCookie()).toEqual([clearedSignIn]);
  });

  it('sends a browser back to this app alone, whatever return path it asks for', async () => {
    const base = await stack();

    for (const returnTo of ['//elsewhere.test/', 'https://elsewhere.test/', '/\t/elsewhere.test/', '\\\\elsewhere.test']) {
      const { location, cookie } = await startSignIn(base, returnTo);
      const answer = await fetch(`${base}${authorize(location)}`, { headers: { Cookie: cookie }, redirect: 'manual' });

      expect(answer.headers.get('location'), returnTo).toBe('/');
    }
  });

  it("sends the session's access token on each call, and never the browser's credentials", async () => {
    const base = await stack();
    const cookie = await signedIn(base);

    const answer = await fetch(`${base}/api/v1/notes`, { headers: { Cookie: `${cookie}; other=1`, Authorization: 'Bearer forged' } });

    expect(await answer.json()).toEqual({ authorization: 'Bearer access-1' });
    expect(received.at(-1)?.headers.cookie).toBeUndefined();
  });

  it('refuses a request that changes state without X-CSRF, sign-out included, before anyone sees it', async () => {
    const base = await stack();
    const cookie = await signedIn(base);

    const call = await fetch(`${base}/api/v1/notes`, { method: 'POST', headers: { Cookie: cookie }, body: '{}' });
    const signOut = await fetch(`${base}/session/sign-out`, { method: 'POST', headers: { Cookie: cookie } });
    const after = await fetch(`${base}/api/v1/notes`, { headers: { Cookie: cookie } });

    expect(call.status).toBe(403);
    expect(await call.json()).toMatchObject({ code: csrfRefusalCode });
    expect(signOut.status).toBe(403);
    expect(received.filter((request) => request.path === '/api/v1/notes')).toHaveLength(1);
    expect(await after.json()).toEqual({ authorization: 'Bearer access-1' });
  });

  it('refreshes once for two concurrent calls of a session near expiry, and both carry the new token', async () => {
    const base = await stack();
    const cookie = await signedIn(base);
    clock += minute - 10_000;

    const answers = await Promise.all([1, 2].map(() => fetch(`${base}/api/v1/notes`, { headers: { Cookie: cookie } })));

    expect(providerCalls.filter((call) => call === '/token refresh_token')).toHaveLength(1);
    expect(await Promise.all(answers.map((answer) => answer.json()))).toEqual([
      { authorization: 'Bearer access-2' },
      { authorization: 'Bearer access-2' },
    ]);
  });

  it('ends the session when the provider refuses its refresh, and clears the cookie', async () => {
    const base = await stack();
    const cookie = await signedIn(base);
    clock += minute;
    refusingRefresh = true;

    const answer = await fetch(`${base}/api/v1/notes`, { headers: { Cookie: cookie } });
    const after = await fetch(`${base}/api/v1/notes`, { headers: { Cookie: cookie } });

    expect(await answer.json()).toEqual({ authorization: null });
    expect(answer.headers.getSetCookie()).toEqual([clearedSession]);
    expect(await after.json()).toEqual({ authorization: null });
    expect(providerCalls.filter((call) => call === '/token refresh_token')).toHaveLength(1);
  });

  it('ends a session without a refresh token when its access token expires', async () => {
    issuingRefreshTokens = false;
    const base = await stack();
    const cookie = await signedIn(base);

    clock += minute - 10_000;
    const late = await fetch(`${base}/api/v1/notes`, { headers: { Cookie: cookie } });
    clock += 10_000;
    const expired = await fetch(`${base}/api/v1/notes`, { headers: { Cookie: cookie } });

    expect(await late.json()).toEqual({ authorization: 'Bearer access-1' });
    expect(await expired.json()).toEqual({ authorization: null });
    expect(expired.headers.getSetCookie()).toEqual([clearedSession]);
    expect(providerCalls.filter((call) => call === '/token refresh_token')).toHaveLength(0);
  });

  it("forgets the tokens on sign-out and sends the browser to the provider's sign-out with its identity token", async () => {
    const base = await stack();
    const cookie = await signedIn(base);

    const answer = await fetch(`${base}/session/sign-out`, { method: 'POST', headers: { Cookie: cookie, 'X-CSRF': '1' } });
    const { location } = (await answer.json()) as { location: string };
    const after = await fetch(`${base}/api/v1/notes`, { headers: { Cookie: cookie } });
    const logout = new URL(location);

    expect(answer.status).toBe(200);
    expect(answer.headers.getSetCookie()).toEqual([clearedSession]);
    expect(`${logout.origin}${logout.pathname}`).toBe(`${issuer}/logout`);
    expect(logout.searchParams.get('client_id')).toBe('web-app');
    expect(logout.searchParams.get('post_logout_redirect_uri')).toBe(`${origin}/`);
    expect(logout.searchParams.get('id_token_hint')).toMatch(/^eyJ/);
    expect(await after.json()).toEqual({ authorization: null });
  });

  it('signs out here alone when the provider offers no sign-out', async () => {
    offeringEndSession = false;
    const base = await stack();
    const cookie = await signedIn(base);

    const answer = await fetch(`${base}/session/sign-out`, { method: 'POST', headers: { Cookie: cookie, 'X-CSRF': '1' } });

    expect(await answer.json()).toEqual({ location: '/' });
    expect(answer.headers.getSetCookie()).toEqual([clearedSession]);
  });

  it('answers 502 while the provider cannot be reached, and tries again at the next sign-in', async () => {
    let reachable = false;
    const reached = await provider();
    const base = await site(
      providerSessions(await api(), settings, {
        onError: () => undefined,
        fetch: (url, init) => (reachable ? towards(reached)(url, init) : Promise.reject(new TypeError('fetch failed'))),
      }),
    );

    const unreachable = await fetch(`${base}/session/sign-in`, { redirect: 'manual' });
    reachable = true;
    const reachedAgain = await fetch(`${base}/session/sign-in`, { redirect: 'manual' });

    expect(unreachable.status).toBe(502);
    expect(await unreachable.json()).toMatchObject({ code: providerUnreachableCode });
    expect(reachedAgain.status).toBe(302);
  });

  it("gives a page rendered for a signed-in browser the session's token, and none otherwise", async () => {
    const base = await stack();
    const cookie = await signedIn(base);

    expect(await (await fetch(`${base}/page`, { headers: { Cookie: cookie } })).text()).toBe('access-1');
    expect(await (await fetch(`${base}/page`)).text()).toBe('signed out');
  });

  it('names each setting the provider, the client or the public origin lacks, for the server to refuse to start', () => {
    expect(() => {
      checkProviderSettings({});
    }).toThrow(/EXTERNAL_AUTH_AUTHORITY.*EXTERNAL_AUTH_INTERACTIVE_CLIENT_ID.*PUBLIC_ORIGIN/);
    expect(() => {
      checkProviderSettings({ ...settings, EXTERNAL_AUTH_AUTHORITY: 'http://provider.test' });
    }).toThrow(/EXTERNAL_AUTH_AUTHORITY/);
    expect(() => {
      checkProviderSettings({ ...settings, PUBLIC_ORIGIN: 'http://app.example.com' });
    }).toThrow(/PUBLIC_ORIGIN/);
    expect(() => {
      checkProviderSettings({ ...settings, PUBLIC_ORIGIN: 'https://app.example.com/a' });
    }).toThrow(/PUBLIC_ORIGIN/);
    expect(() => {
      checkProviderSettings({ ...settings, EXTERNAL_AUTH_INTERACTIVE_SCOPE: 'profile' });
    }).toThrow(/EXTERNAL_AUTH_INTERACTIVE_SCOPE/);
    expect(() => {
      checkProviderSettings({ ...settings, PUBLIC_ORIGIN: 'https://app.example.com' });
    }).not.toThrow();
  });

  it('loads without its settings, as the framework\'s build does, and refuses a sign-in until they are set', async () => {
    const failures: unknown[] = [];
    const sessions = providerSessions(await api(), {});
    const base = await listen(
      createServer((request, response) => {
        sessions.relay(request, response, (error?: unknown) => {
          failures.push(error);
          response.writeHead(500);
          response.end();
        });
      }),
    );

    const answer = await fetch(`${base}/session/sign-in`, { redirect: 'manual' });

    expect(answer.status).toBe(500);
    expect(String(failures[0])).toMatch(/EXTERNAL_AUTH_AUTHORITY/);
    expect(providerCalls).toEqual([]);
  });
});
