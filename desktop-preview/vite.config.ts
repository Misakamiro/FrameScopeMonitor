import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import path from "path";

export default defineConfig(async () => ({
  plugins: [
    react({
      jsxImportSource: "@emotion/react",
      babel: {
        plugins: ["@emotion/babel-plugin"],
      },
    }),
  ],
  test: {
    // Overrides vitest's default excludes, so skip every node_modules tree
    // (installer/ has its own) - a bare "node_modules" only matched the root.
    exclude: ["**/node_modules/**", "dist/**", "src-framescope-baseline/**"],
  },
  resolve: {
    alias: [
      { find: "@preview/native-core", replacement: path.resolve(__dirname, "node_modules/@tauri-apps/api/core.js") },
      { find: /^@tauri-apps\/api\/core$/, replacement: path.resolve(__dirname, "src/preview/runtime/core.ts") },
      { find: /^@tauri-apps\/plugin-store$/, replacement: path.resolve(__dirname, "src/preview/runtime/store.ts") },
      { find: "@", replacement: path.resolve(__dirname, "./src") },
    ],
  },
  clearScreen: false,
  server: {
    port: 1430,
    host: "127.0.0.1",
    strictPort: true,
    watch: {
      ignored: ["**/src-tauri/**"],
    },
  },
}));
