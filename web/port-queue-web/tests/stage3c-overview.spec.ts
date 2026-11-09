// 只读页面验收与成组视觉证据；不向开发库写入测试业务。
import { test, expect } from '@playwright/test'

test('3C港口概览、四个业务入口与查询保留', async ({ page }) => {
  await page.goto('/')
  const overview = await (await page.request.get('/api/overview')).json()
  await expect(page.locator('#port-summary-title')).toHaveText(overview.port.name)
  await expect(page.locator('.port-facilities')).toBeVisible()
  for (const [label, hash] of [['锚地排队', 'queue'], ['泊位计划', 'plans'], ['进出港记录', 'events'], ['进港预约', 'appointments']]) {
    await page.locator('.port-business-flow').getByRole('button', { name: label, exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`#${hash}$`))
    await expect(page.locator('.page-title h1')).toHaveText(label!)
    if (hash !== 'appointments') {
      await expect(page.locator('.business-pane:not([hidden]) tbody tr').first()).toBeVisible()
      await page.screenshot({ path: `../../.impeccable/review/stage3c-${hash}.png`, fullPage: true })
    }
    await page.getByRole('navigation').getByRole('button', { name: '总览', exact: true }).click()
  }
  const operation = page.getByRole('region', { name: '作业分布' })
  await operation.getByRole('button', { name: '泊位', exact: true }).click()
  await expect(operation.locator('.operation-bar-row')).toHaveCount(3)
  await operation.getByRole('button', { name: '事件', exact: true }).click()
  await expect(operation).toContainText('累计执行事件')
  await operation.getByRole('button', { name: '锚地', exact: true }).click()
  for (const width of [1440, 2470, 973, 390, 320]) {
    await page.setViewportSize({ width, height: width < 700 ? 844 : 1024 })
    await page.evaluate(() => window.scrollTo(0, 0))
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy()
    const footerFits = await page.locator('.status-summary').evaluate(panel => panel.querySelector('button.text-action')!.getBoundingClientRect().bottom <= panel.getBoundingClientRect().bottom)
    expect(footerFits).toBeTruthy()
    await page.screenshot({ path: `../../.impeccable/review/stage3c-overview-${width}.png`, fullPage: true, animations: 'disabled' })
  }
})

test('3C业务查询失败重试保留关键词，往返保留分页与关联详情', async ({ page }) => {
  await page.goto('/#events')
  const region = page.locator('.business-pane:not([hidden])')
  await expect(region.locator('tbody tr').first()).toBeVisible()
  const keyword = (await region.locator('tbody strong').first().innerText()).slice(0, 2)
  await page.route('**/api/business/events?**', async route => {
    await route.fulfill({ status: 503, contentType: 'application/json', body: '{}' })
  }, { times: 1 })
  await region.getByLabel('进出港记录关键词').fill(keyword)
  await region.getByRole('button', { name: '查询', exact: true }).click()
  await expect(region.getByRole('alert')).toContainText('暂时无法读取')
  await expect(region.locator('tbody tr')).toHaveCount(0)
  const requested = page.waitForRequest(r => new URL(r.url()).pathname === '/api/business/events')
  await region.getByRole('button', { name: '重新读取' }).click()
  expect(new URL((await requested).url()).searchParams.get('keyword')).toBe(keyword)
  await expect(region.locator('tbody tr').first()).toBeVisible()
  const names = await region.locator('tbody strong').allTextContents()
  expect(names.every(name => name.includes(keyword))).toBeTruthy()
  await page.getByRole('navigation').getByRole('button', { name: '总览', exact: true }).click()
  await page.getByRole('navigation').getByRole('button', { name: '进出港记录', exact: true }).click()
  await expect(region.getByLabel('进出港记录关键词')).toHaveValue(keyword)
  const ship = await region.locator('tbody strong').first().innerText()
  await region.getByRole('button', { name: `查看${ship}的预约` }).first().click()
  await expect(page.locator('#appointment-detail')).toContainText(ship)
  await page.getByRole('navigation').getByRole('button', { name: '进出港记录', exact: true }).click()
  await region.getByRole('button', { name: '重置', exact: true }).click()
  await page.route('**/api/business/events?**', async route => {
    await route.fulfill({ status: 503, contentType: 'application/json', body: '{}' })
  }, { times: 1 })
  await region.getByRole('button', { name: '下一页' }).click()
  await expect(region.getByRole('alert')).toContainText('暂时无法读取')
  const retriedPage = page.waitForRequest(r => new URL(r.url()).pathname === '/api/business/events')
  await region.getByRole('button', { name: '重新读取' }).click()
  expect(new URL((await retriedPage).url()).searchParams.get('page')).toBe('2')
  await expect(region.locator('.pagination')).toContainText('第 2 页')
  await page.getByRole('navigation').getByRole('button', { name: '总览', exact: true }).click()
  await page.getByRole('navigation').getByRole('button', { name: '进出港记录', exact: true }).click()
  await expect(region.locator('.pagination')).toContainText('第 2 页')
})
