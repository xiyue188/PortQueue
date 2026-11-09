// 真实 SQL/API 的浏览器验收；故障与延迟仅拦截确切业务端点，不写开发库。
import { expect, test, type Page } from '@playwright/test'
import { mkdir } from 'node:fs/promises'
const shots = '../../.impeccable/review'
const endpoint = (url: URL) => url.pathname === '/api/appointments'
const overviewEndpoint = (url: URL) => url.pathname === '/api/overview'
async function openList(page: Page) {
  await page.goto('/')
  await page.getByRole('navigation', { name: '业务导航' }).getByRole('button', { name: '进港预约', exact: true }).click()
  await expect(page.locator('.view-pane:visible tbody tr').first()).toBeVisible()
}
test.beforeAll(async () => { await mkdir(shots, { recursive: true }) })

test('总览只读汇总与预约清单分别展示真实数据', async ({ page, request }) => {
  const errors: string[] = []
  page.on('pageerror', e => errors.push(e.message))
  const summary = await (await request.get('/api/overview')).json()
  const data = await (await request.get('/api/appointments')).json()
  expect(data.totalCount).toBeGreaterThan(20)
  await page.goto('/')
  await expect(page.locator('.metric strong')).toHaveText([String(summary.pendingAppointments), String(summary.anchorageVessels), String(summary.confirmedPlans)])
  await expect(page.locator('.state-distribution button')).toHaveCount(6)
  await expect(page.locator('.recent-events li')).toHaveCount(summary.recentEvents.length)
  await expect(page.locator('.spotlight-photo')).toHaveJSProperty('complete', true)
  await expect(page.locator('body')).not.toContainText(/课程演示数据|本地演示|船舶示意|Demo|全部申请日期/)
  await page.screenshot({ path: shots+'/desktop-overview.png', fullPage: true })
  await page.getByRole('button', { name: '查看全部预约' }).click()
  await expect(page.locator('.view-pane:visible tbody tr')).toHaveCount(data.items.length)
  await page.locator('.view-pane:visible tbody .vessel-row').nth(1).click()
  await expect(page.locator('#appointment-detail')).toContainText(data.items[1].appointmentNo)
  await expect(page.locator('.selection-strip')).toContainText(data.items[1].shipName)
  await page.screenshot({ path: shots+'/desktop-appointments.png', fullPage: true })
  expect(errors).toEqual([])
})

test('搜索分页与状态查询保留提交边界', async ({ page }) => {
  await openList(page)
  await page.getByLabel('每页条数').selectOption('2')
  await expect(page.locator('.view-pane:visible tbody tr')).toHaveCount(2)
  const first = await page.locator('.view-pane:visible tbody').innerText()
  await page.getByLabel('预约关键词', { exact: true }).fill('尚未提交')
  await page.getByRole('button', { name: '下一页', exact: true }).click()
  await expect(page.locator('.page-number')).toHaveText('2')
  await expect(page.locator('.view-pane:visible tbody')).not.toHaveText(first)
  await page.getByRole('button', { name: '待审核', exact: true }).click()
  await expect(page.locator('.page-number')).toHaveText('1')
  await expect(page.locator('.view-pane:visible tbody .status-text')).toHaveText(['待审核', '待审核'])
  await page.getByLabel('预约关键词', { exact: true }).fill('PQ202611-1002')
  await page.getByRole('button', { name: '查询', exact: true }).click()
  await expect(page.locator('.view-pane:visible tbody tr')).toHaveCount(1)
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
  await expect(page.locator('.selection-strip')).toContainText('暂无选中预约')
  await page.screenshot({ path: shots+'/empty.png', fullPage: true })
  await page.getByRole('button', { name: '清除筛选', exact: true }).click()
  await expect(page.locator('.view-pane:visible tbody tr')).toHaveCount(20)
})

