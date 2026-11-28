// 从用户看到的船体操作，避免仅测试实现者已知的小圆点；空间与字号也是验收条件。
import { test, expect } from '@playwright/test'
import { mkdirSync } from 'node:fs'
import { loginAnchorage } from './anchorage-support'

for (const role of ['pq.admin', 'pq.dispatcher']) test(`${role}直接抓住船体调序，单击仍只选择，取消不写入`, async ({ page }) => {
  await loginAnchorage(page, role)
  const initial = await (await page.request.get('/api/anchorages')).json()
  const area = initial.items.find((a: { code: string }) => a.code === 'ANCH-INNER-A')
  await page.getByRole('button', { name: new RegExp(area.name) }).click()
  await expect(page.locator('.aq-vessel')).toHaveCount(area.queueCount)
  let writes = 0
  page.on('request', request => { if (request.method() === 'POST' && request.url().endsWith('/reorder')) writes++ })
  const ship = page.locator('.aq-select').nth(2)
  await ship.click()
  await expect(ship).toHaveAttribute('aria-pressed', 'true')
  await expect(page.locator('.aq-reorder[open]')).toHaveCount(0)
  const from = (await ship.locator('.queue-ship').boundingBox())!
  const to = (await page.locator('.queue-ship').first().boundingBox())!
  await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2)
  await page.mouse.down()
  await page.mouse.move(to.x + to.width / 2, to.y + to.height / 2, { steps: 16 })
  await expect(page.locator('.aq-drop-position')).toBeVisible()
  await expect(page.locator('.is-dragging .aq-select > span')).toHaveText('移至第 1 位')
  if (role === 'pq.admin') {
    mkdirSync('../../artifacts/stage4-repair', { recursive: true })
    await page.screenshot({ path: '../../artifacts/stage4-repair/direct-drag.png' })
  }
  await page.mouse.up()
  await expect(page.locator('.aq-reorder[open]')).toBeVisible()
  await expect(page.locator('#aq-new-position')).toHaveValue('1')
  await page.getByRole('button', { name: '取消', exact: true }).click()
  expect(writes).toBe(0)
})

test('触摸船体跨屏调序，空白海面仍能滑动浏览', async ({ page }) => {
  await loginAnchorage(page)
  const areas = await (await page.request.get('/api/anchorages')).json()
  const area = areas.items.find((a: { code: string }) => a.code === 'ANCH-OUTER-A')
  await page.getByRole('button', { name: new RegExp(area.name) }).click()
  await expect(page.locator('.aq-vessel')).toHaveCount(area.queueCount)
  await page.setViewportSize({ width: 390, height: 844 })
  await page.locator('.queue-ship').first().scrollIntoViewIfNeeded()
  const cdp = await page.context().newCDPSession(page)
  const from = (await page.locator('.queue-ship').first().boundingBox())!
  const sea = (await page.locator('.aq-sea').boundingBox())!
  let writes = 0
  page.on('request', request => { if (request.method() === 'POST' && request.url().endsWith('/reorder')) writes++ })
  await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: from.x + from.width/2, y: from.y + from.height/2 }] })
  await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: sea.x + sea.width - 12, y: from.y + from.height/2 }] })
  await expect.poll(() => page.locator('.aq-sea').evaluate(el => el.scrollLeft)).toBeGreaterThan(300)
  await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] })
  await expect(page.locator('.aq-reorder[open]')).toBeVisible()
  await page.getByRole('button', { name: '取消', exact: true }).click()
  await page.locator('.aq-sea').evaluate(el => el.scrollLeft = 0)
  // 顶部海面不属于船舶按钮，可原生滑动，不能让整格都捕获手势。
  const browse = (await page.locator('.aq-sea').boundingBox())!
  await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: browse.x + browse.width - 20, y: browse.y + 4 }] })
  for (const offset of [50, 90, 140, 190]) await cdp.send('Input.dispatchTouchEvent', {
    type: 'touchMove', touchPoints: [{ x: browse.x + browse.width - offset, y: browse.y + 4 }],
  })
  await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] })
  await expect.poll(() => page.locator('.aq-sea').evaluate(el => el.scrollLeft)).toBeGreaterThan(80)
  await expect(page.locator('.aq-reorder[open]')).toHaveCount(0)
  expect(writes).toBe(0)
})

