import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";

export default defineConfig([
  ...nextVitals,
  ...nextTs,
  // Explicit React version: eslint-plugin-react's "detect" mode uses an API removed in ESLint 10.
  { settings: { react: { version: "19.3" } } },
  globalIgnores([".next/**", "out/**", "build/**", "next-env.d.ts"]),
]);
