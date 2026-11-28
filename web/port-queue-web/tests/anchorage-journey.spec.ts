// 真实API回归：跨模块入口依据停留事实，确认动作时间不冒充未来靠泊时间。
import { test, expect } from '@playwright/test'
import { mkdirSync } from 'node:fs'
import { loginAnchorage } from './anchorage-support'
import { formatDate } from '../src/utils/appointmentPresentation'
const evidence = '../../artifacts/stage4-closure'

test('本次到访显示各船事实，计划节点定位确认记录', async ({ page }) => {
  await loginAnchorage(page)
  const list = await (await page.request.get('/api/anchorages')).json()
  const area = list.items.find((a: {code:string}) => a.code === 'ANCH-OUTER-A')
  await page.getByRole('button', { name: new RegExp(area.name) }).click()
  const queue = await (await page.request.get(`/api/anchorages/${area.anchorageId}/queue`)).json()
  expect(new Set(queue.items.map((s: {enteredAt:string}) => s.enteredAt)).size).toBeGreaterThan(5)
  const assigned = queue.items.find((s: {confirmedBerthingAt:string|null}) => s.confirmedBerthingAt)
  expect(assigned).toBeTruthy()
  await page.getByRole('searchbox', { name:'查找队列船舶' }).fill(assigned.shipName)
  await page.getByRole('searchbox', { name:'查找队列船舶' }).press('Enter')
  await expect(page.locator('.aq-identity h2')).toHaveText(assigned.shipName)
  const detail = await (await page.request.get(`/api/anchorage-stays/${assigned.queueEntryId}`)).json()
  const plan = page.locator('.aq-steps button').filter({hasText:'计划确认'})
  await expect(plan.locator('time')).toHaveText(formatDate(detail.planConfirmedAt))
  expect(detail.planConfirmedAt).not.toBe(detail.confirmedBerthingAt)
  await plan.click()
  await expect(page.locator('details[data-record="plan"]')).toHaveAttribute('open','')
  await expect(page.locator('details[data-record="plan"]')).toContainText(`计划靠泊：${formatDate(detail.confirmedBerthingAt)}`)
  await page.getByRole('button',{name:'关闭到访时间线'}).click()
  await page.getByRole('searchbox',{name:'查找队列船舶'}).fill('')
  mkdirSync(evidence,{recursive:true})
  for (const [width,height,name] of [[1440,900,'anchorage-desktop'],[390,844,'anchorage-mobile']] as const) {
    await page.setViewportSize({width,height})
    await page.locator('.aq-select[aria-pressed="true"]').scrollIntoViewIfNeeded()
    await expect.poll(()=>page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true)
    await page.screenshot({path:`${evidence}/${name}.png`,fullPage:true})
  }
})

test('有停留的预约在详情内部往返，无停留草稿不显示入口', async ({page}) => {
  await loginAnchorage(page)
  await expect(page.locator('.aq-identity h2')).toBeVisible()
  const ship = await page.locator('.aq-identity h2').innerText()
  await page.getByRole('button',{name:'查看进港预约',exact:true}).click()
  const link = page.locator('#appointment-detail').getByRole('button',{name:'查看锚地停留'})
  await expect(link).toBeVisible()
  await link.scrollIntoViewIfNeeded()
  const spacing = await link.evaluate(el => {
    const panel=el.closest('#appointment-detail')!, footer=el.closest('footer')!
    return {bottom:panel.getBoundingClientRect().bottom-el.getBoundingClientRect().bottom,
      margin:parseFloat(getComputedStyle(footer).marginTop)}
  })
  expect(spacing.bottom).toBeGreaterThanOrEqual(16)
  expect(spacing.margin).toBeGreaterThanOrEqual(16)
  await page.screenshot({path:`${evidence}/appointment-link-desktop.png`,fullPage:true})
  await link.click()
  await expect(page.locator('.aq-identity h2')).toHaveText(ship)
  await page.getByRole('button',{name:'进港预约',exact:true}).click()
  await page.getByRole('button',{name:'草稿',exact:true}).click()
  await expect(page.locator('.records-panel')).toHaveAttribute('aria-busy','false')
  await page.locator('.records-panel tbody tr').first().click()
  await expect(page.locator('#appointment-detail')).toBeVisible()
  await expect(page.locator('.detail-host')).toHaveAttribute('aria-busy','false')
  await expect(page.getByRole('button',{name:'查看锚地停留'})).toHaveCount(0)
  await page.setViewportSize({width:390,height:844})
  await page.screenshot({path:`${evidence}/appointment-mobile.png`,fullPage:true})
})
