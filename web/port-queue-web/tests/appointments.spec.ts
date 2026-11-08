// 真实 SQL/API 的浏览器验收；故障与延迟仅拦截确切业务端点，不写开发库。
import { expect, test, type Page } from '@playwright/test'
import { mkdir } from 'node:fs/promises'
const shots = '../../.impeccable/review'
const endpoint = (url: URL) => url.pathname === '/api/appointments'
const overviewEndpoint = (url: URL) => url.pathname === '/api/overview'
async function openList(page: Page) {
  await page.goto('/')
  await page.getByRole('button', { name: '进港预约', exact: true }).click()
  await expect(page.locator('tbody tr').first()).toBeVisible()
}
test.beforeAll(async () => { await mkdir(shots, { recursive: true }) })

test('总览真实跨业务指标与清单详情一致', async ({ page, request }) => {
  const errors: string[] = []
  page.on('pageerror', e => errors.push(e.message))
  const summary = await (await request.get('/api/overview')).json()
  const data = await (await request.get('/api/appointments')).json()
  expect(data.totalCount).toBeGreaterThan(20)
  await page.goto('/')
  await expect(page.locator('.metric strong')).toHaveText([String(summary.pendingAppointments), String(summary.anchorageVessels), String(summary.confirmedPlans)])
  await expect(page.locator('tbody tr')).toHaveCount(5)
  await expect(page.locator('.recent-events li')).toHaveCount(summary.recentEvents.length)
  await expect(page.locator('.spotlight-photo')).toHaveJSProperty('complete', true)
  await expect(page.locator('body')).not.toContainText(/课程演示数据|本地演示|船舶示意|Demo|全部申请日期/)
  await page.screenshot({ path: shots+'/desktop-overview.png', fullPage: true })
  await page.getByRole('button', { name: '查看全部预约' }).click()
  await expect(page.locator('tbody tr')).toHaveCount(data.items.length)
  await page.locator('tbody .vessel-row').nth(1).click()
  await expect(page.locator('#appointment-detail')).toContainText(data.items[1].appointmentNo)
  await expect(page.locator('.selection-strip')).toContainText(data.items[1].shipName)
  await page.screenshot({ path: shots+'/desktop-appointments.png', fullPage: true })
  expect(errors).toEqual([])
})

test('搜索分页与状态查询保留提交边界', async ({ page }) => {
  await openList(page)
  await page.getByLabel('每页条数').selectOption('2')
  await expect(page.locator('tbody tr')).toHaveCount(2)
  const first = await page.locator('tbody').innerText()
  await page.getByLabel('预约关键词', { exact: true }).fill('尚未提交')
  await page.getByRole('button', { name: '下一页', exact: true }).click()
  await expect(page.locator('.page-number')).toHaveText('2')
  await expect(page.locator('tbody')).not.toHaveText(first)
  await page.getByRole('button', { name: '待审核', exact: true }).click()
  await expect(page.locator('.page-number')).toHaveText('1')
  await expect(page.locator('tbody .status-text')).toHaveText(['待审核', '待审核'])
  await page.getByLabel('预约关键词', { exact: true }).fill('PQ202611-1002')
  await page.getByRole('button', { name: '查询', exact: true }).click()
  await expect(page.locator('tbody tr')).toHaveCount(1)
  await expect(page.locator('#appointment-detail')).toContainText('PQ202611-1002')
  await page.getByRole('button', { name: '总览', exact: true }).click()
  await expect(page.locator('.metric strong').first()).toHaveText('9')
})

test('无结果不保留旧详情，重置恢复', async ({ page }) => {
  await openList(page)
  await page.getByLabel('预约关键词', { exact: true }).fill('NO-SUCH-APPOINTMENT')
  await page.getByRole('button', { name: '查询', exact: true }).click()
  await expect(page.getByRole('heading', { name: '没有符合条件的预约' })).toBeVisible()
  await expect(page.locator('#appointment-detail')).toHaveCount(0)
  await expect(page.locator('.selection-strip')).toHaveCount(0)
  await page.screenshot({ path: shots+'/empty.png', fullPage: true })
  await page.getByRole('button', { name: '清除筛选', exact: true }).click()
  await expect(page.locator('tbody tr')).toHaveCount(20)
})

test('预约失败可重试恢复', async ({ page }) => {
  await page.route(endpoint, route => route.fulfill({ status: 503, body: '{}' }))
  await page.goto('/')
  await page.getByRole('button', { name: '进港预约', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText('预约数据暂时无法读取')
  await expect(page.locator('.page-number')).toHaveText('—')
  await page.screenshot({ path: shots+'/error.png', fullPage: true })
  await page.unroute(endpoint)
  await page.getByRole('button', { name: '重新查询' }).click()
  await expect(page.locator('tbody tr')).toHaveCount(20)
})

test('总览失败不显示过期数字，重试恢复', async ({ page }) => {
  await page.route(overviewEndpoint, route => route.fulfill({ status: 503, body: '{}' }))
  await page.goto('/')
  await expect(page.locator('.recent-events [role=alert]')).toContainText('总览暂时无法读取')
  await expect(page.locator('.metric strong')).toHaveText(['—', '—', '—'])
  await page.unroute(overviewEndpoint)
  await page.getByRole('button', { name: '重试', exact: true }).click()
  await expect(page.locator('.metric strong')).toHaveText(['9', '10', '4'])
})

test('先发慢请求不能覆盖新状态查询', async ({ page }) => {
  await openList(page)
  let release: (() => void) | undefined
  const held = new Promise<void>(resolve => { release = resolve })
  let intercepted: (() => void) | undefined
  const started = new Promise<void>(resolve => { intercepted = resolve })
  await page.route(endpoint, async route => {
    if (!new URL(route.request().url()).searchParams.has('status')) {
      const response = await route.fetch()
      intercepted?.(); await held
      await route.fulfill({ response }).catch(() => { /* 旧请求已取消。 */ })
    } else await route.continue()
  })
  await page.getByRole('button', { name: '刷新', exact: true }).click()
  await started
  await page.getByRole('button', { name: '待审核', exact: true }).click()
  await expect(page.locator('tbody tr')).toHaveCount(9)
  release?.()
  await expect(page.locator('tbody .status-text')).toHaveText(Array(9).fill('待审核'))
})

test('390和320窄屏无整页溢出，菜单与键盘选择可用', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto('/')
  await expect(page.locator('tbody tr')).toHaveCount(5)
  await expect(page.locator('.recent-events li')).toHaveCount(5)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy()
  await page.screenshot({ path: shots+'/mobile-overview.png', fullPage: true })
  await page.getByRole('button', { name: '展开业务导航' }).click()
  await page.getByRole('button', { name: '进港预约', exact: true }).click()
  const second = page.locator('tbody .vessel-row').nth(1)
  const no = await second.locator('small').innerText()
  await second.focus(); await page.keyboard.press('Enter')
  await expect(page.locator('#appointment-detail')).toContainText(no)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy()
  await page.screenshot({ path: shots+'/mobile-appointments.png', fullPage: true })
  await page.setViewportSize({ width: 320, height: 740 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy()
})
