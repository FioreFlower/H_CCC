import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";

export default defineConfig({
  plugins: [vue()],
  build: {
    outDir: "wwwroot",
    emptyOutDir: true
  },
  server: {
    proxy: {
      "/api": "http://127.0.0.1:5183",
      "/media-cache": "http://127.0.0.1:5183"
    }
  }
});
