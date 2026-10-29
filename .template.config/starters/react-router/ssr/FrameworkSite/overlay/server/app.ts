//#if (localAuth)
import { serverSessions } from "@app-template/api-client/server";
//#elseif (externalAuth)
import { checkProviderSettings, providerSessions } from "@app-template/api-client/server";
//#else
import { relayToApi } from "@app-template/api-client/server";
//#endif
import { createRequestHandler } from "@react-router/express";
import express from "express";
import { randomBytes } from "node:crypto";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import { RouterContextProvider } from "react-router";

import { renderContext, valueFromExpressContext } from "~/context";

const fingerprinted = /-(?=[A-Za-z0-9_-]*[A-Z0-9])[A-Za-z0-9_-]{8,}\.[a-z0-9]+$/;

const apiUpstream = process.env.API_UPSTREAM ?? "http://localhost:5187";
//#if (localAuth)
const sessions = serverSessions(apiUpstream);
//#elseif (externalAuth)
const sessions = providerSessions(apiUpstream, process.env);
//#endif

const contentSecurityPolicy = (nonce: string) =>
  [
    "default-src 'self'",
    "base-uri 'self'",
    "object-src 'none'",
    "form-action 'self'",
    "frame-ancestors 'none'",
    "img-src 'self' data:",
    import.meta.env.DEV ? "style-src 'self' 'unsafe-inline'" : "style-src 'self'",
    `script-src 'self' 'nonce-${nonce}'`,
    "connect-src 'self'",
  ].join("; ");

export const app = express();

app.disable("x-powered-by");

//#if (accounts)
app.use(sessions.relay);
//#else
app.use(relayToApi(apiUpstream));
//#endif

app.use((_request, response, next) => {
  response.setHeader("X-Content-Type-Options", "nosniff");
  response.setHeader("Referrer-Policy", "no-referrer");
  response.setHeader("Cross-Origin-Opener-Policy", "same-origin");
  next();
});

if (import.meta.env.PROD) {
  app.use(
    express.static(join(import.meta.dirname, "../client"), {
      index: false,
      redirect: false,
      cacheControl: false,
      setHeaders: (response, path) => {
        response.setHeader("Cache-Control", fingerprinted.test(path) ? "public, max-age=31536000, immutable" : "no-cache");
      },
    }),
  );
}

app.use((_request, response, next) => {
  const nonce = randomBytes(16).toString("base64");

  response.locals.nonce = nonce;
  response.setHeader("Content-Security-Policy", contentSecurityPolicy(nonce));
  response.setHeader("Cache-Control", "no-store");
  next();
});

app.use(
  createRequestHandler({
    build: () => import("virtual:react-router/server-build"),
//#if (accounts)
    getLoadContext(request, response) {
//#else
    getLoadContext(_request, response) {
//#endif
      const context = new RouterContextProvider();
      context.set(valueFromExpressContext, "Hello from Express");
      context.set(renderContext, {
        nonce: response.locals.nonce,
        apiUpstream,
//#if (accounts)
        accessToken: () => sessions.accessToken(request, response),
//#endif
      });
      return context;
    },
  }),
);

if (process.argv[1] !== undefined && import.meta.url === pathToFileURL(process.argv[1]).href) {
//#if (externalAuth)
  checkProviderSettings(process.env);

//#endif
  const port = process.env.PORT || 3000;
  app.listen(port, (error) => {
    if (error) {
      throw error;
    }

    console.log(`Server is running on http://localhost:${port}`);
  });
}
