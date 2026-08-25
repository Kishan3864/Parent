/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Origin prefix of the API, without the /v1 segment. Defaults to "/api". */
  readonly VITE_API_BASE_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
