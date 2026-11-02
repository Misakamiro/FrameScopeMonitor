import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import path from "path";
import { readFileSync } from "node:fs";

export default defineConfig(async () => ({
  define: { __FRAMESCOPE_VERSION__: JSON.stringify(readFileSync(path.resolve(__dirname,"../VERSION"),"utf8").trim()) },
  plugins: [
    react({
      jsxImportSource: "@emotion/react",
      babel: {
        plugins: ["@emotion/babel-plugin"],
      },
    }),
  ],
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
