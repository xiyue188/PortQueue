// 正向用例访问真实 API，不写开发库；仅故障/延迟用例拦截 HTTP，验证前端恢复边界。
import { expect, test } from '@playwright/test'
import { mkdir } from 'node:fs/promises'

const shots = '../../.impeccable/review'
// 精确匹配业务端点，避免误拦截 Vite 的 /src/api/appointments.ts 模块。
const appointmentEndpoint = (url: URL) => url.pathname === '/api/appointments'
test.beforeAll(async () => { await mkdir(shots, { recursive: true }) })

test('真实预约与选中详情一致，并保存两种桌面布局', async ({ page, request, browser }) => {
  const failures: string[] = []
  page.on('pageerror', error => failures.push(error.message))
  const response = await request.get('/api/appointments')
  expect(response.ok()).toBeTruthy()
  const body = await response.json()
  expect(body.totalCount).toBeGreaterThan(0)
  await page.goto('/')
  await expect(page.locator('tbody tr')).toHaveCount(body.items.length)
  await expect(page.getByRole('heading', { name: body.items[0].shipName, exact: true })).toBeVisible()
  await page.screenshot({ path: `${shots}/desktop-overview.png`, fullPage: true })
  // 原参考面板为 973×640；以相同输出像素捕获工作区首屏，保留桌面 CSS 构图。
  // 只裁掉应用外的海报底色，不拉伸画面；完整高度另见 desktop-overview.png。
  const referenceScale = 973 / 1360
  const referenceContext = await browser.newContext({ viewport: { width: 1440, height: 1024 }, deviceScaleFactor: referenceScale, locale: 'zh-CN', timezoneId: 'Asia/Shanghai' })
  try {
    const referencePage = await referenceContext.newPage()
    await referencePage.goto('http://127.0.0.1:5173/')
    await expect(referencePage.locator('tbody tr')).toHaveCount(body.items.length)
    const frame = await referencePage.locator('.port-workspace').boundingBox()
    if (!frame) throw new Error('预约工作区未渲染')
    await referencePage.screenshot({ path: `${shots}/hero-repro.png`, clip: { ...frame, height: 640 / referenceScale }, scale: 'device' })
  } finally { await referenceContext.close() }
  await page.getByRole('button', { name: '进港预约', exact: true }).click()
  await expect(page.locator('#appointment-detail')).toContainText(body.items[0].appointmentNo)
  if (body.items.length > 1) {
    await page.getByRole('button', { name: `查看 ${body.items[1].appointmentNo} 预约详情`, exact: true }).click()
    await expect(page.locator('#appointment-detail')).toContainText(body.items[1].appointmentNo)
    await expect(page.locator('.selection-strip')).toContainText(body.items[1].appointmentNo)
  }
  await page.screenshot({ path: `${shots}/desktop-appointments.png`, fullPage: true })
  expect(failures).toEqual([])
})

test('真实分页与状态筛选、搜索草稿不会隐式提交', async ({ page }) => {
  await page.goto('/')
  await expect(page.locator('tbody tr')).toHaveCount(5)
  await page.getByLabel('每页条数').selectOption('2')
  await expect(page.locator('tbody tr')).toHaveCount(2)
  const first = await page.locator('tbody').innerText()
  await page.getByLabel('预约关键词', { exact: true }).fill('尚未提交的关键词')
  await page.getByRole('button', { name: '下一页', exact: true }).click()
  await expect(page.locator('.page-number')).toHaveText('2')
  await expect(page.locator('tbody')).not.toHaveText(first)
  await expect(page.locator('tbody tr')).toHaveCount(2)
  await page.getByRole('button', { name: '待审核', exact: true }).click()
  await expect(page.locator('.page-number')).toHaveText('1')
  await expect(page.locator('tbody tr')).toHaveCount(1)
  await expect(page.locator('tbody')).toContainText('PQ-DEMO-005')
  await page.getByLabel('预约状态', { exact: true }).selectOption('COMPLETED')
  await expect(page.locator('tbody tr')).toHaveCount(2)
})

test('无结果清除旧船舶，重置后恢复真实记录', async ({ page }) => {
  await page.goto('/')
  await expect(page.locator('tbody tr')).toHaveCount(5)
  await page.getByRole('button', { name: '进港预约', exact: true }).click()
  await page.getByLabel('预约关键词', { exact: true }).fill('NO-SUCH-APPOINTMENT')
  await page.getByRole('button', { name: '查询', exact: true }).click()
  await expect(page.getByRole('heading', { name: '没有符合条件的预约' })).toBeVisible()
  await expect(page.locator('#appointment-detail')).toHaveCount(0)
  await expect(page.locator('.selection-strip')).toHaveCount(0)
  await page.screenshot({ path: `${shots}/empty.png`, fullPage: true })
  await page.getByRole('button', { name: '清除筛选', exact: true }).click()
  await expect(page.locator('tbody tr')).toHaveCount(5)
})

test('503 显示明确错误，取消拦截后可重试恢复', async ({ page }) => {
  await page.route(appointmentEndpoint, route => route.fulfill({ status: 503, contentType: 'application/problem+json', body: '{"code":"DATABASE_UNAVAILABLE"}' }))
  await page.goto('/')
  await expect(page.getByRole('alert')).toContainText('预约数据暂时无法读取')
  await expect(page.locator('tbody tr')).toHaveCount(0)
  await expect(page.locator('.page-number')).toHaveText('—')
  await page.screenshot({ path: `${shots}/error.png`, fullPage: true })
  await page.unroute(appointmentEndpoint)
  await page.getByRole('button', { name: '重新查询' }).click()
  await expect(page.locator('tbody tr')).toHaveCount(5)
})

test('先发慢请求不能覆盖后发的状态查询', async ({ page }) => {
  await page.goto('/')
  await expect(page.locator('tbody tr')).toHaveCount(5)
  let release: (() => void) | undefined
  const held = new Promise<void>(resolve => { release = resolve })
  let intercepted: (() => void) | undefined
  const started = new Promise<void>(resolve => { intercepted = resolve })
  await page.route(appointmentEndpoint, async route => {
    if (!new URL(route.request().url()).searchParams.has('status')) {
      const response = await route.fetch()
      intercepted?.()
      await held
      await route.fulfill({ response }).catch(() => { /* 取消的旧请求可已关闭。 */ })
    } else await route.continue()
  })
  await page.getByRole('button', { name: '刷新预约' }).click()
  await started
  await expect(page.getByRole('heading', { name: '正在查询预约' })).toBeVisible()
  await page.getByRole('button', { name: '待审核', exact: true }).click()
  await expect(page.locator('tbody tr')).toHaveCount(1)
  release?.()
  await expect(page.locator('tbody')).toContainText('PQ-DEMO-005')
  await expect(page.locator('tbody tr')).toHaveCount(1)
})

test('窄屏没有整页溢出，导航与键盘行选择可用', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto('/')
  await expect(page.locator('tbody tr')).toHaveCount(5)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy()
  await page.screenshot({ path: `${shots}/mobile-overview.png`, fullPage: true })
  await page.getByRole('button', { name: '展开业务导航' }).click()
  await page.getByRole('button', { name: '进港预约', exact: true }).click()
  const button = page.getByRole('button', { name: '查看 PQ-DEMO-004 预约详情', exact: true })
  await button.focus()
  await page.keyboard.press('Enter')
  await expect(page.locator('#appointment-detail')).toContainText('PQ-DEMO-004')
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy()
  await page.screenshot({ path: `${shots}/mobile-appointments.png`, fullPage: true })
  await page.setViewportSize({ width: 320, height: 740 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy()
})
