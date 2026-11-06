import type { OutgoingHttpHeaders, ServerResponse } from 'node:http';

export function answerUncached(response: ServerResponse, status: number, headers: OutgoingHttpHeaders = {}, body?: string): void {
  const length = body === undefined ? {} : { 'content-length': Buffer.byteLength(body) };

  response.writeHead(status, { ...headers, ...length, 'cache-control': 'no-store' });
  response.end(body);
}

export function answerProblem(
  response: ServerResponse,
  status: number,
  title: string,
  detail: string,
  code: string,
  headers: OutgoingHttpHeaders = {},
): void {
  const body = JSON.stringify({ type: 'about:blank', title, status, detail, code });

  answerUncached(response, status, { ...headers, 'content-type': 'application/problem+json' }, body);
}
