// 中期看板收口：真实全状态查询、定时读取生命周期及流程动画；不写开发数据库。
import { test, expect } from '@playwright/test'

const nav = (page: import('@playwright/test').Page, name: string) => page.getByRole('navigation', { name: '业务导航' }).getByRole('button', { name, exact: true })
const shots = '../../.impeccable/review'

test('全部六种状态均可直接筛选，窄屏不隐藏入口', async ({ page, request }) => {
  const summary = await (await request.get('/api/overview')).json()
  await page.goto('/#appointments')
  const tabs = page.getByRole('group', { name: '预约状态筛选' })
  await expect(tabs.getByRole('button')).toHaveCount(7)
  for (const [code, label] of [['DRAFT','草稿'],['PENDING','待审核'],['APPROVED','已批准'],['REJECTED','已驳回'],['CANCELLED','已撤销'],['COMPLETED','已完成']]) {
    await tabs.getByRole('button', { name: label, exact: true }).click()
    await expect(tabs.getByRole('button', { name: label, exact: true })).toHaveAttribute('aria-pressed', 'true')
    const count = summary.appointmentStates.find((state: {code: string}) => state.code === code).count
    await expect(page.locator('.appointments-pane .pagination')).toContainText(`共 ${count} 条预约`)
    if (count) await expect(page.locator('.appointments-pane tbody .status-text')).toHaveText(Array(Math.min(20,count)).fill(label))
  }
  for (const width of [1440,2470,390,320]) {
    await page.setViewportSize({ width, height: width > 700 ? 1024 : 844 })
    await tabs.scrollIntoViewIfNeeded()
    for (const button of await tabs.getByRole('button').all()) await expect(button).toBeInViewport()
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy()
    await page.screenshot({ path: `${shots}/dashboard-statuses-${width}.png`, fullPage: true })
  }
})

test('总览定时读取保留动态页码，后台和离页暂停，返回过期才更新', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await page.clock.install()
  await page.clock.pauseAt(new Date())
  let requests: string[] = []
  page.on('request', r => { if (new URL(r.url()).pathname === '/api/overview') requests.push(r.url()) })
  await page.goto('/')
  await expect(page.locator('.state-distribution button')).toHaveCount(6)
  await page.getByRole('button', { name: '较早动态' }).click()
  await expect(page.locator('.event-range')).toContainText('13–')
  const count = requests.length
  await page.clock.runFor(30_001)
  await expect.poll(() => requests.length).toBe(count + 1)
  expect(requests.at(-1)).toContain('eventsPage=2')
  await expect(page.locator('.recent-events')).toHaveAttribute('aria-busy', 'false')
  await expect(page.locator('.event-range')).toContainText('13–')
  await nav(page, '进港预约').click()
  await page.clock.runFor(60_001)
  expect(requests.length).toBe(count + 1)
  await nav(page, '总览').click()
  await page.clock.runFor(1)
  await expect.poll(() => requests.length).toBe(count + 2)
  await expect(page.locator('.recent-events')).toHaveAttribute('aria-busy', 'false')
  await page.evaluate(() => { Object.defineProperty(document, 'hidden', { configurable: true, value: true }); document.dispatchEvent(new Event('visibilitychange')) })
  await page.clock.runFor(60_001)
  expect(requests.length).toBe(count + 2)
  await page.evaluate(() => { Object.defineProperty(document, 'hidden', { configurable: true, value: false }); document.dispatchEvent(new Event('visibilitychange')) })
  await page.clock.runFor(1)
  await expect.poll(() => requests.length).toBe(count + 3)
  await expect(page.locator('.recent-events')).toHaveAttribute('aria-busy', 'false')
  // 连续失败不密集重试；随后正常轮询恢复当前数据。
  await page.route('**/api/overview?*', route => route.fulfill({ status: 503, body: '{}' }), { times: 1 })
  await page.clock.runFor(30_001)
  await expect(page.locator('.metric strong')).toHaveText(['—','—','—'])
  const afterFailure = requests.length
  await page.clock.runFor(29_000)
  expect(requests.length).toBe(afterFailure)
  await page.clock.runFor(1001)
  await expect(page.locator('.metric strong').first()).not.toHaveText('—')
  expect(requests.at(-1)).toContain('eventsPage=2')
  await expect(page.locator('.event-range')).toContainText('13–')
  await page.getByRole('button', { name: '刷新', exact: true }).click()
  await expect(page.locator('.recent-events')).toHaveAttribute('aria-busy', 'false')
  expect(requests.at(-1)).toContain('eventsPage=2')
})

