import { createContext } from "react-router";

export const valueFromExpressContext = createContext<string>();

export interface RenderContext {
  readonly nonce: string;
  readonly apiUpstream: string;
//#if (accounts)
  readonly accessToken: () => Promise<string | undefined>;
//#endif
}

export const renderContext = createContext<RenderContext>();
