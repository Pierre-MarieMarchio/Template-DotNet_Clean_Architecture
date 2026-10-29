import { mergeApplicationConfig, ApplicationConfig, CSP_NONCE, inject, REQUEST_CONTEXT } from '@angular/core';
import { provideServerRendering, withRoutes } from '@angular/ssr';
import { appConfig } from './app.config';
import { serverRoutes } from './app.routes.server';

const serverConfig: ApplicationConfig = {
  providers: [
    provideServerRendering(withRoutes(serverRoutes)),
    {
      provide: CSP_NONCE,
      useFactory: () => (inject(REQUEST_CONTEXT, { optional: true }) as { nonce?: string } | null)?.nonce ?? null,
    },
  ]
};

export const config = mergeApplicationConfig(appConfig, serverConfig);
