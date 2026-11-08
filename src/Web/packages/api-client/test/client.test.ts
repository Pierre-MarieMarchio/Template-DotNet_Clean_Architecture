import { describe, expect, it } from 'vitest';
import { ApiError, createApiClient, unreadableResponseCode, unwrap, type ApiClientOptions } from '../src/index.ts';

interface Note {
  readonly id: string;
  readonly title: string;
}

interface TestPaths {
  '/api/v1/notes/{id}': {
    parameters: { query?: never; header?: never; path: { id: string }; cookie?: never };
    get: {
      parameters: { query?: never; header?: never; path: { id: string }; cookie?: never };
      requestBody?: never;
      responses: {
        200: { headers: Record<string, unknown>; content: { 'application/json': Note } };
        404: { headers: Record<string, unknown>; content: { 'application/problem+json': unknown } };
      };
    };
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
}

function answering(status: number, body: string, contentType: string, seen: Request[] = []) {
  return (request: Request): Promise<Response> => {
    seen.push(request);
    return Promise.resolve(new Response(body, { status, headers: { 'Content-Type': contentType } }));
  };
}

function client(options: ApiClientOptions) {
  return createApiClient<TestPaths>({ baseUrl: 'http://api.test', ...options });
}

function getNote(options: ApiClientOptions) {
  return unwrap(client(options).GET('/api/v1/notes/{id}', { params: { path: { id: 'n-1' } } }));
}

describe('unwrap', () => {
  it('returns the body of a successful answer', async () => {
    const note = await getNote({ fetch: answering(200, '{"id":"n-1","title":"Groceries"}', 'application/json') });

    expect(note).toEqual({ id: 'n-1', title: 'Groceries' });
  });

  it('throws an ApiError carrying the problem code and field errors', async () => {
    const problem = {
      type: 'https://errors.example/validation.failed',
      title: 'Invalid request',
      status: 400,
      code: 'validation.failed',
      traceId: '00-abc-01',
      errors: { title: ['The title is required.'] },
    };

    const error = await getNote({ fetch: answering(400, JSON.stringify(problem), 'application/problem+json') })
      .catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 400, code: 'validation.failed', message: 'Invalid request' });
    expect((error as ApiError).errorsFor('title')).toEqual(['The title is required.']);
    expect((error as ApiError).errorsFor('body')).toEqual([]);
    expect((error as ApiError).problem.traceId).toBe('00-abc-01');
  });

  it('prefers the problem detail over its title as the message', async () => {
    const problem = { title: 'Not found', detail: 'No note n-1.', status: 404, code: 'notes.notFound' };

    await expect(getNote({ fetch: answering(404, JSON.stringify(problem), 'application/problem+json') }))
      .rejects.toThrow('No note n-1.');
  });

  it('maps an answer that is no problem document to the unreadable-response code', async () => {
    const error = await getNote({ fetch: answering(502, '<html>Bad Gateway</html>', 'text/html') })
      .catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 502, code: unreadableResponseCode });
    expect((error as ApiError).fieldErrors).toEqual({});
  });

  it('refuses a body whose errors are not lists of messages as a problem document', async () => {
    const body = { code: 'validation.failed', errors: { title: 'required' } };

    await expect(getNote({ fetch: answering(400, JSON.stringify(body), 'application/problem+json') }))
      .rejects.toMatchObject({ code: unreadableResponseCode });
  });
});

describe('the access token seam', () => {
  it('sends the current access token as a bearer token', async () => {
    const seen: Request[] = [];

    await getNote({ fetch: answering(200, '{"id":"n-1","title":"t"}', 'application/json', seen), accessToken: () => 'token-1' });

    expect(seen[0]?.headers.get('Authorization')).toBe('Bearer token-1');
  });

  it('awaits an asynchronous token source', async () => {
    const seen: Request[] = [];

    await getNote({
      fetch: answering(200, '{"id":"n-1","title":"t"}', 'application/json', seen),
      accessToken: () => Promise.resolve('token-2'),
    });

    expect(seen[0]?.headers.get('Authorization')).toBe('Bearer token-2');
  });

  it('sends no authorization header while there is no token', async () => {
    const seen: Request[] = [];

    await getNote({ fetch: answering(200, '{"id":"n-1","title":"t"}', 'application/json', seen), accessToken: () => undefined });

    expect(seen[0]?.headers.has('Authorization')).toBe(false);
  });

  it('runs the extra middleware after the token is set', async () => {
    const seen: Request[] = [];
    let authorizationSeenByMiddleware: string | null = null;

    await getNote({
      fetch: answering(200, '{"id":"n-1","title":"t"}', 'application/json', seen),
      accessToken: () => 'token-3',
      middleware: [
        {
          onRequest({ request }) {
            authorizationSeenByMiddleware = request.headers.get('Authorization');
            return request;
          },
        },
      ],
    });

    expect(authorizationSeenByMiddleware).toBe('Bearer token-3');
  });
});
