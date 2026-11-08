import type { paths } from './generated/schema.ts';

export const csrfHeaderName = 'X-CSRF';
export const csrfHeaderKey = csrfHeaderName.toLowerCase();
export const csrfHeader: Readonly<Record<typeof csrfHeaderName, '1'>> = Object.freeze({ [csrfHeaderName]: '1' });

export const refreshTokenModeHeaderName = 'X-Refresh-Token-Mode';
export const refreshTokenModeHeaderKey = refreshTokenModeHeaderName.toLowerCase();
export const refreshTokenModeHeader: Readonly<Record<typeof refreshTokenModeHeaderName, 'cookie'>> = Object.freeze({
  [refreshTokenModeHeaderName]: 'cookie',
});

export function bearer(accessToken: string): string {
  return `Bearer ${accessToken}`;
}

export const grantPaths = [
  '/api/v1/auth/login',
  '/api/v1/auth/login/two-factor',
  '/api/v1/auth/login/external',
  '/api/v1/auth/passkeys/sign-in',
] as const;

export const refreshPath = '/api/v1/auth/refresh';
export const logoutPath = '/api/v1/auth/logout';

const cookieModeRoutes = [...grantPaths, refreshPath, logoutPath] as const;

export type CookieModePath = (typeof cookieModeRoutes)[number];

export const cookieModePaths: readonly string[] = Object.freeze(cookieModeRoutes);

type PostHeaders<Route> = Route extends { post: { parameters: { header?: infer Headers } } } ? NonNullable<Headers> : never;

export type CookieModeRoute<Paths> = {
  [Path in keyof Paths]: [PostHeaders<Paths[Path]>] extends [never]
    ? never
    : typeof refreshTokenModeHeaderName extends keyof PostHeaders<Paths[Path]>
      ? Path
      : never;
}[keyof Paths];

export type CookieModeMismatch<Paths, Listed> = [CookieModeRoute<Paths>] extends [never]
  ? never
  : Exclude<Listed, CookieModeRoute<Paths>> | Exclude<CookieModeRoute<Paths>, Listed>;

type Agreeing<Mismatch extends never> = Mismatch;

export type CookieModePathsAgreeWithTheSchema = Agreeing<CookieModeMismatch<paths, CookieModePath>>;
