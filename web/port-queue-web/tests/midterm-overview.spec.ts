// 中期范围和布局验收；后续入口明确未接入，截图覆盖短窗口与宽屏。
import { test, expect } from '@playwright/test'

test('中期只开放总览与预约，主图保留四入口且不打开后续列表', async ({ page }) => {
  const businessRequests: string[] = []
  page.on('request', request => { if (request.url().includes('/api/business/')) businessRequests.push(request.url()) })
  await page.goto('/')
  await expect(page.locator('#port-summary-title')).toHaveText('上海洋山深水港')
  await expect(page.locator('.port-code')).toContainText('SH-YS')
  for (const name of ['锚地排队', '泊位计划', '进出港记录']) {
    await expect(page.getByRole('navigation').getByRole('button', { name: `${name} 待接入` })).toBeDisabled()
    const entry = page.locator('.port-business-flow').getByRole('button', { name, exact: true })
    await entry.click()
    const dialog = page.getByRole('dialog', { name })
    await expect(dialog).toContainText('该模块尚未接入')
    await dialog.getByRole('button', { name: '返回总览' }).click()
    await expect(entry).toBeFocused()
  }
  for (const hash of ['queue', 'plans', 'events']) {
    await page.goto(`/#${hash}`)
    await expect(page.locator('.page-title h1')).toHaveText('总览')
    expect((await page.request.get(`/api/business/${hash}`)).status()).toBe(404)
  }
  expect(businessRequests).toHaveLength(0)
  await expect(page.getByRole('heading', { name: '作业分布' })).toHaveCount(0)
  await expect(page.getByRole('heading', { name: '进港流程' })).toBeVisible()
  await page.locator('.port-business-flow').getByRole('button', { name: '进港预约', exact: true }).click()
  await expect(page).toHaveURL(/#appointments$/)
  await expect(page.locator('.appointments-pane tbody tr').first()).toBeVisible()
})

test('中期预约分布页脚完整且各面板不重叠', async ({ page }) => {
  await page.goto('/')
  await expect(page.locator('.state-distribution button')).toHaveCount(6)
  for (const [width, height] of [[1440, 900], [1920, 1080], [2470, 900], [1280, 720], [973, 800], [390, 844], [320, 740]]) {
    await page.setViewportSize({ width: width!, height: height! })
    await page.locator('.overview-pane').evaluate(el => el.scrollTop = 0)
    await page.evaluate(() => window.scrollTo(0, 0))
    const geometry = await page.locator('.status-summary').evaluate(panel => {
      const p = panel.getBoundingClientRect(), button = panel.querySelector('.distribution-footer button')!.getBoundingClientRect()
      const next = document.querySelector('.overview-bottom')!.getBoundingClientRect()
      return { bottomInset: p.bottom - button.bottom, insideX: button.left >= p.left && button.right <= p.right, separation: next.top - p.bottom }
    })
    expect(geometry.bottomInset).toBeGreaterThanOrEqual(16)
    expect(geometry.insideX).toBeTruthy()
    expect(geometry.separation).toBeGreaterThanOrEqual(15)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy()
    await page.screenshot({ path: `../../.impeccable/review/midterm-overview-${width}.png`, fullPage: true, animations: 'disabled' })
    if (width === 2470 || width === 1440) {
      await page.locator('.overview-pane').evaluate(el => el.scrollTop = el.scrollHeight)
      await page.screenshot({ path: `../../.impeccable/review/midterm-process-${width}.png`, fullPage: true, animations: 'disabled' })
    }
  }
  await page.getByRole('button', { name: '查看全部预约', exact: true }).click()
  await expect(page.locator('.page-title h1')).toHaveText('进港预约')
})
