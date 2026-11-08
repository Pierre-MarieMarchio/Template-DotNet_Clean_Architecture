import { randomBytes } from 'node:crypto';
import type { IncomingMessage } from 'node:http';

export function newId(): string {
  return randomBytes(32).toString('base64url');
}

export function cookie(name: string, value: string, maxAgeSeconds: number): string {
  return `${name}=${value}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=${String(maxAgeSeconds)}`;
}

export function clearedCookie(name: string): string {
  return cookie(name, '', 0);
}

export function readCookie(request: IncomingMessage, name: string): string | undefined {
  for (const pair of (request.headers.cookie ?? '').split(';')) {
    const [candidate, value] = pair.trim().split('=', 2);

    if (candidate === name && value !== undefined && value !== '') {
      return value;
    }
  }

  return undefined;
}