test('取消让位尚未结束时再次抓船，不跳回原格；详情慢读不改变海面高度', async ({ page }) => {
  await loginAnchorage(page)
  await page.addStyleTag({ content: '.aq-vessel { transition-duration: 1.5s; }' })
  const ship = page.locator('.aq-vessel').nth(2), first = page.locator('.queue-ship').first()
  const from = (await ship.locator('.queue-ship').boundingBox())!, to = (await first.boundingBox())!
  await page.mouse.move(from.x + from.width/2, from.y + from.height/2); await page.mouse.down()
  await page.mouse.move(to.x + to.width/2, to.y + to.height/2, { steps: 10 }); await page.mouse.up()
  await expect(page.locator('.aq-reorder[open]')).toBeVisible()
  await page.getByRole('button', { name: '取消', exact: true }).click()
  const point = (await ship.locator('.queue-ship').boundingBox())!
  await page.mouse.move(point.x + point.width/2, point.y + point.height/2); await page.mouse.down()
  const grabbed = (await ship.boundingBox())!
  await page.mouse.move(point.x + point.width/2 + 40, point.y + point.height/2, { steps: 5 })
  const moved = (await ship.boundingBox())!
  expect(Math.abs(moved.x - grabbed.x - 40)).toBeLessThan(6)
  await page.keyboard.press('Escape'); await page.mouse.up()
  await expect(page.locator('.aq-reorder[open]')).toHaveCount(0)

  // 延迟真实GET响应而非伪造数据，验证选中变化不会伸缩正在操作的画布。
  let release!: () => void
  const held = new Promise<void>(resolve => release = resolve)
  await page.route('**/api/anchorage-stays/*', async route => { const response = await route.fetch(); await held; await route.fulfill({ response }) })
  const before = (await page.locator('.aq-sea').boundingBox())!
  await page.locator('.aq-select').first().click()
  await expect(page.locator('.aq-detail')).toHaveAttribute('aria-busy', 'true')
  const during = (await page.locator('.aq-sea').boundingBox())!
  expect(Math.abs(during.y - before.y)).toBeLessThan(1)
  expect(Math.abs(during.height - before.height)).toBeLessThan(1)
  release()
  await expect(page.locator('.aq-detail')).toHaveAttribute('aria-busy', 'false')
})

for (const role of ['pq.reviewer', 'pq.recorder']) test(`${role}可选择船舶但不能拖动调序`, async ({ page }) => {
  await loginAnchorage(page, role)
  await expect(page.locator('.aq-grab, .aq-select.can-drag')).toHaveCount(0)
  const source = page.locator('.aq-select').nth(1)
  await source.click()
  await expect(source).toHaveAttribute('aria-pressed', 'true')
  const from = (await source.boundingBox())!, to = (await page.locator('.aq-select').first().boundingBox())!
  await page.mouse.move(from.x + from.width/2, from.y + from.height/2); await page.mouse.down()
  await page.mouse.move(to.x + to.width/2, to.y + to.height/2, { steps: 10 }); await page.mouse.up()
  await expect(page.locator('.aq-reorder[open]')).toHaveCount(0)
  await expect(page.getByRole('button', { name: '移至第几位', exact: true })).toHaveCount(0)
})

test('宽屏分配主区空间，详情可读且没有大块底部空洞', async ({ page }) => {
  await loginAnchorage(page, 'pq.admin')
  mkdirSync('../../artifacts/stage4-repair', { recursive: true })
  for (const [width, height] of [[2538, 1393], [1920, 1080], [1920, 900], [1680, 892], [1440, 900], [1366, 768]]) {
    await page.setViewportSize({ width, height })
    await expect(page.locator('.aq-facts')).toBeVisible()
    await page.screenshot({ path: `../../artifacts/stage4-repair/layout-${width}.png`, fullPage: true })
    const metrics = await page.locator('.anchorage-workspace').evaluate(element => {
      const detail = element.querySelector('.aq-detail')!, journey = element.querySelector('.aq-journey')!
      return {
        bottomGap: element.getBoundingClientRect().bottom - journey.getBoundingClientRect().bottom,
        detailWidth: detail.getBoundingClientRect().width,
        labelSize: parseFloat(getComputedStyle(detail.querySelector('dt')!).fontSize),
        valueSize: parseFloat(getComputedStyle(detail.querySelector('dd')!).fontSize),
        overflow: element.scrollHeight - element.clientHeight,
      }
    })
    console.log(`${width}×${height}`, metrics)
    expect.soft(metrics.bottomGap).toBeLessThanOrEqual(24)
    expect.soft(metrics.bottomGap).toBeGreaterThanOrEqual(0)
    expect.soft(metrics.overflow).toBeLessThanOrEqual(2)
    expect.soft(metrics.detailWidth).toBeGreaterThanOrEqual(330)
    expect.soft(metrics.labelSize).toBeGreaterThanOrEqual(14)
    expect.soft(metrics.valueSize).toBeGreaterThanOrEqual(14)
  }
})
