import type { IncomingMessage, OutgoingHttpHeaders, ServerResponse } from 'node:http';
import { bearer, csrfHeader, csrfHeaderKey, csrfHeaderName, refreshTokenModeHeaderKey } from '../protocol.ts';
import { answerProblem, answerUncached } from './answers.ts';
import { forward, isRelayedToApi, pathOf, type ApiRelay } from './relay.ts';
import type { Grant, SessionStore } from './store.ts';

export const csrfRefusalCode = 'session.csrfHeaderRequired';

export const browserCredentials = ['authorization', 'cookie', refreshTokenModeHeaderKey, csrfHeaderKey];

export interface ServerSessions {
  readonly relay: ApiRelay;
  accessToken(request: IncomingMessage, response?: ServerResponse): Promise<string | undefined>;
}

export interface SessionRoute {
  readonly method: string;
  handle(request: IncomingMessage, response: ServerResponse): Promise<void>;
}

const safeMethods = new Set(['GET', 'HEAD']);

export function sessionServer<G extends Grant>(
  target: URL,
  store: SessionStore<G>,
  routes: Readonly<Record<string, SessionRoute>>,
): ServerSessions {
  function handled(request: IncomingMessage, response: ServerResponse, url: string, route: SessionRoute | undefined): Promise<void> {
    if (route?.method === methodOf(request)) {
      return route.handle(request, response);
    }

    if (route === undefined || isRelayedToApi(url)) {
      return relayWithSession(target, store, request, response);
    }

    request.resume();
    answerUncached(response, 405, { allow: route.method });
    return Promise.resolve();
  }

  const relay: ApiRelay = (request, response, next) => {
    const url = request.url ?? '/';
    const path = pathOf(url);
    const route = Object.hasOwn(routes, path) ? routes[path] : undefined;

    if (route === undefined && !isRelayedToApi(url)) {
      next();
      return;
    }

    if (refusedWithoutCsrfHeader(request, response)) {
      return;
    }

    handled(request, response, url, route).catch(next);
  };

  return {
    relay,
    accessToken: (request, response) => accessTokenOf(store, request, response),
  };
}

function methodOf(request: IncomingMessage): string {
  return (request.method ?? 'GET').toUpperCase();
}

function refusedWithoutCsrfHeader(request: IncomingMessage, response: ServerResponse): boolean {
  if (safeMethods.has(methodOf(request)) || request.headers[csrfHeaderKey] === csrfHeader[csrfHeaderName]) {
    return false;
  }

  request.resume();
  answerProblem(response, 403, 'Forbidden', `A request that changes state carries the ${csrfHeaderName}: ${csrfHeader[csrfHeaderName]} header.`, csrfRefusalCode);

  return true;
}

async function relayWithSession<G extends Grant>(target: URL, store: SessionStore<G>, request: IncomingMessage, response: ServerResponse): Promise<void> {
  const found = await store.find(request);
  const answered: OutgoingHttpHeaders = found.cookie === undefined ? {} : { 'set-cookie': found.cookie };
  const add: Record<string, string> = found.grant === undefined ? {} : { authorization: bearer(found.grant.accessToken) };

  forward(target, request, response, { drop: browserCredentials, add, answer: answered });
}

async function accessTokenOf<G extends Grant>(store: SessionStore<G>, request: IncomingMessage, response?: ServerResponse): Promise<string | undefined> {
  const found = await store.find(request);

  if (found.cookie !== undefined && response !== undefined && !response.headersSent) {
    response.appendHeader('set-cookie', found.cookie);
  }

  return found.grant?.accessToken;
}
