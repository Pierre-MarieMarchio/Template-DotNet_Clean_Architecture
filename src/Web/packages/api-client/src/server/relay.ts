import { request as httpRequest, type IncomingHttpHeaders, type IncomingMessage, type OutgoingHttpHeaders, type ServerResponse } from 'node:http';
import { request as httpsRequest } from 'node:https';

export type NextHandler = (error?: unknown) => void;

export type ApiRelay = (request: IncomingMessage, response: ServerResponse, next: NextHandler) => void;

export interface Forwarding {
  readonly drop?: readonly string[];
  readonly add?: Readonly<Record<string, string>>;
  readonly answer?: OutgoingHttpHeaders;
}

const hopByHopHeaders = new Set([
  'connection',
  'keep-alive',
  'proxy-authenticate',
  'proxy-authorization',
  'proxy-connection',
  'te',
  'trailer',
  'transfer-encoding',
  'upgrade',
]);

export function pathOf(url: string): string {
  return url.split('?', 1)[0] ?? '';
}

export function isRelayedToApi(url: string): boolean {
  const path = pathOf(url);

  return path.startsWith('/api/') || path === '/health';
}

export function apiOrigin(upstream: string): URL {
  const target = new URL(upstream);

  if (target.pathname !== '/' || target.search !== '' || target.hash !== '') {
    throw new Error(`The API's address must be an origin, such as http://api:8080, not ${upstream}.`);
  }

  return target;
}

export function relayToApi(upstream: string): ApiRelay {
  const target = apiOrigin(upstream);

  return (request, response, next) => {
    if (!isRelayedToApi(request.url ?? '/')) {
      next();
      return;
    }

    forward(target, request, response, {});
  };
}

export function forward(target: URL, request: IncomingMessage, response: ServerResponse, forwarding: Forwarding): void {
  const send = target.protocol === 'https:' ? httpsRequest : httpRequest;
  const dropped = new Set(forwarding.drop);
  const headers: OutgoingHttpHeaders = {
    ...Object.fromEntries(Object.entries(forwardedHeaders(request)).filter(([name]) => !dropped.has(name))),
    ...forwarding.add,
  };

  const forwarded = send(
    {
      protocol: target.protocol,
      hostname: target.hostname,
      port: target.port,
      method: request.method ?? 'GET',
      path: request.url ?? '/',
      headers,
    },
    (answer) => {
      response.writeHead(answer.statusCode ?? 502, { ...withoutHopByHop(answer.headers), ...forwarding.answer });
      answer.pipe(response);
    },
  );

  forwarded.on('error', () => {
    if (!response.headersSent) {
      response.writeHead(502, forwarding.answer);
    }

    response.end();
  });
  response.on('close', () => forwarded.destroy());
  request.pipe(forwarded);
}

export function forwardedHeaders(request: IncomingMessage): IncomingHttpHeaders {
  const headers = withoutHopByHop(request.headers);
  const address = request.socket.remoteAddress;
  const chain = request.headers['x-forwarded-for'];

  if (address !== undefined) {
    headers['x-forwarded-for'] = chain === undefined ? address : `${String(chain)}, ${address}`;
  }

  headers['x-forwarded-proto'] = 'encrypted' in request.socket ? 'https' : 'http';

  if (request.headers.host !== undefined) {
    headers['x-forwarded-host'] = request.headers.host;
  }

  return headers;
}

export function withoutHopByHop(headers: IncomingHttpHeaders): IncomingHttpHeaders {
  const named = new Set(
    (headers.connection ?? '')
      .split(',')
      .map((name) => name.trim().toLowerCase())
      .filter((name) => name !== ''),
  );

  return Object.fromEntries(
    Object.entries(headers).filter(([name]) => !hopByHopHeaders.has(name) && !named.has(name)),
  );
}
