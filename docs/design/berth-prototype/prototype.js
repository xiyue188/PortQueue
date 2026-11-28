/* 5A交互层：所有变化只存页面内存。正式安排与候选分开，表单和拖动复用同一预览校验。
   不模拟登录、不发业务请求；固定时钟用于复现跨日、实际超时及冲突场景。 */
'use strict';
(() => {
    const data = structuredClone(window.berthScenario);
    const $ = id => document.getElementById(id);
    const esc = value => String(value ?? '').replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[char]));
    const paths = {
        brand: 'M3 8c4-5 7-5 11-1s6 4 9-1M3 14c4-5 7-5 11-1s6 4 9-1M3 20c4-5 7-5 11-1s6 4 9-1',
        home: 'M3 10 12 3l9 7M5 9v12h5v-7h4v7h5V9', ship: 'M3 16 12 12l9 4-3 5H6zM6 14V9h12v5M9 9V5h6v4M12 5V2',
        anchor: 'M12 8v13M8 11h8M3 14v3c0 2 4 4 9 4s9-2 9-4v-3M1 16l2-2 2 2m14 0 2-2 2 2M12 8a3 3 0 1 0 0-6 3 3 0 0 0 0 6',
        berth: 'M3 21V9l9-6 9 6v12M3 9h18M8 21V11m8 10V11M2 21h20', document: 'M6 3h8l5 5v13H6zM14 3v6h5M9 13h7m-7 4h7',
        chart: 'M4 21v-7h3v7zm6 0V8h3v13zm6 0V3h3v18z', search: 'M16 16l5 5M18 10a8 8 0 1 0-16 0 8 8 0 0 0 16 0',
        arrow: 'M4 12h16m-6-6 6 6-6 6', left: 'm14 6-6 6 6 6', right: 'm10 6 6 6-6 6', close: 'M6 6 18 18M18 6 6 18',
        clock: 'M12 7v6l4 2M22 12a10 10 0 1 0-20 0 10 10 0 0 0 20 0', info: 'M12 11v6m0-10v.1M22 12a10 10 0 1 0-20 0 10 10 0 0 0 20 0',
        edit: 'm14 4 6 6M4 20l5-1L21 7l-4-4L5 15z', menu: 'M4 6h16M4 12h16M4 18h16', refresh: 'M20 7v5h-5M4 17v-5h5M5 7a8 8 0 0 1 13-2l2 3M4 16l2 3a8 8 0 0 0 13-2',
        grip: 'M9 5v.1M15 5v.1M9 12v.1M15 12v.1M9 19v.1M15 19v.1', check: 'm5 12 4 4L19 6',
    };
    const icon = name => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="${paths[name] || paths.info}"/></svg>`;
    document.querySelectorAll('[data-icon]').forEach(el => el.innerHTML = icon(el.dataset.icon));
    const hour = 3600000, now = Date.parse(data.observedAt);
    const local = time => new Date(Number(time) + 8 * hour).toISOString().slice(0, 16);
    const parse = value => Date.parse(`${value}:00+08:00`);
    const hm = time => local(time).slice(11, 16);
    const dateText = time => local(time).slice(5, 10).replace('-', '/');
    const full = time => `${dateText(time)} ${hm(time)}`;
    const typeName = type => ({ CONTAINER: '集装箱船', BULK: '散货船', GENERAL: '杂货船' }[type]);
    const statusName = status => ({ CONFIRMED: '已确认', EXECUTING: '执行中', COMPLETED: '已完成', CANDIDATE: '候选', WAITING: '待安排' }[status]);
    data.plans.forEach(p => { p.start = Date.parse(p.start); p.end = Date.parse(p.end); });
    const visit = id => data.visits.find(v => v.id === id);
    const berth = id => data.berths.find(b => b.id === id);
    const current = id => data.plans.find(p => p.visit === id);
    let selected = new URLSearchParams(location.search).get('visit') || 'xingzhou';
    if (!visit(selected))
        selected = 'xingzhou';
    const candidates = new Map();
    let hours = 24, view = 'chart', editing = null, drag = null, suppressedClick = false, focusBeforeDialog = null, feedbackTimer;
    const start = () => parse(`${$('date').value}T00:00`);
    const finish = () => start() + hours * hour;
    const px = () => parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--hour-width'));
    const offset = time => (time - start()) / hour * px();
    const overlaps = (a, b) => a.start < b.end && a.end > b.start;
    const shown = plan => plan.start < finish() && plan.end > start();
    // 未离泊是一条独立事实；不能随原计划时窗移出屏幕而消失。
    const openOccupancy = plan => plan.status === 'EXECUTING' && !plan.actualEnd
        && Date.parse(plan.actualStart) < finish() && finish() > now;
    const occupancyRisk = plan => plan?.status === 'CONFIRMED' && data.plans.some(other =>
        other.berth === plan.berth && other.visit !== plan.visit && other.status === 'EXECUTING' && !other.actualEnd);
    function notify(message) { clearTimeout(feedbackTimer); $('feedback').textContent = message; $('feedback').hidden = false; feedbackTimer = setTimeout(() => $('feedback').hidden = true, 4500); }
    function openDialog(id) { focusBeforeDialog = document.activeElement; $(id).showModal(); }
    function closeDialog(dialog) { dialog.close(); editing = null; if (focusBeforeDialog?.isConnected)
        focusBeforeDialog.focus({ preventScroll: true }); }
    document.querySelectorAll('[data-close]').forEach(button => button.addEventListener('click', () => closeDialog(button.closest('dialog'))));
    document.querySelectorAll('dialog').forEach(dialog => dialog.addEventListener('cancel', event => { event.preventDefault(); closeDialog(dialog); }));
    // 校验输出具体原因；正式接入时必须由服务端和SQL再次检查，不能信任本原型判断。
    function issues(plan) {
        const v = visit(plan.visit), b = berth(plan.berth), errors = [];
        if (!v || !b)
            return ['请选择有效的到访和泊位。'];
        if (!Number.isFinite(plan.start) || !Number.isFinite(plan.end))
            return ['请填写完整的靠泊与离泊时间。'];
        if (plan.end <= plan.start)
            errors.push('计划离泊必须晚于计划靠泊。');
        if (plan.start < now)
            errors.push(`新安排不得早于${full(now)}。`);
        if (!b.types.includes(v.type))
            errors.push(`${b.code}支持${b.types.map(typeName).join('、')}，不支持${typeName(v.type)}。`);
        if (v.length > b.length)
            errors.push(`船长${v.length}米，超过${b.code}可用长度${b.length}米。`);
        if (v.draft > b.draft)
            errors.push(`申报吃水${v.draft}米，超过${b.code}上限${b.draft}米。`);
        if (v.dwt > b.dwt)
            errors.push(`载重吨${v.dwt.toLocaleString()}吨，超过${b.code}上限${b.dwt.toLocaleString()}吨。`);
        for (const p of data.plans.filter(p => p.berth === b.id && p.visit !== v.id)) {
            if (p.status === 'EXECUTING' && !p.actualEnd && plan.end > now) {
                errors.push(`${visit(p.visit).name}尚未离泊，实际占用结束时间待定；不能仅按原计划${hm(p.end)}释放。`);
            }
            else if (p.status !== 'COMPLETED' && overlaps(plan, p))
                errors.push(`与${visit(p.visit).name}的${full(p.start)}—${full(p.end)}安排重叠。`);
        }
        return errors;
    }
    function proposalFor(id) {
        const saved = candidates.get(id) || current(id);
        if (saved)
            return { ...saved };
        const v = visit(id), base = v.estimate ? Math.max(now, Date.parse(v.estimate)) : now;
        return { visit: id, berth: 'b2', start: Math.ceil(base / (15 * 60000)) * 15 * 60000, end: Math.ceil(base / (15 * 60000)) * 15 * 60000 + v.hours * hour, status: 'CANDIDATE' };
    }
    // 查找只使用已确认/实际占用；候选不会把其他人的正式窗口占住。
    function findSlot(id) {
        const v = visit(id), earliest = Math.max(now, start());
        for (let at = Math.ceil(earliest / (15 * 60000)) * 15 * 60000; at + v.hours * hour <= finish(); at += 15 * 60000) {
            for (const b of data.berths) {
                const p = { visit: id, berth: b.id, start: at, end: at + v.hours * hour, status: 'CANDIDATE' };
                if (!issues(p).length)
                    return p;
            }
        }
        return null;
    }
    function freeSegments(b) {
        const blocks = data.plans.filter(p => p.berth === b.id && p.status !== 'COMPLETED').map(p => ({ start: Math.max(p.start, start(), now), end: p.status === 'EXECUTING' && !p.actualEnd ? finish() : Math.min(p.end, finish()) })).filter(p => p.end > p.start).sort((a, b) => a.start - b.start);
        let cursor = Math.max(start(), now);
        const segments = [];
        for (const block of blocks) {
            if (block.start > cursor)
                segments.push({ start: cursor, end: block.start });
            cursor = Math.max(cursor, block.end);
        }
        if (cursor < finish())
            segments.push({ start: cursor, end: finish() });
        return segments;
    }
    function barHtml(p, kind = 'current', track = 0) {
        if (!shown(p))
            return '';
        const v = visit(p.visit), movable = p.status === 'CONFIRMED' || kind === 'candidate';
        const from = Math.max(start(), p.start), to = Math.min(finish(), p.end), duration = (to - from) / hour * px();
        const label = `${v.name}，${statusName(kind === 'candidate' ? 'CANDIDATE' : p.status)}，${full(p.start)}至${full(p.end)}`;
        return `<button class="plan-bar ${kind === 'candidate' ? 'candidate' : ''} ${candidates.has(p.visit) && kind === 'current' ? 'has-candidate' : ''} ${p.start < start() ? 'continued-start' : ''} ${p.end > finish() ? 'continued-end' : ''}" data-visit="${v.id}" data-kind="${kind}" data-status="${p.status}" data-draggable="${movable}" style="left:${offset(from)}px;width:${duration}px;--candidate-track:${track}" aria-label="${label}" title="${label}" aria-pressed="${selected === v.id}"><strong>${icon(movable ? 'grip' : 'ship')}${v.name}</strong><span class="bar-caption">${hm(p.start)}—${dateText(p.end) !== dateText(p.start) ? '次日 ' : ''}${hm(p.end)} <span>${kind === 'candidate' ? '候选' : statusName(p.status)}</span></span></button>`;
    }
    function renderAxis() {
        const scroll = $('axis-scroll').scrollLeft;
        $('axis').style.setProperty('--hours', hours);
        $('ruler').innerHTML = `<div class="corner">泊位 / 时刻</div><div class="ticks">${Array.from({ length: hours / 2 }, (_, i) => { const time = start() + i * 2 * hour; return `<span class="tick ${hm(time) === '00:00' ? 'midnight' : ''}" style="left:${i * 2 * px()}px">${hm(time) === '00:00' ? dateText(time) + ' ' : ''}${hm(time)}</span>`; }).join('')}</div>`;
        $('resource-rows').innerHTML = data.berths.map(b => {
            const plans = data.plans.filter(p => p.berth === b.id && shown(p));
            const pending = [...candidates.values()].filter(p => p.berth === b.id && shown(p));
            const occupied = data.plans.find(p => p.berth === b.id && openOccupancy(p));
            const uncertainStart = Math.max(start(), now);
            return `<section class="resource-row" data-berth="${b.id}" style="--candidate-count:${pending.length}" aria-label="${b.name}"><div class="resource-label"><strong>${icon('berth')}${b.code}</strong><span>${b.name}</span><small>${occupied ? '尚未离泊 · 结束待定' : `${b.length}m · 吃水≤${b.draft}m`}</small></div><div class="lane ${!plans.length && !pending.length ? 'empty' : ''}" data-lane="${b.id}">
        ${freeSegments(b).map(s => `<button class="free-slot" data-free-berth="${b.id}" data-free-start="${s.start}" data-free-end="${s.end}" style="left:${offset(s.start)}px;width:${(s.end - s.start) / hour * px()}px" aria-label="${b.code} ${full(s.start)}至${full(s.end)}空闲，预览安排"><span>${s.end - s.start >= 3 * hour ? '空闲时段' : ''}</span></button>`).join('')}
        ${plans.map(p => barHtml(p)).join('')}${pending.map((p, index) => barHtml(p, 'candidate', index)).join('')}
        ${occupied ? `<div class="occupancy-pending" style="left:${offset(uncertainStart)}px;width:${(finish()-uncertainStart)/hour*px()}px"><button data-select="${occupied.visit}" title="实际离泊未登记，后续安排需核查">${visit(occupied.visit).name} · 未释放</button></div>` : ''}
        ${now >= start() && now < finish() ? `<span class="now-line" style="left:${offset(now)}px" title="参照时刻 ${hm(now)}"></span>` : ''}
      </div></section>`;
        }).join('');
        $('axis-scroll').scrollLeft = scroll;
        $('range-label').textContent = `${full(start())} — ${full(finish())}`;
        renderList();
    }
    function renderList() {
        $('schedule-list').innerHTML = data.berths.map(b => {
            const rows = [...data.plans, ...candidates.values()].filter(p => p.berth === b.id && (shown(p) || openOccupancy(p))).sort((a, b) => a.start - b.start);
            return `<section class="list-resource"><h4>${b.code} · ${b.name}</h4>${rows.length ? rows.map(p => `<button data-select="${p.visit}" aria-pressed="${selected === p.visit}"><span><strong>${visit(p.visit).name}</strong><small>${openOccupancy(p) ? `实际靠泊 ${full(Date.parse(p.actualStart))} · 尚未离泊，结束待定` : `${full(p.start)}—${full(p.end)}`}</small></span><span>${statusName(p.status)}</span></button>`).join('') : '<p>该时段没有安排</p>'}</section>`;
        }).join('');
    }
    function renderDetail() {
        const v = visit(selected), plan = current(selected), draft = candidates.get(selected);
        const status = plan?.status || (draft ? 'CANDIDATE' : 'WAITING');
        const time = plan ? `<div class="arrangement"><div class="berth-line">${icon('berth')}${berth(plan.berth).code} · ${berth(plan.berth).name}</div><div><small>计划靠泊</small><strong>${hm(plan.start)}</strong><span>${dateText(plan.start)}</span></div><div><small>计划离泊</small><strong>${hm(plan.end)}</strong><span>${dateText(plan.end)}</span></div></div>` :
            `<div class="no-plan">${icon('clock')}<span><strong>${draft ? '已有候选安排' : '尚未安排泊位'}</strong><small>${v.estimate ? `人工预计 ${full(Date.parse(v.estimate))}` : '人工预计未填写'}</small></span></div>`;
        $('detail').innerHTML = `<div class="detail-scene" role="img" aria-label="港口集装箱作业场景"></div><div class="detail-body"><header><div class="identity"><h2 id="detail-title">${v.name}</h2><span class="state ${status.toLowerCase()}">${statusName(status)}</span></div><p class="call-number">${v.call}</p></header>${time}
      ${draft ? `<div class="notice candidate-note">候选：${berth(draft.berth).code} · ${full(draft.start)}—${full(draft.end)}${plan ? '<br>原确认安排仍有效' : ''}</div>` : ''}
      ${plan?.status === 'EXECUTING' && !plan.actualEnd ? '<p class="notice">实际尚未离泊，结束时间待定。</p>' : ''}
      ${occupancyRisk(plan) ? '<p class="notice">前船尚未离泊，当前安排需核查。</p>' : ''}
      <dl class="facts"><div><dt>船型</dt><dd>${typeName(v.type)}</dd></div><div><dt>预计作业</dt><dd>${v.hours} 小时</dd></div><div><dt>船长 / 申报吃水</dt><dd>${v.length} / ${v.draft} 米</dd></div><div><dt>载重吨</dt><dd>${v.dwt.toLocaleString()} 吨</dd></div></dl>
      <div class="source-row">${icon(v.entered ? 'anchor' : 'ship')}${v.source}${plan ? `<span> · 确认版本 v${plan.version}</span>` : ''}</div>
      ${plan && v.estimate ? `<details class="reference"><summary>候泊参考</summary><p>人工预计 ${full(Date.parse(v.estimate))}。当前安排以确认计划为准。</p></details>` : ''}
      </div><div class="detail-actions">${!plan || plan.status === 'CONFIRMED' ? `<button class="button primary" id="edit-arrangement">${icon('edit')}${draft ? '编辑候选' : plan ? '调整安排' : '安排泊位'}</button>${draft ? '<button class="quiet" id="discard-candidate">撤回候选预览</button>' : ''}` : ''}<button class="quiet visit-link" id="visit">本次到访 ${icon('arrow')}</button></div>`;
        $('compact-task').innerHTML = `<strong>${v.name}</strong>${!plan || plan.status === 'CONFIRMED' ? `<button class="button primary" id="quick-edit">${draft ? '编辑候选' : plan ? '调整安排' : '安排泊位'}</button>` : `<span class="state">${statusName(status)}</span>`}<button class="quiet" id="quick-visit">到访</button>${draft ? '<button class="quiet" id="quick-discard">撤回候选</button>' : ''}`;
        $('quick-edit')?.addEventListener('click', () => openEdit(proposalFor(selected)));
        $('quick-visit').addEventListener('click', () => openVisit(selected));
        $('quick-discard')?.addEventListener('click', () => { candidates.delete(selected); render(); notify('候选已撤回。'); });
        $('edit-arrangement')?.addEventListener('click', () => openEdit(proposalFor(selected)));
        $('discard-candidate')?.addEventListener('click', () => { candidates.delete(selected); render(); notify('候选已撤回，原确认安排保持不变。'); });
        $('visit').addEventListener('click', () => openVisit(selected));
    }
    function renderWaiting() {
        const rows = data.visits.filter(v => !current(v.id));
        $('waiting-count').textContent = `${rows.length} 艘`;
        $('waiting-visits').innerHTML = rows.map(v => `<button class="waiting-item" data-select="${v.id}" aria-pressed="${selected === v.id}"><span class="waiting-icon">${icon('ship')}</span><span><strong>${v.name}</strong><small>${v.source} · ${candidates.has(v.id) ? '已有候选' : `${v.hours}小时作业`}</small></span></button>`).join('');
    }
    function render() { renderAxis(); renderDetail(); renderWaiting(); }
    function select(id, locate = false) {
        if (!visit(id))
            return;
        selected = id;
        render();
        if (locate) {
            const p = candidates.get(id) || current(id);
            if (p) {
                $('date').value = local(p.start).slice(0, 10);
                renderAxis();
                $('axis-scroll').scrollLeft = Math.max(0, offset(p.start) - px());
            }
        }
    }
    function openVisit(id) {
        const v = visit(id), p = current(id), d = candidates.get(id);
        const records = [{ name: '预约提交', time: v.submitted, text: v.call }, { name: '审核批准', time: v.reviewed, text: '船舶及申报资料审核通过' },
            ...(v.entered ? [{ name: '实际入锚', time: v.entered, text: v.source }] : []), ...(p ? [{ name: '计划确认', time: p.confirmed, text: `${berth(p.berth).code} · v${p.version}` }] : []),
            ...(p?.actualStart ? [{ name: '实际靠泊', time: p.actualStart, text: berth(p.berth).name }] : []), ...(p?.actualEnd ? [{ name: '实际离泊', time: p.actualEnd, text: '本次作业已完成' }] : [])].sort((a, b) => Date.parse(a.time) - Date.parse(b.time));
        $('visit-title').textContent = `${v.name} · 本次到访`;
        $('visit-content').innerHTML = records.map(r => `<div class="journey-record">${icon('check')}<div><strong>${r.name}</strong><time>${full(Date.parse(r.time))}</time><p>${r.text}</p></div></div>`).join('') + (d ? `<p class="notice candidate-note">候选安排尚未确认：${berth(d.berth).code} ${full(d.start)}—${full(d.end)}</p>` : '');
        openDialog('visit-dialog');
    }
    function readEdit() { return { visit: editing, berth: $('edit-berth').value, start: parse($('edit-start').value), end: parse($('edit-end').value), status: 'CANDIDATE', reason: $('edit-reason').value.trim() }; }
    function validateEdit() {
        if (!editing)
            return;
        const p = readEdit(), errors = issues(p), original = candidates.get(editing) || current(editing);
        if (original && original.berth === p.berth && original.start === p.start && original.end === p.end)
            errors.push('安排未变化，无需生成候选。');
        $('validation').innerHTML = errors.length ? `<ul>${errors.map(e => `<li>${esc(e)}</li>`).join('')}</ul>` : `时段检查通过 · ${(p.end - p.start) / hour}小时${p.end - p.start < visit(editing).hours * hour ? '（短于申报作业时长，请核对）' : ''}`;
        $('apply').disabled = errors.length > 0;
        return errors;
    }
    function openEdit(p) {
        if (['EXECUTING', 'COMPLETED'].includes(current(p.visit)?.status)) {
            notify('该计划只读；实际作业不能通过移动时间条改写。');
            return;
        }
        editing = p.visit;
        select(p.visit);
        const original = current(p.visit);
        $('edit-title').textContent = original ? '预览计划调整' : '预览泊位安排';
        $('edit-ship').textContent = visit(p.visit).name;
        $('old-arrangement').textContent = original ? `当前确认：${berth(original.berth).code} · ${full(original.start)}—${full(original.end)}` : '尚无确认计划';
        $('edit-berth').innerHTML = data.berths.map(b => `<option value="${b.id}">${b.code} · ${b.name}</option>`).join('');
        $('edit-berth').value = p.berth;
        $('edit-start').value = local(p.start);
        $('edit-end').value = local(p.end);
        $('edit-reason').value = p.reason || '';
        validateEdit();
        openDialog('edit-dialog');
    }
    $('edit-form').addEventListener('input', validateEdit);
    $('edit-form').addEventListener('submit', event => { event.preventDefault(); if (validateEdit().length)
        return; if (!$('edit-reason').value.trim()) {
        $('edit-reason').setCustomValidity('请填写安排原因。');
        $('edit-reason').reportValidity();
        return;
    } const p = readEdit(); candidates.set(p.visit, p); closeDialog($('edit-dialog')); render(); notify('已应用候选预览；正式确认安排未改变。'); });
    $('edit-reason').addEventListener('input', () => $('edit-reason').setCustomValidity(''));
    // 指针从条身开始；6px区分选择与拖动。旧条保留，候选落点按15分钟吸附。
    function cleanupDrag() {
        if (!drag)
            return;
        cancelAnimationFrame(drag.frame);
        drag.el.classList.remove('dragging');
        drag.ghost?.remove();
        document.querySelectorAll('.drop-valid,.drop-invalid').forEach(el => el.classList.remove('drop-valid', 'drop-invalid'));
        $('drag-feedback').hidden = true;
        drag = null;
    }
    function updateDrag() {
        const d = drag;
        if (!d?.moving)
            return;
        const rect = $('axis-scroll').getBoundingClientRect();
        if (d.x > rect.right - 38)
            $('axis-scroll').scrollLeft += 9;
        else if (d.x < rect.left + parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--label-width')) + 20)
            $('axis-scroll').scrollLeft -= 9;
        const lanes = [...document.querySelectorAll('[data-lane]')];
        const labelWidth = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--label-width'));
        const target = d.x >= rect.left + labelWidth && d.x <= rect.right ? lanes.find(l => { const r = l.getBoundingClientRect(); return d.y >= r.top && d.y <= r.bottom; }) : null;
        if (target) {
            const r = target.getBoundingClientRect(), relative = (d.x - r.left - d.grab) / px() * hour;
            const at = Math.round((start() + relative) / (15 * 60000)) * 15 * 60000;
            d.proposal = { ...d.original, berth: target.dataset.lane, start: at, end: at + (d.original.end - d.original.start) };
            const errors = issues(d.proposal);
            target.append(d.ghost);
            d.ghost.style.left = `${offset(at)}px`;
            d.ghost.style.top = '20px';
            d.ghost.style.width = `${(d.original.end - d.original.start) / hour * px()}px`;
            d.ghost.classList.toggle('invalid', errors.length > 0);
            lanes.forEach(l => l.classList.remove('drop-valid', 'drop-invalid'));
            target.classList.add(errors.length ? 'drop-invalid' : 'drop-valid');
            $('drag-feedback').textContent = `${berth(d.proposal.berth).code} · ${full(at)}—${full(d.proposal.end)}${errors.length ? ' · ' + errors[0] : ''}`;
        }
        else {
            d.proposal = null;
            d.ghost.hidden = true;
            lanes.forEach(l => l.classList.remove('drop-valid', 'drop-invalid'));
            $('drag-feedback').textContent = '移入泊位时段后再松手；当前落点不生成安排。';
        }
        if (target)
            d.ghost.hidden = false;
        d.frame = requestAnimationFrame(updateDrag);
    }
    $('resource-rows').addEventListener('pointerdown', event => {
        const el = event.target.closest('.plan-bar[data-draggable="true"]');
        if (!el || event.button !== 0 || drag)
            return;
        const original = el.dataset.kind === 'candidate' ? candidates.get(el.dataset.visit) : current(el.dataset.visit);
        drag = { el, id: event.pointerId, x: event.clientX, y: event.clientY, fromX: event.clientX, fromY: event.clientY, grab: event.clientX - el.getBoundingClientRect().left, original: { ...original }, moving: false, proposal: null };
        el.setPointerCapture(event.pointerId);
    });
    document.addEventListener('pointermove', event => {
        if (!drag || drag.id !== event.pointerId)
            return;
        drag.x = event.clientX;
        drag.y = event.clientY;
        if (!drag.moving && Math.hypot(drag.x - drag.fromX, drag.y - drag.fromY) > 6) {
            drag.moving = true;
            drag.el.classList.add('dragging');
            drag.ghost = drag.el.cloneNode(true);
            drag.ghost.classList.remove('dragging', 'candidate');
            drag.ghost.classList.add('drag-ghost');
            drag.ghost.removeAttribute('data-visit');
            drag.ghost.setAttribute('aria-hidden', 'true');
            drag.ghost.tabIndex = -1;
            drag.el.parentNode.append(drag.ghost);
            $('drag-feedback').hidden = false;
            updateDrag();
        }
        if (drag.moving)
            event.preventDefault();
    }, { passive: false });
    document.addEventListener('pointerup', event => {
        if (!drag || drag.id !== event.pointerId)
            return;
        const { proposal, moving } = drag;
        cleanupDrag();
        if (moving) {
            suppressedClick = true;
            setTimeout(() => suppressedClick = false, 0);
            if (proposal)
                openEdit(proposal);
        }
    });
    document.addEventListener('pointercancel', cleanupDrag);
    document.addEventListener('keydown', event => { if (event.key === 'Escape' && drag) {
        event.preventDefault();
        cleanupDrag();
        suppressedClick = true;
        setTimeout(() => suppressedClick = false, 150);
    } });
    window.addEventListener('resize', () => { cleanupDrag(); renderAxis(); });
    document.addEventListener('click', event => {
        if (suppressedClick)
            return;
        const selectButton = event.target.closest('[data-select],.plan-bar[data-visit]');
        if (selectButton) {
            select(selectButton.dataset.select || selectButton.dataset.visit);
            $('search-results').hidden = true;
            return;
        }
        const free = event.target.closest('[data-free-berth]');
        if (free) {
            const p = { visit: selected, berth: free.dataset.freeBerth, start: Number(free.dataset.freeStart), end: Number(free.dataset.freeStart) + visit(selected).hours * hour, status: 'CANDIDATE' };
            openEdit(p);
        }
    });
    document.querySelectorAll('[data-hours]').forEach(button => button.addEventListener('click', () => { hours = Number(button.dataset.hours); document.querySelectorAll('[data-hours]').forEach(b => b.setAttribute('aria-pressed', String(b === button))); renderAxis(); }));
    document.querySelectorAll('[data-view]').forEach(button => button.addEventListener('click', () => { view = button.dataset.view; document.querySelectorAll('[data-view]').forEach(b => b.setAttribute('aria-pressed', String(b === button))); $('axis-scroll').hidden = view !== 'chart'; $('schedule-list').hidden = view !== 'list'; }));
    function setDate(value) { if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || !Number.isFinite(parse(value + 'T00:00')))
        value = local(now).slice(0, 10); $('date').value = value; renderAxis(); $('axis-scroll').scrollLeft = 0; }
    $('date').addEventListener('change', () => setDate($('date').value));
    $('previous').addEventListener('click', () => setDate(local(start() - 24 * hour).slice(0, 10)));
    $('next').addEventListener('click', () => setDate(local(start() + 24 * hour).slice(0, 10)));
    $('now').addEventListener('click', () => { setDate(local(now).slice(0, 10)); $('axis-scroll').scrollLeft = Math.max(0, offset(now) - 3 * px()); });
    $('empty-window').addEventListener('click', () => { const p = findSlot(selected); if (p)
        openEdit(p);
    else
        notify('当前区间没有适配且足够长的空闲时段，请调整日期或范围。'); });
    let composing = false;
    function search() { if (composing)
        return; const keyword = $('search').value.trim().toLowerCase(); if (!keyword) {
        $('search-results').hidden = true;
        return;
    } const matches = data.visits.filter(v => `${v.name} ${v.call}`.toLowerCase().includes(keyword)); $('search-results').innerHTML = matches.length ? matches.map(v => `<button data-search-select="${v.id}"><strong>${v.name}</strong><small>${v.call}</small></button>`).join('') : '<p>没有匹配的到访</p>'; $('search-results').hidden = false; }
    $('search').addEventListener('input', search);
    $('search').addEventListener('compositionstart', () => composing = true);
    $('search').addEventListener('compositionend', () => { composing = false; search(); });
    $('search-results').addEventListener('click', event => { const b = event.target.closest('[data-search-select]'); if (b) {
        select(b.dataset.searchSelect, true);
        $('search-results').hidden = true;
    } });
    $('search').addEventListener('keydown', event => { if (composing || event.isComposing)
        return; if (event.key === 'Escape')
        $('search-results').hidden = true; if (event.key === 'ArrowDown') {
        event.preventDefault();
        $('search-results').querySelector('button')?.focus();
    } if (event.key === 'Enter') {
        event.preventDefault();
        const first = $('search-results').querySelector('button');
        if (first) {
            select(first.dataset.searchSelect, true);
            $('search-results').hidden = true;
        }
    } });
    $('search-results').addEventListener('keydown', event => { const buttons = [...$('search-results').querySelectorAll('button')], index = buttons.indexOf(document.activeElement); if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        buttons[(index + (event.key === 'ArrowDown' ? 1 : -1) + buttons.length) % buttons.length]?.focus();
    } if (event.key === 'Escape') {
        $('search-results').hidden = true;
        $('search').focus();
    } });
    document.addEventListener('click', event => { if (!event.target.closest('.search-wrap'))
        $('search-results').hidden = true; });
    $('about').addEventListener('click', () => openDialog('info-dialog'));
    $('reset').addEventListener('click', () => { cleanupDrag(); candidates.clear(); selected = 'xingzhou'; $('date').value = local(now).slice(0, 10); $('search').value = ''; $('search-results').hidden = true; render(); notify('已还原初始预览。'); });
    $('menu').addEventListener('click', () => { const open = $('sidebar').classList.toggle('open'); $('menu').setAttribute('aria-expanded', String(open)); });
    document.querySelectorAll('[data-nav]').forEach(button => button.addEventListener('click', () => { $('sidebar').classList.remove('open'); $('menu').setAttribute('aria-expanded', 'false'); notify(`${button.dataset.nav}的正式页面保持不变；这里仅预览泊位交互。`); }));
    $('all-visits').addEventListener('click', () => { $('visit-title').textContent = '全部到访'; $('visit-content').innerHTML = data.visits.map(v => `<button class="visit-list-button" data-visit-choice="${v.id}"><strong>${v.name}</strong><span>${v.call} · ${statusName(current(v.id)?.status || (candidates.has(v.id) ? 'CANDIDATE' : 'WAITING'))}</span></button>`).join(''); openDialog('visit-dialog'); });
    $('visit-content').addEventListener('click', event => { const b = event.target.closest('[data-visit-choice]'); if (b) {
        closeDialog($('visit-dialog'));
        select(b.dataset.visitChoice, true);
    } });
    render();
    $('axis-scroll').scrollLeft = Math.max(0, Math.floor((now - start()) / hour - 4) * px());
})();
