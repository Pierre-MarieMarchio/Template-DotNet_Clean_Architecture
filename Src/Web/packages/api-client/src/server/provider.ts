import { randomBytes } from 'node:crypto';
import type { IncomingMessage, ServerResponse } from 'node:http';
import * as oidc from 'openid-client';
import { apiOrigin, isRelayedToApi, pathOf, type ApiRelay } from './relay.ts';
import type { ServerSessions } from './session.ts';
import {
  accessTokenOf,
  answerProblem,
  clearedCookie,
  readCookie,
  refusedWithoutCsrfHeader,
  relayWithSession,
  sessionCookie,
  sessionStore,
  type Grant,
  type Renewal,
} from './store.ts';

export const signInPath = '/session/sign-in';
export const callbackPath = '/session/callback';
export const signOutPath = '/session/sign-out';
export const signInCookie = '__Host-sign-in';

export const providerUnreachableCode = 'session.providerUnreachable';
export const signInStateInvalidCode = 'session.signInStateInvalid';
export const signInFailedCode = 'session.signInFailed';

export type ProviderSettings = Readonly<Record<string, string | undefined>>;

export interface ProviderSessionsOptions {
  readonly authorizationParameters?: Readonly<Record<string, string>>;
  readonly sessionLifetime?: number;
  readonly refreshAhead?: number;
  readonly now?: () => number;
  readonly onError?: (error: unknown) => void;
  readonly fetch?: oidc.CustomFetch;
}

interface ProviderGrant extends Grant {
  readonly refreshToken: string | undefined;
  readonly idToken: string | undefined;
}

interface PendingSignIn {
  readonly state: string;
  readonly nonce: string;
  readonly codeVerifier: string;
  readonly returnTo: string;
  readonly expiresAt: number;
}

interface Client {
  readonly authority: URL;
  readonly clientId: string;
  readonly clientSecret: string | undefined;
  readonly scope: string;
  readonly origin: string;
}

const defaultScope = 'openid profile email offline_access';
const signInLifetime = 10 * 60_000;
const pendingSignInLimit = 10_000;
const assumedTokenLifetime = 5 * 60;
const loopbackHosts = new Set(['localhost', '127.0.0.1', '[::1]']);

