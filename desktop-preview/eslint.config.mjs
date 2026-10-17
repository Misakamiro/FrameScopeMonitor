import { dirname } from "node:path";
import { fileURLToPath } from "node:url";
import js from "@eslint/js";
import { FlatCompat } from "@eslint/eslintrc";

const compat = new FlatCompat({
  baseDirectory: dirname(fileURLToPath(import.meta.url)),
  recommendedConfig: js.configs.recommended,
});

export default [
  {
    // Generated artifacts and the frozen comparison snapshot are not live source.
    ignores: [
      "**/node_modules/**",
      "**/dist/**",
      "**/target/**",
      "src-framescope-baseline/**",
    ],
  },
  ...compat.config({
    extends: [
      "eslint:recommended",
      "plugin:react/recommended",
      "plugin:react/jsx-runtime",
      "plugin:react-hooks/recommended",
    ],
    plugins: ["jsx-a11y"],
    env: { browser: true, node: true, es2022: true },
    settings: { react: { version: "detect" } },
    rules: {
      // Emotion's JSX transform consumes this property.
      "react/no-unknown-property": ["error", { ignore: ["css"] }],
      "jsx-a11y/alt-text": "warn",
      "jsx-a11y/aria-props": "warn",
      "jsx-a11y/aria-proptypes": "warn",
      "jsx-a11y/aria-unsupported-elements": "warn",
      "jsx-a11y/role-has-required-aria-props": "warn",
      "jsx-a11y/role-supports-aria-props": "warn",
    },
    overrides: [
      {
        files: ["**/*.{ts,tsx,mts,cts}"],
        extends: ["plugin:@typescript-eslint/recommended"],
        // TypeScript owns component prop validation.
        rules: { "react/prop-types": "off" },
      },
    ],
  }),
];
