import { existsSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';

export interface WebApp {
  readonly name: string;
  readonly origin: string;
}

const projectFileName = 'cleanarch.json';
const portVariable = /^WEB_([A-Z0-9_]+)_HOST_PORT=(\d+)\s*$/;

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

export function readPorts(root: string, environment: Record<string, string | undefined>): Map<string, string> {
  const named = environment['SMOKE_ENV_FILE'];
  const file = named !== undefined && named !== '' ? resolve(root, named) : ['.env', '.env.example'].map((name) => join(root, name)).find(existsSync);
  const ports = portsOf(file === undefined ? [] : readFileSync(file, 'utf8').split(/\r?\n/));
  const overrides = Object.entries(environment).map(([key, value]) => `${key}=${value ?? ''}`);
  for (const [name, port] of portsOf(overrides)) {
    ports.set(name, port);
  }
  return ports;
}

export function discoverApps(root: string, environment: Record<string, string | undefined>): WebApp[] {
  const recorded = JSON.parse(readFileSync(join(root, projectFileName), 'utf8')) as { web?: { apps?: { name: string }[] } };
  const ports = readPorts(root, environment);
  return (recorded.web?.apps ?? []).map(({ name }) => {
    const port = ports.get(squash(name));
    if (port === undefined) {
      throw new Error(`No WEB_*_HOST_PORT in the stack's environment names a port for the web app ${name}.`);
    }
    return { name, origin: `http://127.0.0.1:${port}` };
  });
}