export function providerSessions(upstream: string, settings: ProviderSettings, options: ProviderSessionsOptions = {}): ServerSessions {
  const target = apiOrigin(upstream);
  let client: Client | undefined;
  const now = options.now ?? Date.now;
  const sessionLifetime = options.sessionLifetime ?? 12 * 60 * 60_000;
  const onError =
    options.onError ??
    ((error: unknown) => {
      console.error('Signing in at the provider failed:', error);
    });
  const pending = new Map<string, PendingSignIn>();

  let configuration: Promise<oidc.Configuration> | undefined;

  function configured(): Client {
    client ??= readClient(settings);

    return client;
  }

  function discovered(): Promise<oidc.Configuration> {
    const { authority, clientId, clientSecret } = configured();
    const reaching = options.fetch === undefined ? undefined : { [oidc.customFetch]: options.fetch };

    configuration ??= oidc
      .discovery(authority, clientId, clientSecret, undefined, reaching)
      .catch((error: unknown) => {
        configuration = undefined;
        throw error;
      });

    return configuration;
  }

  function grantOf(tokens: oidc.TokenEndpointResponse & oidc.TokenEndpointResponseHelpers, previous?: ProviderGrant): ProviderGrant {
    const accessTokenExpiresAt = now() + (tokens.expiresIn() ?? assumedTokenLifetime) * 1000;
    const refreshToken = tokens.refresh_token ?? previous?.refreshToken;

    return {
      accessToken: tokens.access_token,
      accessTokenExpiresAt,
      refreshToken,
      idToken: tokens.id_token ?? previous?.idToken,
      expiresAt: previous?.expiresAt ?? (refreshToken === undefined ? accessTokenExpiresAt : now() + sessionLifetime),
    };
  }

  async function refresh(grant: ProviderGrant): Promise<Renewal<ProviderGrant>> {
    if (grant.refreshToken === undefined) {
      return 'kept';
    }

    try {
      return grantOf(await oidc.refreshTokenGrant(await discovered(), grant.refreshToken), grant);
    } catch (error) {
      return error instanceof oidc.ResponseBodyError ? 'ended' : 'kept';
    }
  }

  const store = sessionStore(refresh, { refreshAhead: options.refreshAhead ?? 30_000, now });

  async function signIn(request: IncomingMessage, response: ServerResponse): Promise<void> {
    const { origin, scope } = configured();
    let config: oidc.Configuration;

    try {
      config = await discovered();
    } catch (error) {
      onError(error);
      answerProblem(response, 502, 'Bad Gateway', 'The identity provider could not be reached.', providerUnreachableCode);
      return;
    }

    const codeVerifier = oidc.randomPKCECodeVerifier();
    const signingIn: PendingSignIn = {
      state: oidc.randomState(),
      nonce: oidc.randomNonce(),
      codeVerifier,
      returnTo: localPath(new URL(request.url ?? '/', origin).searchParams.get('returnTo'), origin),
      expiresAt: now() + signInLifetime,
    };
    const id = randomBytes(32).toString('base64url');

    for (const [stale, entry] of pending) {
      if (entry.expiresAt <= now() || pending.size >= pendingSignInLimit) {
        pending.delete(stale);
      }
    }

    pending.set(id, signingIn);

    const location = oidc.buildAuthorizationUrl(config, {
      ...options.authorizationParameters,
      redirect_uri: `${origin}${callbackPath}`,
      scope,
      code_challenge: await oidc.calculatePKCECodeChallenge(codeVerifier),
      code_challenge_method: 'S256',
      state: signingIn.state,
      nonce: signingIn.nonce,
    });

    response.writeHead(302, {
      location: location.href,
      'set-cookie': `${signInCookie}=${id}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=${String(signInLifetime / 1000)}`,
      'cache-control': 'no-store',
    });
    response.end();
  }

  async function callback(request: IncomingMessage, response: ServerResponse): Promise<void> {
    const { origin } = configured();
    const id = readCookie(request, signInCookie);
    const signingIn = id === undefined ? undefined : pending.get(id);
    const answered = new URL(request.url ?? '/', origin);
    const forgetSignIn = { 'set-cookie': clearedCookie(signInCookie) };

    if (id !== undefined) {
      pending.delete(id);
    }

    if (signingIn === undefined || signingIn.expiresAt <= now() || answered.searchParams.get('state') !== signingIn.state) {
      const detail = 'This sign-in was not started by this browser, or has expired: sign in again.';
      answerProblem(response, 400, 'Bad Request', detail, signInStateInvalidCode, forgetSignIn);
      return;
    }

    let grant: ProviderGrant;

    try {
      const tokens = await oidc.authorizationCodeGrant(await discovered(), new URL(`${callbackPath}${answered.search}`, origin), {
        pkceCodeVerifier: signingIn.codeVerifier,
        expectedState: signingIn.state,
        expectedNonce: signingIn.nonce,
        idTokenExpected: true,
      });
      grant = grantOf(tokens);
    } catch (error) {
      onError(error);

      if (error instanceof TypeError) {
        answerProblem(response, 502, 'Bad Gateway', 'The identity provider could not be reached.', providerUnreachableCode, forgetSignIn);
      } else {
        answerProblem(response, 400, 'Bad Request', 'The identity provider did not sign this browser in.', signInFailedCode, forgetSignIn);
      }

      return;
    }

    response.writeHead(302, {
      location: signingIn.returnTo,
      'set-cookie': [store.open(request, grant), clearedCookie(signInCookie)],
      'cache-control': 'no-store',
    });
    response.end();
  }

  async function signOut(request: IncomingMessage, response: ServerResponse): Promise<void> {
    const { origin } = configured();
    request.resume();
    const grant = store.end(request);
    let location = '/';

    if (grant !== undefined && configuration !== undefined) {
      const config = await configuration.catch(() => undefined);

      if (config?.serverMetadata().end_session_endpoint !== undefined) {
        location = oidc.buildEndSessionUrl(config, {
          post_logout_redirect_uri: `${origin}/`,
          ...(grant.idToken === undefined ? {} : { id_token_hint: grant.idToken }),
        }).href;
      }
    }

    const body = JSON.stringify({ location });

    response.writeHead(200, {
      'set-cookie': clearedCookie(sessionCookie),
      'content-type': 'application/json',
      'content-length': Buffer.byteLength(body),
      'cache-control': 'no-store',
    });
    response.end(body);
  }

  const routes: Readonly<Record<string, { readonly method: string; readonly handle: typeof signIn }>> = {
    [signInPath]: { method: 'GET', handle: signIn },
    [callbackPath]: { method: 'GET', handle: callback },
    [signOutPath]: { method: 'POST', handle: signOut },
  };

  const relay: ApiRelay = (request, response, next) => {
    const url = request.url ?? '/';
    const route = Object.hasOwn(routes, pathOf(url)) ? routes[pathOf(url)] : undefined;

    if (route === undefined && !isRelayedToApi(url)) {
      next();
      return;
    }

    if (refusedWithoutCsrfHeader(request, response)) {
      return;
    }

    if (route !== undefined && (request.method ?? 'GET').toUpperCase() !== route.method) {
      request.resume();
      response.writeHead(405, { allow: route.method, 'cache-control': 'no-store' });
      response.end();
      return;
    }

    (route === undefined ? relayWithSession(target, store, request, response) : route.handle(request, response)).catch(next);
  };

  return {
    relay,
    accessToken: (request, response) => accessTokenOf(store, request, response),
  };
}

