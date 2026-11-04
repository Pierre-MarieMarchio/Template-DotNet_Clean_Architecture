import { existsSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';

export interface WebApp {
  readonly name: string;
  readonly origin: string;
  readonly signsInFromThePage: boolean;
}

export interface StandInProvider {
  readonly authority: string;
  readonly origin: string;
  readonly discovery: string;
}

const projectFileName = 'cleanarch.json';
const portVariable = /^WEB_([A-Z0-9_]+)_HOST_PORT=(\d+)\s*$/;
const authorityVariable = /^EXTERNAL_AUTH_AUTHORITY=(.*?)\s*$/;
const unresolvableDomain = '.invalid';

export function findProjectRoot(from: string): string {
  for (let folder = resolve(from); ; folder = dirname(folder)) {
    if (existsSync(join(folder, projectFileName))) {
      return folder;
    }
    if (dirname(folder) === folder) {
      throw new Error(`No ${projectFileName} above ${from}: the smoke runs inside a generated project.`);
    }
  }
}

function squash(name: string): string {
  return name.replaceAll('_', '').toUpperCase();
}

function portsOf(lines: Iterable<string>): Map<string, string> {
  const ports = new Map<string, string>();
  for (const line of lines) {
    const found = portVariable.exec(line);
    if (found?.[1] !== undefined && found[2] !== undefined) {
      ports.set(squash(found[1]), found[2]);
    }
  }
  return ports;
}

function stackLines(root: string, environment: Record<string, string | undefined>): { file: string[]; overrides: string[] } {
  const named = environment['SMOKE_ENV_FILE'];
  const file = named !== undefined && named !== '' ? resolve(root, named) : ['.env', '.env.example'].map((name) => join(root, name)).find(existsSync);
  return {
    file: file === undefined ? [] : readFileSync(file, 'utf8').split(/\r?\n/),
    overrides: Object.entries(environment).map(([key, value]) => `${key}=${value ?? ''}`),
  };
}

export function readPorts(root: string, environment: Record<string, string | undefined>): Map<string, string> {
  const { file, overrides } = stackLines(root, environment);
  const ports = portsOf(file);
  for (const [name, port] of portsOf(overrides)) {
    ports.set(name, port);
  }
  return ports;
}

export function standInProvider(root: string, environment: Record<string, string | undefined>): StandInProvider | undefined {
  const { file, overrides } = stackLines(root, environment);
  const authority = [...file, ...overrides].map((line) => authorityVariable.exec(line)?.[1]).filter((value) => value !== undefined).at(-1);
  if (authority === undefined || !URL.canParse(authority)) {
    return undefined;
  }
  const { hostname, origin } = new URL(authority);
  if (!hostname.endsWith(unresolvableDomain)) {
    return undefined;
  }
  const discovery = new URL('.well-known/openid-configuration', authority.endsWith('/') ? authority : `${authority}/`).href;
  return { authority, origin, discovery };
}

export function discoveryDocument({ authority, origin }: StandInProvider): Record<string, unknown> {
  return {
    issuer: authority,
    authorization_endpoint: `${origin}/authorize`,
    token_endpoint: `${origin}/token`,
    jwks_uri: `${origin}/jwks`,
    end_session_endpoint: `${origin}/logout`,
    response_types_supported: ['code'],
    subject_types_supported: ['public'],
    id_token_signing_alg_values_supported: ['RS256'],
    code_challenge_methods_supported: ['S256'],
  };
}

export function discoverApps(root: string, environment: Record<string, string | undefined>): WebApp[] {
  const recorded = JSON.parse(readFileSync(join(root, projectFileName), 'utf8')) as { web?: { apps?: { name: string; framework?: string }[] } };
  const ports = readPorts(root, environment);
  return (recorded.web?.apps ?? []).map(({ name, framework }) => {
    const port = ports.get(squash(name));
    if (port === undefined) {
      throw new Error(`No WEB_*_HOST_PORT in the stack's environment names a port for the web app ${name}.`);
    }
    return { name, origin: `http://127.0.0.1:${port}`, signsInFromThePage: framework === 'blazor-wasm' };
  });
}
