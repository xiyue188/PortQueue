/* 从仓库根目录运行 node docs/design/anchorage-prototype/verify.cjs。
   验证4A的浏览器内交互，截图进入被Git忽略的.impeccable/review。 */
const { chromium } = require('../../../web/port-queue-web/node_modules/@playwright/test');
const { pathToFileURL } = require('node:url');
const path = require('node:path');
const fs = require('node:fs');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../../..');
const evidence = path.join(root, '.impeccable/review/anchorage-4a');
fs.mkdirSync(evidence, { recursive: true });

async function main() {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const errors = [];
  const results = [];
  try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(pathToFileURL(path.join(__dirname, 'index.html')).href);
    await page.waitForFunction(() => document.querySelectorAll('.vessel').length === 5);
    for (const [name, width, height] of [['desktop',1440,900],['short',1366,768],['wide',1920,1080],['mobile',390,844]]) {
      await page.setViewportSize({width,height});
      await page.screenshot({path:path.join(evidence,`${name}.png`),fullPage:true});
      assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${name}: horizontal page overflow`);
      results.push(`${name}: complete render, no page overflow`);
    }
    await page.setViewportSize({width:1440,height:900});
    // 核验真正的指针拖动，不能用直接修改DOM顺序替代。
    const source = await page.locator('[data-drag="donglan"]').boundingBox();
    const target = await page.locator('[data-drag="hailan"]').boundingBox();
    await page.mouse.move(source.x+source.width/2,source.y+source.height/2);
    await page.mouse.down();
    await page.mouse.move(target.x+target.width/2,target.y+target.height/2,{steps:16});
    await page.waitForTimeout(300); // 等相邻船舶的240ms让位动画结束后取证。
    assert.equal(await page.locator('#sea').evaluate(el => el.scrollLeft),0);
    await page.screenshot({path:path.join(evidence,'dragging.png'),fullPage:true});
    await page.mouse.up();
    assert(await page.locator('#reorder-dialog').isVisible());
    assert.equal(await page.locator('#new-position').inputValue(),'1');
    assert.equal(await page.locator('#affected-ships li').count(),3);
    await page.locator('#reason').fill('按现场作业安排调整');
    await page.screenshot({path:path.join(evidence,'confirm.png'),fullPage:true});
    await page.getByRole('button',{name:'确认调整',exact:true}).click();
    assert.equal(await page.locator('[data-id="donglan"] .rank').innerText(),'2');
    assert.equal(await page.locator('#presence').innerText(),'6 / 8 艘');
    results.push('drag 5 → 2: three other vessels shift; presence remains 6');
    await page.getByRole('button',{name:'调整顺位',exact:true}).click();
    await page.locator('#new-position').selectOption('4');
    await page.keyboard.press('Escape');
    assert.equal(await page.locator('[data-id="donglan"] .rank').innerText(),'2');
    results.push('Escape: preview discarded and saved order restored');
    await page.locator('[data-drag="donglan"]').focus();
    await page.keyboard.press('Enter');
    await page.locator('#new-position').selectOption('0');
    await page.locator('#reason').fill('   ');
    await page.getByRole('button',{name:'确认调整',exact:true}).click();
    assert.equal(await page.locator('#reorder-error').innerText(),'请填写调整原因。');
    await page.locator('#reason').fill('优先作业安排');
    await page.getByRole('button',{name:'确认调整',exact:true}).click();
    assert.equal(await page.locator('[data-id="donglan"] .rank').innerText(),'1');
    results.push('keyboard reorder: required reason checked; move to first confirmed');
    await page.locator('#search').fill('海曙');
    await page.locator('#search').press('Enter');
    assert.equal(await page.locator('#selected-name').innerText(),'海曙');
    assert.equal(await page.locator('.vessel').count(),5);
    await page.locator('[data-step="2"]').click();
    assert(await page.locator('#visit-dialog').isVisible());
    await page.locator('[data-timeline="0"]').click();
    assert.equal(await page.locator('[data-timeline="0"]').getAttribute('aria-expanded'),'true');
    await page.keyboard.press('Escape');
    results.push('search selects a vessel without filtering the queue; timeline expands');
    await page.locator('[data-area="east"]').click();
    assert.equal(await page.locator('.vessel').count(),2);
    await page.locator('[data-area="north"]').click();
    assert(await page.locator('#empty-state').isVisible());
    assert(await page.locator('#position-button').isDisabled());
    await page.locator('#reset').click();
    assert.equal(await page.locator('[data-id="donglan"] .rank').innerText(),'5');
    results.push('area switching, empty state and reset return consistent counts');
    await page.setViewportSize({width:390,height:844});
    await page.getByRole('button',{name:'调整顺位',exact:true}).click();
    await page.locator('#new-position').selectOption('1');
    await page.screenshot({path:path.join(evidence,'mobile-confirm.png')});
    assert(await page.getByRole('button',{name:'确认调整',exact:true}).isVisible());
    await page.keyboard.press('Escape');
    await page.emulateMedia({reducedMotion:'reduce'});
    assert.equal(await page.locator('.vessel').first().evaluate(el => getComputedStyle(el).transitionDuration),'0s');
    results.push('mobile confirmation visible; reduced motion disables movement');
    // 真实触摸事件核验屏外目标：手指停在左缘，画布需自行滚到第一位。
    const touchPage = await browser.newPage({viewport:{width:390,height:844},hasTouch:true,isMobile:true});
    touchPage.on('pageerror',error => errors.push(error.message));
    await touchPage.goto(pathToFileURL(path.join(__dirname,'index.html')).href);
    await touchPage.waitForFunction(() => document.getElementById('sea').scrollLeft > 0);
    const touchHandle = touchPage.locator('[data-drag="donglan"]');
    const handleBox = await touchHandle.boundingBox();
    assert(handleBox.height >= 44 && handleBox.width >= 44);
    const seaBox = await touchPage.locator('#sea').boundingBox();
    const session = await touchPage.context().newCDPSession(touchPage);
    const point = {x:handleBox.x+handleBox.width/2,y:handleBox.y+handleBox.height/2};
    await session.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[point]});
    await session.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x:seaBox.x+8,y:point.y}]});
    await touchPage.waitForFunction(() => document.querySelector('[data-id="donglan"] .rank').textContent === '1');
    await touchPage.screenshot({path:path.join(evidence,'mobile-dragging.png')});
    await session.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
    assert.equal(await touchPage.locator('#new-position').inputValue(),'0');
    assert.equal(await touchPage.locator('#affected-ships li').count(),4);
    await touchPage.getByRole('button',{name:'取消',exact:true}).click();
    assert.equal(await touchPage.locator('[data-id="donglan"] .rank').innerText(),'5');
    const stoppedAt = await touchPage.locator('#sea').evaluate(el => el.scrollLeft);
    await touchPage.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
    assert.equal(await touchPage.locator('#sea').evaluate(el => el.scrollLeft),stoppedAt);
    results.push('mobile touch 5 → 1: edge scroll reaches hidden first slot; four shifts; cancel restores; scroll stops');
    await touchPage.close();
    assert.deepEqual(errors,[]);
    fs.writeFileSync(path.join(evidence,'verification.json'),JSON.stringify({results,errors},null,2));
    console.log(results.join('\n'));
    console.log('Browser errors: 0');
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode=1; });
