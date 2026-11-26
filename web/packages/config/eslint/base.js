import js from "@eslint/js";
import globals from "globals";
import tseslint from "typescript-eslint";

/** Shared ESLint flat config for every TypeScript package. */
export default tseslint.config(
  { ignores: ["dist/**", ".next/**", "node_modules/**", "storybook-static/**", "test-results/**", "playwright-report/**", "**/*.d.ts", "next-env.d.ts"] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  // Build scripts, e2e helpers and tool configs run in Node.
  { files: ["scripts/**/*.{js,mjs,ts}", "e2e/**/*.{js,mjs,ts}", "*.config.{js,mjs,ts}"], languageOptions: { globals: { ...globals.node } } },
);
