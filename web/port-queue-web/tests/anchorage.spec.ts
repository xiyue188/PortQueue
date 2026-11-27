// 阶段4浏览器验收走真实API与本机数据库，截图进入忽略目录；不使用route.fulfill伪造业务。
import { test, expect } from '@playwright/test'
import { loginAnchorage as login } from './anchorage-support'
import { mkdirSync } from 'node:fs'
const evidence = '../../artifacts/stage4'
test('匿名锚地入口明确要求登录',async({page}) => {
  await page.goto('/#queue')
  await expect(page.getByText('请在账号区登录后继续。')).toBeVisible()
})
test('完整真实队列、搜索选择与到访返回',async({page}) => {
  await login(page)
  const response = await (await page.request.get('/api/anchorages')).json()
  const busiest = response.items.reduce((a: {queueCount:number},b: {queueCount:number}) => a.queueCount > b.queueCount ? a : b)
  expect(busiest.queueCount).toBeGreaterThan(5)
  await page.getByRole('button',{name:new RegExp(busiest.name)}).click()
  await expect(page.locator('.aq-vessel')).toHaveCount(busiest.queueCount)
  const queue = await (await page.request.get(`/api/anchorages/${busiest.anchorageId}/queue`)).json()
  await expect(page.locator('.aq-occupancy strong')).toHaveText(String(queue.occupiedCount))
  const last = queue.items.at(-1)
  await page.getByRole('searchbox',{name:'查找队列船舶'}).fill(last.shipName)
  await page.getByRole('searchbox',{name:'查找队列船舶'}).press('Enter')
  await expect(page.locator('.aq-identity h2')).toHaveText(last.shipName)
  await expect(page.locator('.aq-vessel')).toHaveCount(busiest.queueCount)
  await page.getByRole('button',{name:'展开时间线',exact:true}).click()
  await expect(page.locator('.aq-dialog[open]')).toContainText(last.callNo)
  await page.getByRole('button',{name:'关闭到访时间线'}).click()
  await page.getByRole('button',{name:'查看进港预约',exact:true}).click()
  await expect(page.locator('#appointment-detail')).toContainText(last.shipName)
  await page.getByRole('button',{name:'锚地排队',exact:true}).click()
  await expect(page.locator('.aq-identity h2')).toHaveText(last.shipName)
})
test('正常缩放桌面时间线完整、窄屏无横向溢出',async({page}) => {
  mkdirSync(evidence,{recursive:true})
  const errors: string[] = []; page.on('pageerror',e=>errors.push(e.message))
  await login(page)
  const response = await (await page.request.get('/api/anchorages')).json()
  const populated = response.items.find((a: {queueCount:number}) => a.queueCount > 5)
  await page.getByRole('button',{name:new RegExp(populated.name)}).click()
  await expect(page.locator('.aq-journey')).toBeVisible()
  for (const [name,width,height] of [['desktop',1440,900],['short',1366,768],['user',1680,892],['mobile',390,844]] as const) {
    await page.setViewportSize({width,height})
    await expect.poll(()=>page.evaluate(()=>document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    if (width>1000) {
      expect(await page.locator('.aq-journey').evaluate(el => el.getBoundingClientRect().bottom)).toBeLessThanOrEqual(height-20)
      expect(await page.locator('.anchorage-workspace').evaluate(el=>el.scrollHeight <= el.clientHeight+2)).toBeTruthy()
    }
    await page.screenshot({path:`${evidence}/4b-${name}.png`,fullPage:true})
  }
  expect(errors).toEqual([])
})
