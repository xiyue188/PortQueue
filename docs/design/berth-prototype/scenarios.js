/* 5A专用合成夹具；设备能力、船舶和时刻仅用于交互验收，不代表真实洋山港。
   固定UTC+8时刻，刷新可复现；正式查询以后接API，不把本文件带入生产数据层。
   图片沿用仓库port-scene素材，来源见web/port-queue-web/src/assets内说明。 */
'use strict';
window.berthScenario = {
    observedAt: '2026-11-28T10:30:00+08:00',
    berths: [
        { id: 'b1', code: 'B-01', name: '集装箱一号泊位', types: ['CONTAINER'], length: 380, draft: 16, dwt: 150000 },
        { id: 'b2', code: 'B-02', name: '综合二号泊位', types: ['CONTAINER', 'GENERAL', 'BULK'], length: 350, draft: 15, dwt: 100000 },
        { id: 'b3', code: 'B-03', name: '散杂货三号泊位', types: ['GENERAL', 'BULK'], length: 240, draft: 12, dwt: 60000 },
    ],
    visits: [
        { id: 'xingzhou', name: '星洲明珠', call: 'YS-20261128-003', type: 'CONTAINER', length: 265, draft: 11.9, dwt: 72000, hours: 8, source: '外锚地1区', entered: '2026-11-28T07:40:00+08:00', estimate: '2026-11-28T16:00:00+08:00', submitted: '2026-11-27T08:20:00+08:00', reviewed: '2026-11-27T10:15:00+08:00' },
        { id: 'donghai', name: '东海晨光', call: 'YS-20261128-002', type: 'BULK', length: 210, draft: 11.6, dwt: 58000, hours: 6, source: '外锚地1区', entered: '2026-11-28T08:15:00+08:00', estimate: null, submitted: '2026-11-27T09:05:00+08:00', reviewed: '2026-11-27T11:40:00+08:00' },
        { id: 'pujiang', name: '浦江清和', call: 'YS-20261128-009', type: 'GENERAL', length: 128, draft: 6.4, dwt: 12500, hours: 4, source: '直接安排', entered: null, estimate: null, submitted: '2026-11-27T13:10:00+08:00', reviewed: '2026-11-27T15:30:00+08:00' },
        { id: 'jianglan', name: '江澜远航', call: 'YS-20261128-001', type: 'CONTAINER', length: 228, draft: 10.8, dwt: 52000, hours: 10, source: '外锚地1区', entered: '2026-11-28T06:10:00+08:00', estimate: '2026-11-28T13:00:00+08:00', submitted: '2026-11-27T06:42:00+08:00', reviewed: '2026-11-27T11:01:00+08:00' },
        { id: 'haiyan', name: '海晏远行', call: 'YS-20261127-018', type: 'CONTAINER', length: 250, draft: 12, dwt: 65000, hours: 4, source: '直接安排', entered: null, estimate: null, submitted: '2026-11-26T07:10:00+08:00', reviewed: '2026-11-26T09:00:00+08:00' },
        { id: 'linGang', name: '临港晓风', call: 'YS-20261128-010', type: 'CONTAINER', length: 155, draft: 7.8, dwt: 21000, hours: 5, source: '内锚地', entered: '2026-11-28T09:20:00+08:00', estimate: null, submitted: '2026-11-27T09:15:00+08:00', reviewed: '2026-11-27T12:35:00+08:00' },
        { id: 'yuanchuan', name: '远川春晖', call: 'YS-20261128-015', type: 'GENERAL', length: 150, draft: 7.5, dwt: 19000, hours: 8, source: '直接安排', entered: null, estimate: null, submitted: '2026-11-27T11:20:00+08:00', reviewed: '2026-11-27T14:10:00+08:00' },
        { id: 'haining', name: '海宁嘉禾', call: 'YS-20261127-012', type: 'CONTAINER', length: 169, draft: 8.4, dwt: 26500, hours: 4, source: '直接安排', entered: null, estimate: null, submitted: '2026-11-26T14:00:00+08:00', reviewed: '2026-11-26T15:00:00+08:00' },
    ],
    plans: [
        { visit: 'haiyan', berth: 'b1', start: '2026-11-28T06:00:00+08:00', end: '2026-11-28T10:00:00+08:00', status: 'EXECUTING', version: 1, confirmed: '2026-11-27T16:30:00+08:00', actualStart: '2026-11-28T06:20:00+08:00', actualEnd: null },
        { visit: 'jianglan', berth: 'b1', start: '2026-11-28T14:00:00+08:00', end: '2026-11-29T00:00:00+08:00', status: 'CONFIRMED', version: 2, confirmed: '2026-11-28T09:10:00+08:00' },
        { visit: 'linGang', berth: 'b2', start: '2026-11-28T11:00:00+08:00', end: '2026-11-28T16:00:00+08:00', status: 'CONFIRMED', version: 1, confirmed: '2026-11-28T08:30:00+08:00' },
        { visit: 'yuanchuan', berth: 'b3', start: '2026-11-28T18:00:00+08:00', end: '2026-11-29T02:00:00+08:00', status: 'CONFIRMED', version: 1, confirmed: '2026-11-28T09:40:00+08:00' },
        { visit: 'haining', berth: 'b2', start: '2026-11-28T01:00:00+08:00', end: '2026-11-28T05:00:00+08:00', status: 'COMPLETED', version: 1, confirmed: '2026-11-27T15:00:00+08:00', actualStart: '2026-11-28T01:10:00+08:00', actualEnd: '2026-11-28T05:25:00+08:00' },
    ],
};
