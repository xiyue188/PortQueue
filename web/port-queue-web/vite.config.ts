// 本地开发与 preview 用相同的 /api 代理；页面不用跨域，也不持有数据库配置。
import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// 隔离验收可指定独立后端；普通 VS Code 启动仍使用 5180，不抢占已有服务。
const apiTarget = process.env.PORTQUEUE_API_TARGET ?? 'http://localhost:5180'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    host: '127.0.0.1',
    port: 5173,
    strictPort: true,
    proxy: { '/api': { target: apiTarget } },
  },
  preview: {
    host: '127.0.0.1',
    port: 4173,
    strictPort: true,
    proxy: { '/api': { target: apiTarget } },
  },
})
