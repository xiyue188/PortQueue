// Vite 构建配置：只负责前端开发服务器、Vue 插件和路径别名。
// API 地址和业务请求应在后续 api/composables 模块中集中管理。
import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    strictPort: true,
  },
})
