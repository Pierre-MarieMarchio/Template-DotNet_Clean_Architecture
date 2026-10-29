import { request as httpRequest, type IncomingHttpHeaders, type IncomingMessage, type ServerResponse } from 'node:http';
import { request as httpsRequest } from 'node:https';

export type NextHandler = (error?: unknown) => void;

export type ApiRelay = (request: IncomingMessage, response: ServerResponse, next: NextHandler) => void;

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

export function isRelayedToApi(url: string): boolean {
  const path = url.split('?', 1)[0] ?? '';

  return path.startsWith('/api/') || path === '/health';
}

export function relayToApi(upstream: string): ApiRelay {
  const target = new URL(upstream);

  if (target.pathname !== '/' || target.search !== '' || target.hash !== '') {
    throw new Error(`The API's address must be an origin, such as http://api:8080, not ${upstream}.`);
  }

  const send = target.protocol === 'https:' ? httpsRequest : httpRequest;

  return (request, response, next) => {
    const url = request.url ?? '/';

    if (!isRelayedToApi(url)) {
      next();
      return;
    }

    const forwarded = send(
      {
        protocol: target.protocol,
        hostname: target.hostname,
        port: target.port,
        method: request.method ?? 'GET',
        path: url,
        headers: forwardedHeaders(request),
      },
      (answer) => {
        response.writeHead(answer.statusCode ?? 502, withoutHopByHop(answer.headers));
        answer.pipe(response);
      },
    );

    forwarded.on('error', () => {
      if (!response.headersSent) {
        response.writeHead(502);
      }

      response.end();
    });
    response.on('close', () => forwarded.destroy());
    request.pipe(forwarded);
  };
}

function forwardedHeaders(request: IncomingMessage): IncomingHttpHeaders {
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

function withoutHopByHop(headers: IncomingHttpHeaders): IncomingHttpHeaders {
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
