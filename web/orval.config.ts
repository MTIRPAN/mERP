import { defineConfig } from 'orval';

// npm run gen:api — the API must be running in Development (OpenAPI is served at /openapi/v1.json).
export default defineConfig({
  merp: {
    input: { target: process.env.MERP_OPENAPI_URL ?? 'http://localhost:5000/openapi/v1.json' },
    output: {
      mode: 'tags-split',
      target: 'src/shared/api/generated/endpoints',
      schemas: 'src/shared/api/generated/model',
      client: 'react-query',
      httpClient: 'fetch',
      clean: true,
      formatter: 'prettier',
      override: {
        mutator: { path: 'src/shared/api/http.ts', name: 'customFetch' },
        fetch: { includeHttpResponseReturnType: false },
        query: { signal: true },
      },
    },
  },
});
