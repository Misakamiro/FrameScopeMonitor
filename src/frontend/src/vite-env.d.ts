/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_FRAMESCOPE_VERSION: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}

declare module "node:fs" {
  export function readFileSync(path: URL | string, encoding: "utf8"): string;
}