export function checkProviderSettings(settings: ProviderSettings): void {
  readClient(settings);
}

function readClient(settings: ProviderSettings): Client {
  const problems: string[] = [];
  const authority = absoluteUrl(settings['EXTERNAL_AUTH_AUTHORITY']);
  const clientId = (settings['EXTERNAL_AUTH_INTERACTIVE_CLIENT_ID'] ?? '').trim();
  const clientSecret = (settings['EXTERNAL_AUTH_INTERACTIVE_CLIENT_SECRET'] ?? '').trim();
  const scope = (settings['EXTERNAL_AUTH_INTERACTIVE_SCOPE'] ?? '').trim() || defaultScope;
  const origin = absoluteUrl(settings['PUBLIC_ORIGIN']);

  if (authority?.protocol !== 'https:') {
    problems.push("EXTERNAL_AUTH_AUTHORITY must be the provider's https issuer URL.");
  }

  if (clientId === '') {
    problems.push('EXTERNAL_AUTH_INTERACTIVE_CLIENT_ID must name the client the provider registered for this app.');
  }

  if (!scope.split(/\s+/).includes('openid')) {
    problems.push('EXTERNAL_AUTH_INTERACTIVE_SCOPE must include openid.');
  }

  if (!isPublicOrigin(origin)) {
    problems.push(
      'PUBLIC_ORIGIN must be the origin browsers reach this app at, such as https://app.example.com:' +
        ' https, or http on localhost, since the session cookie is Secure.',
    );
  }

  if (authority === undefined || origin === undefined || problems.length > 0) {
    throw new Error(`This server signs people in at an identity provider and cannot start: ${problems.join(' ')}`);
  }

  return { authority, clientId, clientSecret: clientSecret === '' ? undefined : clientSecret, scope, origin: origin.origin };
}

function isPublicOrigin(origin: URL | undefined): origin is URL {
  const secure = origin?.protocol === 'https:' || (origin?.protocol === 'http:' && loopbackHosts.has(origin.hostname));

  return secure && origin.pathname === '/' && origin.search === '' && origin.hash === '';
}

function absoluteUrl(value: string | undefined): URL | undefined {
  try {
    return new URL((value ?? '').trim());
  } catch {
    return undefined;
  }
}

function localPath(returnTo: string | null, origin: string): string {
  const target = new URL(returnTo ?? '/', origin);

  return target.origin === origin ? `${target.pathname}${target.search}${target.hash}` : '/';
}
