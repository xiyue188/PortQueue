// 浏览器验收使用本机 Edge；先启动后端 5180。截图与 trace 写入忽略目录。
import { defineConfig } from '@playwright/test'
const port = process.env.PORTQUEUE_UI_PORT ?? '5173'
const baseURL = `http://127.0.0.1:${port}`

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 30_000,
  outputDir: '../../artifacts/ui-browser-tests',
  reporter: [['list'], ['json', { outputFile: '../../artifacts/ui-browser-results.json' }]],
  use: {
    baseURL,
    channel: 'msedge',
    headless: true,
    viewport: { width: 1440, height: 1024 },
    locale: 'zh-CN',
    timezoneId: 'Asia/Shanghai',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  webServer: {
    command: `pnpm run dev --port ${port}`,
    url: baseURL,
    reuseExistingServer: !process.env.CI,
  },
})
