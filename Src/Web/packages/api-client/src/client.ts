import createClient, { type Client, type Middleware } from 'openapi-fetch';
import type { paths } from './generated/schema.ts';
import { toApiError } from './problem-details.ts';

export type AccessTokenProvider = () => string | undefined | Promise<string | undefined>;

export interface ApiClientOptions {
  readonly baseUrl?: string;
  readonly fetch?: (request: Request) => Promise<Response>;
  readonly accessToken?: AccessTokenProvider;
  readonly middleware?: readonly Middleware[];
}

export type ApiClient<Paths extends object = paths> = Client<Paths>;

export function createApiClient<Paths extends object = paths>(options: ApiClientOptions = {}): ApiClient<Paths> {
  const client = createClient<Paths>({
    baseUrl: options.baseUrl ?? '',
    ...(options.fetch === undefined ? {} : { fetch: options.fetch }),
  });

  if (options.accessToken !== undefined) {
    client.use(bearerToken(options.accessToken));
  }

  for (const middleware of options.middleware ?? []) {
    client.use(middleware);
  }

  return client;
}

export function bearerToken(accessToken: AccessTokenProvider): Middleware {
  return {
    async onRequest({ request }) {
      const token = await accessToken();

      if (token !== undefined && token !== '') {
        request.headers.set('Authorization', `Bearer ${token}`);
      }

      return request;
    },
  };
}

export interface ApiResult<Data> {
  readonly data?: Data;
  readonly error?: unknown;
  readonly response: Response;
}

export async function unwrap<Data>(call: Promise<ApiResult<Data>>): Promise<Data> {
  const { data, error, response } = await call;

  if (error !== undefined || !response.ok) {
    throw toApiError(response, error);
  }

  return data as Data;
}
