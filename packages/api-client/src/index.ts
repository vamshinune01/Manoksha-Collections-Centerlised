// Generated OpenAPI types for the Manoksha backend. Regenerate with `npm run api-client:generate`
// after the backend contract (contracts/openapi/manoksha-v1.json) changes.
import type { components, paths } from "./schema";

export type { components, paths };
export type Schemas = components["schemas"];

/** RFC 7807 problem details returned by the backend, with a stable machine-readable code. */
export interface ApiProblem {
  status: number;
  title: string;
  code?: string;
  correlationId?: string;
  errors?: Record<string, string[]>;
  [key: string]: unknown;
}
