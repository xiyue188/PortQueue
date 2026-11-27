/* 4A独立原型使用固定场景，只修改浏览器内存。正式办理的数据与授权由4B—4D接入。 */
'use strict';

const icons = {
  brand: '<path d="M3 8c4-5 7-5 11-1s6 4 9-1M3 14c4-5 7-5 11-1s6 4 9-1M3 20c4-5 7-5 11-1s6 4 9-1"/>',
  home: '<path d="m3 11 9-8 9 8M5 10v11h5v-7h4v7h5V10"/>',
  anchor: '<circle cx="12" cy="5" r="2"/><path d="M12 7v14M8 10h8M3 14c0 5 4 7 9 7s9-2 9-7M3 14l-1 4M3 14l4 1M21 14l1 4M21 14l-4 1"/>',
  document: '<path d="M6 2h8l4 4v16H6zM14 2v5h4M9 11h6M9 15h6"/>',
  berth: '<path d="M3 19h18M5 19V8l7-5 7 5v11M3 8h18M9 19v-5h6v5"/>',
  clock: '<circle cx="12" cy="12" r="9"/><path d="M12 6v6l4 2"/>',
  chart: '<path d="M4 20V10h4v10M10 20V5h4v15M16 20V2h4v18"/>',
  search: '<circle cx="10" cy="10" r="6"/><path d="m15 15 6 6"/>',
  info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v6M12 7v.1"/>',
  sort: '<path d="M6 3v18m-3-3 3 3 3-3M18 21V3m-3 3 3-3 3 3M11 8h3M11 12h3M11 16h3"/>',
  pause: '<path d="M8 5v14M16 5v14"/>',
  play: '<path d="m9 4 11 8-11 8z"/>',
  refresh: '<path d="M20 7a9 9 0 1 0 1 8M20 2v6h-6"/>',
  arrow: '<path d="M4 12h15m-5-5 5 5-5 5"/>',
  close: '<path d="m6 6 12 12M6 18 18 6"/>',
  ship: '<path d="m3 15 9-4 9 4-3 6H6zM7 13V7h10v6M10 7V3h4v4M12 11v10"/>',
  grip: '<circle cx="7" cy="7" r=".8"/><circle cx="12" cy="7" r=".8"/><circle cx="17" cy="7" r=".8"/><circle cx="7" cy="13" r=".8"/><circle cx="12" cy="13" r=".8"/><circle cx="17" cy="13" r=".8"/>',
  check: '<path d="m5 12 4 4 10-10"/>',
};
const icon = name => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${icons[name] || icons.anchor}</svg>`;
document.querySelectorAll('[data-icon]').forEach(el => { el.innerHTML = icon(el.dataset.icon); });
const $ = id => document.getElementById(id);

// 可核算的固定参照时刻为2026-11-27 10:00，所有停留时长由入锚时刻计算。
const ships = [
  { id: 'yuanfan', name: '远帆', call: 'YS-20261127-036', enter: '06:15', estimate: '10:30', confirmed: '11:00', status: '已安排', hue: '#256c99' },
  { id: 'hailan', name: '海岚', call: 'YS-20261127-038', enter: '06:38', estimate: '11:30', confirmed: '', status: '待安排', hue: '#508ea9' },
  { id: 'haishu', name: '海曙', call: 'YS-20261127-039', enter: '07:02', estimate: '', confirmed: '', status: '待安排', hue: '#3b7da9' },
  { id: 'qihang', name: '启航', call: 'YS-20261127-040', enter: '07:36', estimate: '12:00', confirmed: '', status: '待安排', hue: '#236286' },
  { id: 'donglan', name: '东澜', call: 'YS-20261127-042', enter: '08:10', estimate: '12:30', confirmed: '', status: '待安排', hue: '#2978a9' },
  { id: 'haichen', name: '海辰', call: 'YS-20261127-035', enter: '05:45', estimate: '', confirmed: '', status: '已撤销', hue: '#7c9aac' },
  { id: 'xingzhou', name: '星舟', call: 'YS-20261127-044', enter: '09:00', estimate: '14:00', confirmed: '', status: '待安排', hue: '#3e819d' },
  { id: 'haiyue', name: '海岳', call: 'YS-20261127-046', enter: '09:20', estimate: '15:00', confirmed: '', status: '待安排', hue: '#2a648e' },
];
const initialAreas = {
  south: { name: '南锚地', capacity: 8, order: ['yuanfan','hailan','haishu','qihang','donglan'], cancelled: ['haichen'] },
  east: { name: '东锚地', capacity: 6, order: ['xingzhou','haiyue'], cancelled: [] },
  north: { name: '北锚地', capacity: 6, order: [], cancelled: [] },
};
let areas = structuredClone(initialAreas);
let areaKey = 'south';
let selectedId = 'donglan';
let previewOrder = null;
let pending = null;
let drag = null;
let lastTrigger = null;
let movementDisabled = matchMedia('(prefers-reduced-motion: reduce)').matches;
const area = () => areas[areaKey];
const shipById = id => ships.find(ship => ship.id === id);

// 船形是顺位控件中的矢量图标。集装箱分格和船桥使抓取对象具备航运辨识度。
function boatArt(ship) {
  const containers = [36,52,68,84,100].map((y, row) => [24,39].map((x, col) =>
    `<rect x="${x}" y="${y}" width="13" height="14" rx="1" fill="${(row+col)%3 === 0 ? '#a6ccd9' : ship.hue}" stroke="#d5e9ef" stroke-width=".6"/><path d="M${x+3} ${y+2}v10m4-10v10m3-10v10" stroke="#d2e5ec" opacity=".35" stroke-width=".6"/>`).join('')).join('');
  return `<svg class="boat-art" viewBox="0 0 76 174" fill="none" aria-hidden="true"><path d="M14 143C8 118 9 80 13 52M62 143c6-25 5-63 1-91" stroke="#edfaff" stroke-width="2" opacity=".55"/><path d="M38 4C24 15 17 26 17 40v110l5 16h32l5-16V40C59 26 52 15 38 4Z" fill="#153d59" stroke="#e8f5f9" stroke-width="2"/><path d="M38 11C28 20 22 29 22 41v106l4 13h24l4-13V41C54 29 48 20 38 11Z" fill="#79a4b8"/><path d="m38 14-7 14h14z" fill="#d0e5eb"/>${containers}<rect x="23" y="120" width="30" height="8" rx="2" fill="#e7f4f7"/><rect x="25" y="130" width="26" height="17" rx="2" fill="#bcd7e3"/><path d="M28 133h20M28 138h20" stroke="#315a71" stroke-width="2"/><rect x="33" y="149" width="10" height="7" rx="1" fill="#e7f4f7"/><path d="M38 116v12M31 123h14" stroke="#294d65" stroke-width="1.3"/></svg>`;
}
function waiting(ship) {
  const [hours, minutes] = ship.enter.split(':').map(Number);
  const duration = 600 - hours * 60 - minutes;
  return `${Math.floor(duration / 60)}小时${duration % 60}分`;
}
function reposition(order, id, destination) {
  const next = order.filter(item => item !== id);
  next.splice(destination, 0, id);
  return next;
}
function renderQueue() {
  const members = area().order;
  $('queue-vessels').innerHTML = members.map((id, index) => {
    const ship = shipById(id);
    return `<div class="vessel${id === selectedId ? ' selected' : ''}" data-id="${id}" role="listitem" style="--slot:${index}"><span class="rank">${index+1}</span><button class="vessel-select" data-select="${id}" aria-label="选择${ship.name}，第${index+1}位"><span>${boatArt(ship)}</span><strong class="ship-name">${ship.name}</strong><span class="ship-wait">等待 ${waiting(ship)}</span></button><button class="drag-handle" data-drag="${id}" aria-label="调整${ship.name}顺位" title="拖动调整顺位，也可按回车选择位置">${icon('grip')}</button></div>`;
  }).join('');
  $('queue-count').textContent = `${members.length} 艘`;
  $('empty-state').hidden = members.length > 0;
  document.querySelector('.queue-path').hidden = !members.length;
  document.querySelector('.sea-caption').hidden = !members.length;
  const presence = members.length + area().cancelled.length;
  $('presence').innerHTML = `${presence} <small>/ ${area().capacity} 艘</small>`;
  $('available').textContent = `余位 ${area().capacity-presence}`;
  $('capacity-bars').innerHTML = Array.from({length: area().capacity}, (_, index) => `<i class="${index < presence ? 'filled' : ''}"></i>`).join('');
  $('cancelled-row').hidden = !area().cancelled.length;
  $('anchorage-title').textContent = area().name;
  document.querySelectorAll('[data-area]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.area === areaKey)));
  updateSelection();
  updateSearch();
  keepSelectedVisible();
}
function keepSelectedVisible() {
  if (innerWidth > 760) { $('sea').scrollLeft = 0; return; }
  const element = document.querySelector(`.vessel[data-id="${selectedId}"]`);
  if (!element) return;
  const center = element.offsetLeft + element.offsetWidth / 2;
  $('sea').scrollLeft = Math.max(0, center - $('sea').clientWidth / 2 + 12);
}
function stepsFor(ship) {
  const cancelled = ship.status === '已撤销';
  return [
    { title:'预约已提交', time:'05:10', detail:`${ship.name}的进港申请已提交，关联本次到访 ${ship.call}。`, done:true },
    { title:'审核已批准', time:'05:30', detail:'申请已批准，可继续登记入锚或安排泊位。', done:true },
    { title:'实际入锚', time:ship.enter, detail:`${ship.enter}进入${area().name}，当前等待${waiting(ship)}。`, done:true, active:!cancelled },
    { title:cancelled ? '预约已撤销' : ship.confirmed ? '泊位已确认' : '泊位待安排', time:cancelled ? '09:40' : ship.confirmed || '待办理', detail:cancelled ? '09:40撤销预约，保留实际在场记录，下一步登记离锚。' : ship.confirmed ? `已确认11:00靠泊，实际离锚与进港尚待登记。` : '等待调度员选择合适的泊位和时间窗。', done:cancelled || !!ship.confirmed, active:cancelled },
    { title:cancelled ? '实际离锚' : '实际进港', time:'待登记', detail:cancelled ? '实际离锚后，该次停留结束并释放在场容量。' : '实际离锚后再登记进港，实际时间由作业记录形成。', done:false },
  ];
}
function updateSelection() {
  const ship = shipById(selectedId);
  document.querySelector('.visit-panel').hidden = !ship;
  document.querySelector('.journey-panel').hidden = !ship;
  $('position-button').disabled = !ship || !area().order.includes(ship.id) || area().order.length < 2;
  document.querySelectorAll('.vessel').forEach(el => el.classList.toggle('selected', el.dataset.id === selectedId));
  if (!ship) return;
  $('selected-name').textContent = ship.name;
  $('selected-status').textContent = ship.status;
  $('selected-status').className = `status${ship.confirmed ? ' assigned' : ''}`;
  $('visit-number').textContent = ship.call;
  $('entered-time').textContent = ship.enter;
  $('waiting-time').textContent = waiting(ship);
  $('estimated-time').textContent = ship.estimate || '—';
  $('confirmed-time').textContent = ship.confirmed || '—';
  $('plan-button').disabled = ship.status === '已撤销';
  $('plan-button').innerHTML = `${ship.confirmed ? '查看泊位安排' : '安排泊位'} ${icon('arrow')}`;
  $('journey').innerHTML = stepsFor(ship).map((step, index) => `<button class="journey-node${step.done ? ' done' : ''}${step.active ? ' active' : ''}" data-step="${index}"><span class="node-dot">${icon(step.active ? 'ship' : step.done ? 'check' : 'clock')}</span><strong>${step.title}</strong><time>${step.time}</time></button>`).join('');
}
function updateSearch() {
  const query = $('search').value.trim().toLowerCase();
  let matches = 0;
  document.querySelectorAll('.vessel').forEach(el => {
    const ship = shipById(el.dataset.id);
    const matched = `${ship.name} ${ship.call}`.toLowerCase().includes(query);
    if (matched) matches += 1;
    el.classList.toggle('search-dim', query.length > 0 && !matched);
  });
  $('search-count').textContent = query ? `${matches}艘` : '';
}
function renderPositions() {
  const order = previewOrder || area().order;
  document.querySelectorAll('.vessel').forEach(el => {
    const index = order.indexOf(el.dataset.id);
    if (!drag || el.dataset.id !== drag.id) el.style.setProperty('--slot', index);
    el.querySelector('.rank').textContent = index+1;
  });
}

// 拖动只改变预览顺序，松手后确认。Escape、取消、原位放回均恢复已保存的顺序。
function beginDrag(event) {
  const handle = event.target.closest('[data-drag]');
  if (!handle || area().order.length < 2 || (event.pointerType === 'mouse' && event.button !== 0)) return;
  selectedId = handle.dataset.drag;
  updateSelection();
  const element = handle.closest('.vessel');
  drag = { id:selectedId, pointer:event.pointerId, handle, element, startX:event.clientX, startY:event.clientY, clientX:event.clientX, clientY:event.clientY, startScroll:$('sea').scrollLeft, frame:0, lastFrame:0, moved:false, initialIndex:area().order.indexOf(selectedId) };
  previewOrder = [...area().order];
  handle.setPointerCapture(event.pointerId);
  event.preventDefault();
}
function moveDrag(event) {
  if (!drag || event.pointerId !== drag.pointer) return;
  const dx = event.clientX-drag.startX;
  const dy = event.clientY-drag.startY;
  if (!drag.moved && Math.hypot(dx,dy) < 6) return;
  drag.moved = true;
  drag.clientX = event.clientX;
  drag.clientY = event.clientY;
  drag.element.classList.add('is-dragging');
  positionDraggedShip();
  if (!drag.frame) drag.frame = requestAnimationFrame(scrollDragEdge);
}
function positionDraggedShip() {
  // 补偿画布滚动量，让抓取的船始终跟随手指，落点由滚动后的实际坐标计算。
  const dx = drag.clientX-drag.startX + $('sea').scrollLeft-drag.startScroll;
  const dy = drag.clientY-drag.startY;
  drag.element.style.transform = `translate(${dx}px,${Math.max(-20,Math.min(22,dy))}px)`;
  const bounds = $('queue-vessels').getBoundingClientRect();
  // 每条船仍占20%的画布，东锚地少船时保留舒适的间隔。
  const target = Math.max(0, Math.min(area().order.length-1, Math.floor((drag.clientX-bounds.left)/(bounds.width*.2))));
  previewOrder = reposition(area().order, drag.id, target);
  renderPositions();
}
function scrollDragEdge(timestamp) {
  if (!drag) return;
  drag.frame = 0;
  const sea = $('sea');
  // 桌面画布裁切装饰水纹，不应因装饰或拖动溢出被程序滚动。
  if (!['auto','scroll'].includes(getComputedStyle(sea).overflowX)) return;
  const bounds = sea.getBoundingClientRect();
  const edge = 48;
  const velocity = drag.clientX < bounds.left+edge
    ? -Math.min(1, (bounds.left+edge-drag.clientX)/edge)
    : drag.clientX > bounds.right-edge ? Math.min(1, (drag.clientX-bounds.right+edge)/edge) : 0;
  const elapsed = drag.lastFrame ? Math.min(32, timestamp-drag.lastFrame) : 16;
  drag.lastFrame = timestamp;
  const before = sea.scrollLeft;
  sea.scrollLeft += velocity * 360 * elapsed/1000;
  positionDraggedShip();
  // 只在边缘且仍能滚动时继续；取消或释放指针会终止帧任务。
  if (velocity && sea.scrollLeft !== before) drag.frame = requestAnimationFrame(scrollDragEdge);
}
function endDrag(event, cancelled = false) {
  if (!drag || (event && event.pointerId !== drag.pointer)) return;
  const completed = drag;
  const target = previewOrder.indexOf(completed.id);
  cancelAnimationFrame(completed.frame);
  drag = null;
  completed.element.classList.remove('is-dragging');
  completed.element.style.transform = '';
  if (completed.handle.hasPointerCapture(completed.pointer)) completed.handle.releasePointerCapture(completed.pointer);
  if (cancelled || (completed.moved && target === completed.initialIndex)) {
    previewOrder = null;
    renderPositions();
    return;
  }
  openReorder(completed.id, target, completed.handle);
}
function showDialog(dialog, trigger) {
  lastTrigger = trigger || document.activeElement;
  dialog.showModal();
}
function openReorder(id, target = area().order.indexOf(id), trigger) {
  if (!area().order.includes(id) || area().order.length < 2) return;
  selectedId = id;
  updateSelection();
  pending = { id, areaKey, original:[...area().order] };
  $('reorder-vessel').textContent = `${shipById(id).name} · ${shipById(id).call}`;
  $('old-position').textContent = `第 ${area().order.indexOf(id)+1} 位`;
  $('new-position').innerHTML = area().order.map((_, index) => `<option value="${index}"${index === target ? ' selected' : ''}>第 ${index+1} 位</option>`).join('');
  $('reason').value = '';
  $('reorder-error').textContent = '';
  updatePreview();
  showDialog($('reorder-dialog'), trigger);
}
function updatePreview() {
  if (!pending) return;
  const target = Number($('new-position').value);
  previewOrder = reposition(pending.original, pending.id, target);
  $('affected-ships').innerHTML = pending.original.filter(id => id !== pending.id && pending.original.indexOf(id) !== previewOrder.indexOf(id)).map(id => `<li><span>${shipById(id).name}</span><span>第 ${pending.original.indexOf(id)+1} 位 → 第 ${previewOrder.indexOf(id)+1} 位</span></li>`).join('');
  $('reorder-form').querySelector('[type=submit]').disabled = pending.original.indexOf(pending.id) === target;
  renderPositions();
}
function closeDialog(dialog) {
  if (dialog === $('reorder-dialog')) { pending = null; previewOrder = null; renderPositions(); }
  dialog.close();
  lastTrigger?.focus({preventScroll:true});
}
function openVisit(stepIndex = 2) {
  const ship = shipById(selectedId);
  if (!ship) return;
  $('visit-body').innerHTML = `<h3 class="visit-title-name">${ship.name}</h3><p class="visit-number">${ship.call} · ${area().name}</p><ol class="timeline-list">${stepsFor(ship).map((step,index) => `<li><button data-timeline="${index}" aria-expanded="${index === stepIndex}"><strong>${step.title}</strong><time>${step.time}</time></button><p class="timeline-detail"${index === stepIndex ? '' : ' hidden'}>${step.detail}</p></li>`).join('')}</ol>`;
  showDialog($('visit-dialog'));
}
function openInformation(title, content) {
  $('handoff-title').textContent = title;
  $('handoff-body').innerHTML = `<div class="handoff-copy">${content}</div>`;
  showDialog($('handoff-dialog'));
}

$('queue-vessels').addEventListener('pointerdown',beginDrag);
$('queue-vessels').addEventListener('pointermove',moveDrag);
$('queue-vessels').addEventListener('pointerup',event => endDrag(event));
$('queue-vessels').addEventListener('pointercancel',event => endDrag(event,true));
$('queue-vessels').addEventListener('lostpointercapture',event => { if (drag) endDrag(event,true); });
$('queue-vessels').addEventListener('click',event => {
  const select = event.target.closest('[data-select]');
  if (select) { selectedId = select.dataset.select; updateSelection(); }
  // detail=0 表示键盘触发，指针已经通过pointerup处理。
  const handle = event.target.closest('[data-drag]');
  if (handle && event.detail === 0) openReorder(handle.dataset.drag,undefined,handle);
});
document.addEventListener('keydown',event => { if (event.key === 'Escape' && drag) { endDrag(null,true); event.preventDefault(); } });
$('position-button').addEventListener('click',() => openReorder(selectedId));
$('new-position').addEventListener('change',updatePreview);
$('reorder-form').addEventListener('submit',event => {
  event.preventDefault();
  if (!pending) return;
  if (!$('reason').value.trim()) { $('reorder-error').textContent = '请填写调整原因。'; $('reason').focus(); return; }
  const oldPosition = pending.original.indexOf(pending.id)+1;
  const newPosition = previewOrder.indexOf(pending.id)+1;
  areas[pending.areaKey].order = [...previewOrder];
  const name = shipById(pending.id).name;
  closeDialog($('reorder-dialog'));
  renderQueue();
  $('feedback').textContent = `${name}已从第${oldPosition}位调整至第${newPosition}位。`;
  document.querySelector(`[data-drag="${selectedId}"]`)?.focus({preventScroll:true});
});
document.querySelectorAll('.close-dialog').forEach(button => button.addEventListener('click',() => closeDialog(button.closest('dialog'))));
document.querySelectorAll('dialog').forEach(dialog => dialog.addEventListener('cancel',event => { event.preventDefault(); closeDialog(dialog); }));
$('search').addEventListener('input',updateSearch);
$('search').addEventListener('keydown',event => {
  if (event.key !== 'Enter' || !event.target.value.trim()) return;
  const match = area().order.find(id => `${shipById(id).name} ${shipById(id).call}`.toLowerCase().includes(event.target.value.trim().toLowerCase()));
  if (match) { selectedId = match; updateSelection(); document.querySelector(`[data-select="${match}"]`)?.focus({preventScroll:true}); }
});
document.querySelectorAll('[data-area]').forEach(button => button.addEventListener('click',() => {
  areaKey = button.dataset.area;
  selectedId = area().order[0] || null;
  $('search').value = '';
  $('feedback').textContent = '';
  renderQueue();
}));
$('cancelled-detail').addEventListener('click',() => { selectedId = 'haichen'; updateSelection(); openVisit(3); });
$('visit-button').addEventListener('click',() => openVisit());
$('journey-expand').addEventListener('click',() => openVisit());
$('journey').addEventListener('click',event => { const step = event.target.closest('[data-step]'); if (step) openVisit(Number(step.dataset.step)); });
$('visit-body').addEventListener('click',event => {
  const button = event.target.closest('[data-timeline]');
  if (!button) return;
  const expanded = button.getAttribute('aria-expanded') === 'true';
  button.setAttribute('aria-expanded',String(!expanded));
  button.nextElementSibling.hidden = expanded;
});
$('plan-button').addEventListener('click',() => {
  const ship = shipById(selectedId);
  openInformation(ship.confirmed ? '已确认泊位安排' : '继续安排泊位',`<p><strong>${ship.name}</strong><br>${ship.call}</p><p>${ship.confirmed ? `本次设计场景已确认 ${ship.confirmed} 靠泊。` : `从锚地带入这次到访，泊位页将保留其他船舶的安排，供调度员选择资源和时间窗。`}</p><p>此处展示4A与后续泊位页的衔接。泊位拖动交互将在5A—5D设计中实现。</p>`);
});
$('help').addEventListener('click',() => openInformation('4A 交互设计','<p>使用固定样例推演锚地办理，观察时刻为10:00。调整只保存在当前页面。</p><p>拖动船舶下方的把手，或选择“调整顺位”。松手后查看受影响的船舶，填写原因并确认。按Esc或取消恢复原顺序。</p><p>可切换锚地、搜索选择船舶、展开到访时间线。船舶图标表达队列顺序。场景照片沿用项目素材。</p>'));
document.querySelectorAll('[data-route]').forEach(button => button.addEventListener('click',() => {
  if (button.dataset.route === 'queue') { $('workspace').scrollIntoView({block:'start'}); return; }
  openInformation(button.textContent.trim(),'<p>当前打开4A锚地交互设计。导航保留整套工作台的位置关系，业务模块按各自阶段接入。</p>');
}));
$('reset').addEventListener('click',() => {
  areas = structuredClone(initialAreas); areaKey = 'south'; selectedId = 'donglan'; pending = null; previewOrder = null;
  $('search').value = ''; $('feedback').textContent = ''; renderQueue();
});
function updateMotionButton() {
  document.body.classList.toggle('no-motion',movementDisabled);
  $('motion').setAttribute('aria-pressed',String(movementDisabled));
  $('motion').setAttribute('aria-label',movementDisabled ? '启用动态反馈' : '暂停动态反馈');
  $('motion').innerHTML = icon(movementDisabled ? 'play' : 'pause');
}
$('motion').addEventListener('click',() => { movementDisabled = !movementDisabled; updateMotionButton(); });
updateMotionButton();
renderQueue();
window.addEventListener('resize', keepSelectedVisible);
