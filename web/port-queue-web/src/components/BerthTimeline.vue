<script setup lang="ts">
// 同一时间比例下分别绘制安排与实际占用；相互重叠的草稿/候选分轨，永不互相遮盖。
import { computed, nextTick, ref, watch } from 'vue'
import type { BerthSchedule } from '../types/berth'
import PortIcon from './PortIcon.vue'
import { berthDay, berthRange, berthTime, revisionLabel } from '../utils/berthPresentation'
import { formatDate } from '../utils/appointmentPresentation'
const props = defineProps<{ schedule: BerthSchedule; selectedId: string }>()
const emit = defineEmits<{ select: [id: string] }>()
const scroll = ref<HTMLElement>(), mode = ref<'chart'|'list'>('chart')
const start = computed(() => Date.parse(props.schedule.from)), end = computed(() => Date.parse(props.schedule.to))
const hours = computed(() => (end.value-start.value)/3600_000)
const ticks = computed(() => Array.from({length:hours.value},(_,i) => {
  const value = new Date(start.value+i*3600_000).toISOString()
  return {offset:i, text:berthTime(value),day:i % 24 === 0 ? berthDay(value) : ''}
}))
function span(from: string, to: string | null) {
  const left = Math.max(start.value,Date.parse(from)), right = Math.min(end.value,to ? Date.parse(to) : end.value)
  return {left:`${(left-start.value)/3600_000*48}px`,width:`${Math.max(4,(right-left)/3600_000*48)}px`}
}
const rows = computed(() => props.schedule.resources.map(resource => {
  const trackEnds: number[] = []
  const plans = props.schedule.revisions.filter(r => r.berthId === resource.berthId).sort((a,b) => Date.parse(a.plannedBerthingAt)-Date.parse(b.plannedBerthingAt)).map(revision => {
    let track = trackEnds.findIndex(t => t <= Date.parse(revision.plannedBerthingAt))
    if (track < 0) track = trackEnds.length
    trackEnds[track] = Date.parse(revision.plannedUnberthingAt)
    return {revision,track}
  })
  const occupancies = props.schedule.occupancies.filter(o => o.berthId === resource.berthId)
  return {resource,plans,occupancies,trackCount:Math.max(1,trackEnds.length)}
}))
const nowOffset = computed(() => (Date.parse(props.schedule.observedAt)-start.value)/3600_000*48)
async function current() { await nextTick(); if (scroll.value) scroll.value.scrollLeft = Math.max(0,nowOffset.value-110) }
watch(() => props.schedule.from, current, {immediate:true})
defineExpose({ current })
</script>
<template>
  <section class="bp-schedule glass-panel" aria-label="泊位资源排期">
    <header class="bp-panel-toolbar"><h3>资源时间轴</h3><div class="bp-segment" role="group" aria-label="排期视图"><button :aria-pressed="mode === 'chart'" @click="mode = 'chart'">时间轴</button><button :aria-pressed="mode === 'list'" @click="mode = 'list'">明细</button></div></header>
    <div class="bp-legend"><span><i class="confirmed"></i>已确认</span><span><i class="executing"></i>执行中</span><span><i class="candidate"></i>草稿 / 候选</span><span><i class="completed"></i>已完成</span><span class="bp-zone">北京时间</span></div>
    <div v-show="mode === 'chart'" ref="scroll" class="bp-axis-scroll" tabindex="0" aria-label="泊位时间轴，可横向滚动">
      <div class="bp-axis" :style="{width:`${142+hours*48}px`}">
        <div class="bp-ruler"><div class="bp-corner">泊位 / 时间</div><div class="bp-ticks"><span v-for="tick in ticks" :key="tick.offset" :class="{midnight:tick.day}" :style="{left:`${tick.offset*48}px`}"><small v-if="tick.day">{{ tick.day }}</small>{{ tick.text }}</span></div></div>
        <div v-for="row in rows" :key="row.resource.berthId" class="bp-resource-row" :style="{minHeight:`max(var(--bp-row-min),calc(${row.trackCount} * var(--bp-track-height) + ${row.occupancies.length*27+22}px))`}">
          <div class="bp-resource-label"><strong><PortIcon name="berth" :size="18"/>{{ row.resource.code }}</strong><span>{{ row.resource.name }}</span><small>{{ row.resource.lengthM }} m · 吃水 {{ row.resource.maxDraftM }} m</small><small v-if="row.resource.statusCode !== 'AVAILABLE'">{{ row.resource.statusCode === 'MAINTENANCE' ? '维护中' : '已停用' }}</small></div>
          <div class="bp-lane" :data-berth-id="row.resource.berthId">
            <span v-if="nowOffset >= 0 && nowOffset < hours*48" class="bp-now" :style="{left:`${nowOffset}px`}"></span>
            <button v-for="{revision:r,track} in row.plans" :key="r.revisionId" class="bp-plan-bar" :class="{candidate:!r.confirmedAt}" :data-status="r.planStatusCode" :data-revision-id="r.revisionId" :style="{...span(r.plannedBerthingAt,r.plannedUnberthingAt),top:`calc(12px + ${track} * var(--bp-track-height))`}" :aria-pressed="selectedId === r.appointmentId" :aria-label="`${r.shipName}，${revisionLabel(r)}，版本${r.versionNo}，${formatDate(r.plannedBerthingAt)}至${formatDate(r.plannedUnberthingAt)}`" :title="`${r.shipName} · ${formatDate(r.plannedBerthingAt)} → ${formatDate(r.plannedUnberthingAt)}`" @click="emit('select',r.appointmentId)">
              <strong><PortIcon name="ship" :size="18"/>{{ r.shipName }}</strong><span>{{ berthRange(r.plannedBerthingAt,r.plannedUnberthingAt) }} · {{ revisionLabel(r) }} · V{{ r.versionNo }}</span>
            </button>
            <div v-for="(o,index) in row.occupancies" :key="o.executedRevisionId" class="bp-occupancy" :class="{open:!o.actualUnberthingAt}" :style="{...span(o.actualBerthingAt,o.actualUnberthingAt),top:`calc(${15+index*27}px + ${row.trackCount} * var(--bp-track-height))`}"><button :title="`${o.shipName} · ${formatDate(o.actualBerthingAt)} → ${o.actualUnberthingAt ? formatDate(o.actualUnberthingAt) : '未离泊'}`" @click="emit('select',o.appointmentId)">{{ o.shipName }} · {{ o.actualUnberthingAt ? '实际占用' : '未离泊' }}</button></div>
            <span v-if="!row.plans.length && !row.occupancies.length" class="bp-no-arrangement">该时段暂无安排</span>
          </div>
        </div>
        <p v-if="!rows.length" class="bp-empty">暂无泊位资料</p>
      </div>
    </div>
    <div v-show="mode === 'list'" class="bp-schedule-list">
      <section v-for="row in rows" :key="row.resource.berthId"><h4>{{ row.resource.code }} · {{ row.resource.name }}</h4><button v-for="{revision:r} in row.plans" :key="r.revisionId" :aria-pressed="selectedId === r.appointmentId" @click="emit('select',r.appointmentId)"><strong>{{ r.shipName }}</strong><span>{{ formatDate(r.plannedBerthingAt) }} → {{ formatDate(r.plannedUnberthingAt) }}</span><span>{{ revisionLabel(r) }} · V{{ r.versionNo }}</span></button><button v-for="o in row.occupancies" :key="o.executedRevisionId" @click="emit('select',o.appointmentId)">{{ o.shipName }} · 实际 {{ formatDate(o.actualBerthingAt) }} → {{ o.actualUnberthingAt ? formatDate(o.actualUnberthingAt) : '未离泊' }}</button><p v-if="!row.plans.length && !row.occupancies.length">该时段暂无安排</p></section>
    </div>
    <footer class="bp-axis-footer"><span>{{ rows.length }} 个泊位 · {{ hours }} 小时</span><button class="bp-quiet" @click="current">定位当前时刻 <PortIcon name="clock" :size="16"/></button></footer>
  </section>
</template>
