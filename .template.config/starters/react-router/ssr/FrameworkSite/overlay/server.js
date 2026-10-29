import express from "express";
import { createServer } from "node:http";

const PORT = Number.parseInt(process.env.PORT || "5173");

const app = express();
const server = createServer(app);
const viteDevServer = await import("vite").then((vite) =>
  vite.createServer({
    server: { middlewareMode: true, hmr: { server } },
  }),
);

app.disable("x-powered-by");
app.use(viteDevServer.middlewares);
app.use(async (req, res, next) => {
  try {
    const source = await viteDevServer.ssrLoadModule("./server/app.ts");
    return await source.app(req, res, next);
  } catch (error) {
    if (typeof error === "object" && error instanceof Error) {
      viteDevServer.ssrFixStacktrace(error);
    }
    next(error);
  }
});

server.listen(PORT, () => {
  console.log(`Development server running on http://localhost:${PORT}`);
});
