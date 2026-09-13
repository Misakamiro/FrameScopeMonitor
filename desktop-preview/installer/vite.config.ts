import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The installer ships as a single embedded-payload exe; keep the bundle lean.
export default defineConfig({
  plugins: [react()],
  clearScreen: false,
  build: {
    target: "chrome110",
    minify: "esbuild",
    sourcemap: false,
    chunkSizeWarningLimit: 2000,
  },
});
