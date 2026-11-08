import { createRequire } from 'node:module';
import { describe, expect, it } from 'vitest';

interface RedoclyUtils {
  readFileFromUrl(
    url: string,
    config: { headers: { name: string; value: string; matches: string }[]; customFetch: typeof fetch },
  ): Promise<{ body: string }>;
}

const fromOpenapiTypescript = createRequire(createRequire(import.meta.url).resolve('openapi-typescript'));
const utils = fromOpenapiTypescript('@redocly/openapi-core/lib/utils') as RedoclyUtils;

describe('@redocly/openapi-core under the minimatch override', () => {
  it('matches a header pattern against a URL', async () => {
    const sent: Record<string, string>[] = [];
    const customFetch = ((_url: string, init?: RequestInit) => {
      sent.push(init?.headers as Record<string, string>);
      return Promise.resolve(new Response('{}', { headers: { 'content-type': 'application/json' } }));
    }) as unknown as typeof fetch;

    await utils.readFileFromUrl('https://spec.test/api.json', {
      headers: [
        { name: 'X-Token', value: 'secret', matches: 'https://spec.test/**' },
        { name: 'X-Other', value: 'nope', matches: 'https://other.test/**' },
      ],
      customFetch,
    });

    expect(sent).toEqual([{ 'X-Token': 'secret' }]);
  });
});