test('总览指标可按需展开口径，默认不占用额外版面', async ({ page }) => {
  await page.goto('/')
  const metrics = page.locator('.metric-definition')
  await expect(metrics).toHaveCount(3)
  for (const [label, definition] of [
    ['待审核预约', '已提交、尚未批准或驳回的预约数量。'],
    ['锚地在场', '已进入锚地、尚未登记离锚的停留记录数。'],
    ['待靠泊计划', '状态为已确认、尚未开始执行的当前泊位计划数量。'],
  ]) {
    const metric = metrics.filter({ hasText: label })
    await expect(metric).not.toHaveAttribute('open', '')
    await metric.locator('summary').press('Enter')
    await expect(metric).toHaveAttribute('open', '')
    await expect(metric).toContainText(definition)
    await metric.locator('summary').press('Enter')
    await expect(metric).not.toHaveAttribute('open', '')
  }
  for (const width of [1440, 320]) {
    await page.setViewportSize({ width, height: 900 })
    await metrics.nth(1).locator('summary').click()
    await expect(metrics.nth(1).locator('p')).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy()
    const fits = await page.locator('.metrics-strip').evaluate(el => {
      const area = el.getBoundingClientRect()
      return [...el.querySelectorAll('summary, p')].filter(item => item.getBoundingClientRect().height > 0)
        .every(item => { const rect = item.getBoundingClientRect(); return rect.left >= area.left && rect.right <= area.right && rect.bottom <= area.bottom })
    })
    expect(fits).toBeTruthy()
    await page.screenshot({ path: `${shots}/dashboard-metric-definition-${width}.png`, fullPage: true })
    await metrics.nth(1).locator('summary').press('Space')
    await expect(metrics.nth(1)).not.toHaveAttribute('open', '')
  }
})

test('小船确实沿轨道运动，可暂停且尊重隐藏和减少动画', async ({ page }) => {
  await page.goto('/')
  await page.locator('.port-process').scrollIntoViewIfNeeded()
  const track = page.locator('.desktop-track')
  await expect.poll(() => track.evaluate(el => !(el as SVGSVGElement).animationsPaused())).toBeTruthy()
  const boat = track.locator('.route-boat')
  const x = await boat.evaluate(el => (el as SVGGraphicsElement).getCTM()!.e)
  await expect.poll(() => boat.evaluate(el => (el as SVGGraphicsElement).getCTM()!.e)).toBeGreaterThan(x + 2)
  await page.getByRole('button', { name: '暂停流程动画' }).click()
  expect(await track.evaluate(el => (el as SVGSVGElement).animationsPaused())).toBeTruthy()
  await page.getByRole('button', { name: '继续流程动画' }).click()
  await expect.poll(() => track.evaluate(el => (el as SVGSVGElement).animationsPaused())).toBeFalsy()
  await page.evaluate(() => { Object.defineProperty(document, 'hidden', { configurable: true, value: true }); document.dispatchEvent(new Event('visibilitychange')) })
  await expect.poll(() => track.evaluate(el => (el as SVGSVGElement).animationsPaused())).toBeTruthy()
  await page.evaluate(() => { Object.defineProperty(document, 'hidden', { configurable: true, value: false }); document.dispatchEvent(new Event('visibilitychange')) })
  await nav(page, '进港预约').click()
  await expect.poll(() => track.evaluate(el => (el as SVGSVGElement).animationsPaused())).toBeTruthy()
  await nav(page, '总览').click()
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await expect(page.locator('.process-diagram')).toHaveClass(/is-reduced/)
  await expect(boat).toBeHidden()
  expect(await track.evaluate(el => (el as SVGSVGElement).animationsPaused())).toBeTruthy()
})

test('居中布局与中期页脚在桌面和窄屏保持完整', async ({ page }) => {
  await page.goto('/')
  await expect(page.locator('.state-distribution button')).toHaveCount(6)
  for (const [width,height] of [[1440,900],[2470,1080],[390,844],[320,740]]) {
    await page.setViewportSize({ width: width!, height: height! })
    await page.locator('.port-process').scrollIntoViewIfNeeded()
    // 固定时间点只为截图稳定；动画运动与暂停另有独立行为断言。
    await page.locator('.process-track').evaluateAll(elements => elements.forEach(el => { (el as SVGSVGElement).pauseAnimations(); (el as SVGSVGElement).setCurrentTime(5) }))
    const layout = await page.locator('.process-diagram').evaluate(el => {
      const diagram = el.getBoundingClientRect(), body = el.parentElement!.getBoundingClientRect()
      return { horizontal: Math.abs(diagram.left + diagram.width/2 - body.left - body.width/2), vertical: Math.abs(diagram.top + diagram.height/2 - body.top - body.height/2) }
    })
    expect(layout.horizontal).toBeLessThan(1)
    expect(layout.vertical).toBeLessThanOrEqual(6)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy()
    await page.screenshot({ path: `${shots}/dashboard-process-${width}.png`, fullPage: true, animations: 'disabled' })
    await page.locator('.overview-pane').evaluate(el => el.scrollTop = 0)
    await page.evaluate(() => window.scrollTo(0,0))
    await page.screenshot({ path: `${shots}/dashboard-overview-${width}.png`, fullPage: true, animations: 'disabled' })
  }
})