test('预约失败可重试恢复', async ({ page }) => {
  await page.route(endpoint, route => route.fulfill({ status: 503, body: '{}' }))
  await page.goto('/')
  await page.getByRole('navigation', { name: '业务导航' }).getByRole('button', { name: '进港预约', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText('预约数据暂时无法读取')
  await expect(page.locator('.page-number')).toHaveText('—')
  await page.screenshot({ path: shots+'/error.png', fullPage: true })
  await page.unroute(endpoint)
  await page.getByRole('button', { name: '重新查询' }).click()
  await expect(page.locator('.view-pane:visible tbody tr')).toHaveCount(20)
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
  await expect(page.locator('.view-pane:visible tbody tr')).toHaveCount(9)
  release?.()
  await expect(page.locator('.view-pane:visible tbody .status-text')).toHaveText(Array(9).fill('待审核'))
})

test('390和320窄屏无整页溢出，菜单与键盘选择可用', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto('/')
  await expect(page.locator('.state-distribution button')).toHaveCount(6)
  await expect(page.locator('.recent-events li')).toHaveCount(12)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy()
  await page.screenshot({ path: shots+'/mobile-overview.png', fullPage: true })
  await page.getByRole('button', { name: '展开业务导航' }).click()
  await page.getByRole('navigation', { name: '业务导航' }).getByRole('button', { name: '进港预约', exact: true }).click()
  const second = page.locator('.view-pane:visible tbody .vessel-row').nth(1)
  const no = await second.locator('small').innerText()
  await second.focus(); await page.keyboard.press('Enter')
  await expect(page.locator('#appointment-detail')).toContainText(no)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy()
  await page.screenshot({ path: shots+'/mobile-appointments.png', fullPage: true })
  await page.setViewportSize({ width: 320, height: 740 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy()
})

test('搜索候选支持直接选择、部分输入和键盘关闭', async ({ page }) => {
  await page.goto('/')
  const input = page.getByRole('combobox', { name: '预约关键词' })
  await input.click()
  await expect(page.getByRole('option')).toHaveCount(6)
  await page.screenshot({ path: shots+'/search-suggestions.png', fullPage: true })
  const no = await page.getByRole('option').first().locator('div span').innerText()
  await input.press('ArrowDown')
  await expect(input).toHaveAttribute('aria-activedescendant', 'appointment-option-0')
  await input.press('Enter')
  await expect(page.locator('.view-pane:visible tbody tr')).toHaveCount(20)
  await expect(page.locator('#appointment-detail')).toContainText(no)
  await input.click()
  await input.fill('海岚')
  await expect(page.getByRole('option').first()).toContainText('海岚')
  await input.press('Escape')
  await expect(input).toHaveAttribute('aria-expanded', 'false')
  await expect(page.locator('#appointment-detail')).toContainText(no)
  await input.click()
  await input.fill('不存在的预约候选')
  await expect(page.locator('.search-message')).toHaveText('没有匹配的预约')
  await page.getByRole('heading', { name: '进港预约', exact: true }).click()
  await expect(input).toHaveAttribute('aria-expanded', 'false')
})

test('切换保留筛选和选择，外框一致且不会重复请求', async ({ page }) => {
  let requests = 0
  page.on('request', request => { if (endpoint(new URL(request.url()))) requests++ })
  await openList(page)
  await page.getByRole('button', { name: '待审核', exact: true }).click()
  await expect(page.locator('.view-pane:visible tbody tr')).toHaveCount(9)
  await page.locator('.view-pane:visible tbody .vessel-row').nth(1).click()
  const detail = await page.locator('#appointment-detail').innerText()
  const shell = await page.locator('.port-workspace').boundingBox()
  const sidebar = await page.locator('.sidebar').boundingBox()
  const count = requests
  await page.getByRole('button', { name: '总览', exact: true }).click()
  expect(await page.locator('.port-workspace').boundingBox()).toEqual(shell)
  expect(await page.locator('.sidebar').boundingBox()).toEqual(sidebar)
  await page.getByRole('navigation', { name: '业务导航' }).getByRole('button', { name: '进港预约', exact: true }).click()
  expect(await page.locator('#appointment-detail').innerText()).toBe(detail)
  await expect(page.locator('.view-pane:visible tbody tr')).toHaveCount(9)
  expect(requests).toBe(count)
  await page.goBack()
  await expect(page.getByRole('heading', { name: '总览', exact: true })).toBeVisible()
  await page.goForward()
  await expect(page.getByRole('button', { name: '待审核', exact: true })).toHaveAttribute('aria-pressed', 'true')
})

test('慢刷新保留表格节点与滚动，失败清除旧记录', async ({ page }) => {
  await openList(page)
  const table = page.locator('.appointments-pane .table-scroll')
  await table.evaluate(element => { element.scrollTop = 100; element.setAttribute('data-preserved', 'yes') })
  const top = await table.evaluate(element => element.scrollTop)
  const shell = await page.locator('.port-workspace').boundingBox()
  let release: (() => void) | undefined
  const held = new Promise<void>(resolve => { release = resolve })
  await page.route(endpoint, async route => {
    if (new URL(route.request().url()).searchParams.get('pageSize') !== '20') return route.continue()
    const response = await route.fetch()
    await held
    await route.fulfill({ response })
  })
  await page.getByRole('button', { name: '刷新', exact: true }).click()
  await expect(page.locator('.appointments-pane .query-progress')).toBeVisible()
  await expect(table).toHaveAttribute('data-preserved', 'yes')
  expect(await table.evaluate(element => element.scrollTop)).toBe(top)
  expect(await page.locator('.port-workspace').boundingBox()).toEqual(shell)
  release?.()
  await expect(page.locator('.appointments-pane .query-progress')).toHaveCount(0)
  await page.unroute(endpoint)
  await page.route(endpoint, route => route.fulfill({ status: 503, body: '{}' }))
  await page.getByRole('button', { name: '刷新', exact: true }).click()
  await expect(page.locator('.appointments-pane [role=alert]')).toBeVisible()
  await expect(page.locator('.appointments-pane tbody')).toHaveCount(0)
  await expect(page.locator('#appointment-detail')).toHaveCount(0)
  expect(await page.locator('.port-workspace').boundingBox()).toEqual(shell)
})

test('输入法不提前查询，候选错误重试及窄屏浮层可用', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto('/')
  const input = page.getByRole('combobox', { name: '预约关键词' })
  await input.click()
  await expect(page.getByRole('option')).toHaveCount(6)
  let calls = 0
  page.on('request', request => { if (new URL(request.url()).searchParams.get('pageSize') === '6') calls++ })
  await input.dispatchEvent('compositionstart')
  await input.fill('海')
  await page.waitForTimeout(300) // 超过防抖间隔，验证合成输入没有发出请求。
  expect(calls).toBe(0)
  await input.dispatchEvent('compositionend')
  await expect(page.getByRole('option').first()).toContainText('海')
  await page.screenshot({ path: shots+'/mobile-search.png', fullPage: true })
  const box = await page.locator('.search-popover').boundingBox()
  expect(box!.x + box!.width).toBeLessThanOrEqual(390)
  await page.route(url => endpoint(url) && url.searchParams.get('pageSize') === '6', route => route.fulfill({ status: 503, body: '{}' }))
  await input.fill('东泽')
  await expect(page.locator('.search-message[role=alert]')).toBeVisible()
  await page.unrouteAll()
  await page.getByRole('button', { name: '重试', exact: true }).click()
  await expect(page.getByRole('option').first()).toContainText('东泽')
  await page.setViewportSize({ width: 320, height: 740 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy()
})

test('连续提交关键词与状态使用最新请求条件', async ({ page, request }) => {
  await openList(page)
  const pending = await (await request.get('/api/appointments?keyword=海岚&status=PENDING')).json()
  const approved = await (await request.get('/api/appointments?keyword=新澜&status=APPROVED')).json()
  expect(pending.totalCount).toBeGreaterThan(0)
  expect(approved.totalCount).toBeGreaterThan(0)
  let release: (() => void) | undefined
  let intercept: (() => void) | undefined
  let held = new Promise<void>(resolve => { release = resolve })
  let started = new Promise<void>(resolve => { intercept = resolve })
  let delayStatus = ''
  await page.route(endpoint, async route => {
    const params = new URL(route.request().url()).searchParams
    if (params.get('pageSize') === '20' && params.get('keyword') === '海岚' && (params.get('status') ?? '') === delayStatus) {
      const response = await route.fetch()
      intercept?.(); await held
      await route.fulfill({ response }).catch(() => { /* 后一次操作已取消旧请求。 */ })
    } else await route.continue()
  })
  const input = page.getByRole('combobox', { name: '预约关键词' })
  await input.fill('海岚')
  await page.getByRole('button', { name: '查询', exact: true }).click()
  await started
  await page.getByRole('button', { name: '待审核', exact: true }).click()
  await expect(page.locator('.view-pane:visible tbody .status-text')).toHaveText(Array(pending.totalCount).fill('待审核'))
  await expect(page.locator('#appointment-detail')).toContainText('海岚')
  release?.()

  // 反向次序：状态请求未完成时提交新关键词，也必须继承已提交的新状态。
  delayStatus = 'APPROVED'
  held = new Promise<void>(resolve => { release = resolve })
  started = new Promise<void>(resolve => { intercept = resolve })
  await page.getByRole('button', { name: '已批准', exact: true }).click()
  await started
  await input.fill('新澜')
  await page.getByRole('button', { name: '查询', exact: true }).click()
  await expect(page.locator('.view-pane:visible tbody .status-text')).toHaveText(Array(approved.totalCount).fill('已批准'))
  await expect(page.locator('#appointment-detail')).toContainText('新澜')
  release?.()
})

test('用户宽屏与参考宽度的布局证据', async ({ page }) => {
  await page.setViewportSize({ width: 2470, height: 1303 })
  await page.goto('/')
  await expect(page.locator('.state-distribution button')).toHaveCount(6)
  await page.screenshot({ path: shots+'/user-2470-overview.png', fullPage: true })
  const shell = await page.locator('.port-workspace').boundingBox()
  await page.getByRole('navigation', { name: '业务导航' }).getByRole('button', { name: '进港预约', exact: true }).click()
  await expect(page.locator('.view-pane:visible tbody tr')).toHaveCount(20)
  expect(await page.locator('.port-workspace').boundingBox()).toEqual(shell)
  await page.screenshot({ path: shots+'/user-2470-appointments.png', fullPage: true })
  await page.setViewportSize({ width: 973, height: 640 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy()
  await page.screenshot({ path: shots+'/reference-973-appointments.png', fullPage: true })
})
