import { createServer, request, type IncomingHttpHeaders, type Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { afterEach, describe, expect, it } from 'vitest';
import { isRelayedToApi, relayToApi } from '../src/server.ts';

interface Received {
  readonly method: string | undefined;
  readonly url: string | undefined;
  readonly headers: IncomingHttpHeaders;
  readonly body: string;
}

const servers: Server[] = [];

afterEach(async () => {
  await Promise.all(servers.splice(0).map((server) => new Promise((resolve) => server.close(resolve))));
});

async function listen(server: Server): Promise<string> {
  servers.push(server);
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));

  return `http://127.0.0.1:${String((server.address() as AddressInfo).port)}`;
}

async function api(received: Received[]): Promise<string> {
  return listen(
    createServer((request, response) => {
      const chunks: Buffer[] = [];

      request.on('data', (chunk: Buffer) => chunks.push(chunk));
      request.on('end', () => {
        received.push({ method: request.method, url: request.url, headers: request.headers, body: Buffer.concat(chunks).toString() });
        response.writeHead(201, [
          ['Content-Type', 'application/problem+json'],
          ['Set-Cookie', 'first=1; HttpOnly'],
          ['Set-Cookie', 'second=2; HttpOnly'],
          ['X-Answered-By', 'api'],
        ]);
        response.end('{"code":"relayed"}');
      });
    }),
  );
}

async function site(upstream: string): Promise<string> {
  const relay = relayToApi(upstream);

  return listen(
    createServer((request, response) => {
      relay(request, response, () => {
        response.writeHead(200, { 'Content-Type': 'text/html' });
        response.end('<html>page</html>');
      });
    }),
  );
}

describe('relayToApi', () => {
  it('relays a call to the API with its method, path, query and body, and returns its answer whole', async () => {
    const received: Received[] = [];
    const base = await site(await api(received));

    const answer = await fetch(`${base}/api/v1/notes?page=2`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: '{"title":"t"}',
    });

    expect(answer.status).toBe(201);
    expect(await answer.text()).toBe('{"code":"relayed"}');
    expect(answer.headers.get('x-answered-by')).toBe('api');
    expect(answer.headers.getSetCookie()).toEqual(['first=1; HttpOnly', 'second=2; HttpOnly']);
    expect(received).toHaveLength(1);
    expect(received[0]).toMatchObject({ method: 'POST', url: '/api/v1/notes?page=2', body: '{"title":"t"}' });
  });

  it('keeps the browser host and names the browser in the forwarded headers', async () => {
    const received: Received[] = [];
    const base = await site(await api(received));

    await fetch(`${base}/health`, { headers: { 'X-Forwarded-For': '203.0.113.7' } });

    const headers = received[0]?.headers;
    expect(headers?.host).toBe(new URL(base).host);
    expect(headers?.['x-forwarded-host']).toBe(new URL(base).host);
    expect(headers?.['x-forwarded-proto']).toBe('http');
    expect(headers?.['x-forwarded-for']).toBe('203.0.113.7, 127.0.0.1');
  });

  it('forwards no hop-by-hop header, nor one the connection header names', async () => {
    const received: Received[] = [];
    const base = await site(await api(received));

    await new Promise((resolve, reject) => {
      request(
        `${base}/api/v1/notes`,
        { headers: { 'Proxy-Authorization': 'Basic c2VjcmV0', Connection: 'x-private', 'X-Private': 'secret', 'X-Kept': 'kept' } },
        (answer) => {
          answer.resume();
          answer.on('end', resolve);
        },
      )
        .on('error', reject)
        .end();
    });

    const headers = received[0]?.headers;
    expect(headers?.['proxy-authorization']).toBeUndefined();
    expect(headers?.['x-private']).toBeUndefined();
    expect(headers?.['x-kept']).toBe('kept');
  });

  it('leaves every other path to the next handler', async () => {
    const received: Received[] = [];
    const base = await site(await api(received));

    for (const path of ['/', '/apis', '/api', '/healthz', '/notes/api/v1']) {
      const answer = await fetch(`${base}${path}`);

      expect(await answer.text()).toBe('<html>page</html>');
    }

    expect(received).toHaveLength(0);
  });

  it('answers 502 when the API does not answer', async () => {
    const stopped = createServer();
    const unreachable = await listen(stopped);
    servers.pop();
    await new Promise((resolve) => stopped.close(resolve));
    const base = await site(unreachable);

    const answer = await fetch(`${base}/api/v1/notes`);

    expect(answer.status).toBe(502);
  });

  it('refuses an address that is not an origin', () => {
    expect(() => relayToApi('http://api:8080/base')).toThrow(/must be an origin/);
  });
});

describe('isRelayedToApi', () => {
  it('names the API routes and its health check, with or without a query', () => {
    expect(isRelayedToApi('/api/v1/notes?page=1')).toBe(true);
    expect(isRelayedToApi('/health?probe=1')).toBe(true);
    expect(isRelayedToApi('/health/ready')).toBe(false);
  });
});
