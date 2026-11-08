import { expect, test, type ConsoleMessage } from '@playwright/test';
import { discoverApps, discoveryDocument, findProjectRoot, standInProvider } from '../src/apps.ts';

const root = findProjectRoot(import.meta.dirname);
const apps = discoverApps(root, process.env);
const provider = standInProvider(root, process.env);
const relayRoute = '/api/v1/relay-check';
const relayCodes: Record<number, string> = { 404: 'route.notFound', 401: 'auth.required' };

interface Relay {
  status: number;
  contentType: string | null;
  code: unknown;
}

function isRelayProbe(message: ConsoleMessage, origin: string): boolean {
  return message.location().url === origin + relayRoute;
}

test('the project records a web app to drive', () => {
  expect(apps.length, 'cleanarch.json records no web app').toBeGreaterThan(0);
});

for (const app of apps) {
  test(`${app.name} loads, breaks no policy and reaches the API through its relay`, async ({ page }) => {
    const problems: string[] = [];
    const providerCalls: string[] = [];

    if (provider !== undefined) {
      await page.route(`${provider.origin}/**`, async (route) => {
        const url = route.request().url();
        providerCalls.push(url);
        if (url === provider.discovery) {
          await route.fulfill({ json: discoveryDocument(provider), headers: { 'access-control-allow-origin': app.origin } });
        } else {
          await route.abort();
        }
      });
    }

    await page.exposeFunction('reportViolation', (directive: string, blocked: string) => {
      problems.push(`Content-Security-Policy violation: ${directive} blocked ${blocked}`);
    });
    await page.addInitScript(() => {
      const report = (window as unknown as Record<string, (directive: string, blocked: string) => void>)['reportViolation'];
      document.addEventListener('securitypolicyviolation', (event) => {
        report?.(event.violatedDirective, event.blockedURI || event.sample);
      });
    });
    page.on('console', (message) => {
      if (message.type() === 'error' && !isRelayProbe(message, app.origin)) {
        problems.push(`Console error: ${message.text()}`);
      }
    });
    page.on('pageerror', (error) => {
      problems.push(`Uncaught: ${error.message}`);
    });

    const response = await page.goto(`${app.origin}/`);
    expect(response?.ok(), `${app.origin}/ answered ${String(response?.status())}`).toBe(true);
    await expect(page.locator('body')).toHaveText(/\S/, { useInnerText: true });
    await page.waitForLoadState('networkidle');

    const relay = await page.evaluate(async (route): Promise<Relay> => {
      const answer = await fetch(route);
      const body = (await answer.json()) as { code?: unknown };
      return { status: answer.status, contentType: answer.headers.get('content-type'), code: body.code };
    }, relayRoute);
    expect(relay.contentType, 'the relay answered something other than the API').toContain('application/problem+json');
    expect(relay.code, `the relay answered ${String(relay.status)}, not the API's 404 or its 401`).toBe(relayCodes[relay.status]);

    expect(problems, `${app.name} broke in the browser`).toEqual([]);
    if (provider !== undefined && app.signsInFromThePage) {
      expect(providerCalls, `${app.name} never read the discovery document of ${provider.authority}`).toContain(provider.discovery);
    }
  });
}
