import { describe, expectTypeOf, it } from 'vitest';
import type { CookieModeMismatch, CookieModePath, CookieModeRoute } from '../src/protocol.ts';

interface Operation<Header> {
  parameters: { query?: never; header?: Header; path?: never; cookie?: never };
}

interface SchemaWithoutCookieMode {
  '/api/v1/notes': { get: Operation<never>; post: Operation<never> };
}

interface SchemaWithCookieMode {
  '/api/v1/auth/refresh': { get?: never; post: Operation<{ 'X-Refresh-Token-Mode'?: string }> };
  '/api/v1/auth/register': { get?: never; post: Operation<never> };
  '/api/v1/auth/me': { get: Operation<{ 'X-Refresh-Token-Mode'?: string }>; post?: never };
}

describe('the cookie-mode routes the schema declares', () => {
  it('ask nothing of the list when the schema holds none, as without local accounts', () => {
    expectTypeOf<CookieModeRoute<SchemaWithoutCookieMode>>().toBeNever();
    expectTypeOf<CookieModeMismatch<SchemaWithoutCookieMode, CookieModePath>>().toBeNever();
  });

  it('are the POST routes that take the header, and a list must name each of them alone', () => {
    expectTypeOf<CookieModeRoute<SchemaWithCookieMode>>().toEqualTypeOf<'/api/v1/auth/refresh'>();
    expectTypeOf<CookieModeMismatch<SchemaWithCookieMode, '/api/v1/auth/refresh'>>().toBeNever();
    expectTypeOf<CookieModeMismatch<SchemaWithCookieMode, '/api/v1/auth/refresh' | '/api/v1/auth/me'>>().toEqualTypeOf<'/api/v1/auth/me'>();
    expectTypeOf<CookieModeMismatch<SchemaWithCookieMode, never>>().toEqualTypeOf<'/api/v1/auth/refresh'>();
  });
});
