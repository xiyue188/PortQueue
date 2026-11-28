<script setup lang="ts">
// 泊位页围绕资源排期与单次到访展开；资源、清单、详情分别加载，不用样例补空数据。
import { computed, nextTick, onScopeDispose, ref, watch } from 'vue'
import type { SessionClient } from '../composables/useSession'
import { useBerths } from '../composables/useBerths'
import BerthTimeline from '../components/BerthTimeline.vue'
import PortIcon from '../components/PortIcon.vue'
import { formatDate } from '../utils/appointmentPresentation'
import { berthTime, planLabel } from '../utils/berthPresentation'
import scene from '../assets/port-scene.png'
import '../styles/berth.css'
const props = defineProps<{active:boolean; accountSession:SessionClient}>()
const emit = defineEmits<{appointment:[id:string]; anchorage:[id:string]; changed:[]}>()
const model = useBerths()
const {schedule,visits,detail,selectedId,date,hours,scope,keyword,page,loading,visitsLoading,detailLoading,error,visitsError,detailError} = model
const input = ref(''), composing = ref(false), historyDialog = ref<HTMLDialogElement>(), timeline = ref<InstanceType<typeof BerthTimeline>>()
let historyTrigger: HTMLElement | null = null
const identity = computed(() => { const user = props.accountSession.session.value?.user; return user ? `${user.userId}:${user.roleCode}` : '' })
const current = computed(() => detail.value?.versions.find(v => v.isCurrent))
const currentResource = computed(() => schedule.value?.resources.find(r => r.berthId === current.value?.berthId))
const latestCandidate = computed(() => {
  const latest = detail.value?.versions.slice().sort((a,b) => b.versionNo-a.versionNo)[0]
  return latest && !latest.confirmedAt && latest.versionNo > (current.value?.versionNo ?? 0) ? latest : undefined
})
const pageCount = computed(() => Math.max(1,Math.ceil((visits.value?.totalCount ?? 0)/8)))
const eventName = (code:string) => ({ANCHORAGE_ENTER:'实际入锚',ANCHORAGE_LEAVE:'实际离锚',PORT_ENTER:'实际进港',BERTHING:'实际靠泊',UNBERTHING:'实际离泊'}[code] ?? code)
const journey = computed(() => {
  const d = detail.value; if (!d) return []
  return [
    {id:'submit',name:'预约提交',time:d.submittedAt,description:d.appointmentNo},
    {id:'review',name:'预约审核',time:d.reviewedAt,description:d.reviewerName ?? ''},
    ...d.versions.filter(v => v.confirmedAt).map(v => ({id:`v${v.revisionId}`,name:`计划确认 · V${v.versionNo}`,time:v.confirmedAt,description:`${formatDate(v.plannedBerthingAt)} → ${formatDate(v.plannedUnberthingAt)}`})),
    ...d.events.map(e => ({id:e.eventId,name:eventName(e.eventTypeCode),time:e.eventTime,description:e.recordedByName ?? ''})),
  ].filter(e => e.time).sort((a,b) => Date.parse(a.time!)-Date.parse(b.time!))
})
async function refresh() { if (identity.value) await model.refresh() }
function invalidate() { model.invalidate(); if (props.active && identity.value) void model.refresh(false) }
async function locateAppointment(id:string) {
  if (!identity.value) return
  if (selectedId.value !== id || !detail.value) await model.select(id)
}
async function changeDay(offset:number) { const value = new Date(`${date.value}T00:00:00+08:00`); value.setTime(value.getTime()+offset*86400_000); date.value = new Date(value.getTime()+8*3600_000).toISOString().slice(0,10); await model.loadSchedule() }
async function today() { date.value = new Date(Date.now()+8*3600_000).toISOString().slice(0,10); await model.loadSchedule(); await timeline.value?.current() }
function search() { if (composing.value) return; keyword.value = input.value.trim(); page.value = 1; void model.loadVisits() }
function changeScope(next:'waiting'|'all') { scope.value = next; page.value = 1; void model.loadVisits() }
function changePage(offset:number) { page.value += offset; void model.loadVisits() }
async function showHistory() { historyTrigger = document.activeElement as HTMLElement; historyDialog.value?.showModal(); await nextTick() }
function closeHistory() { historyDialog.value?.close(); historyTrigger?.focus({preventScroll:true}) }
watch(identity,() => {model.clear(); closeHistory(); input.value = ''; keyword.value = ''; page.value = 1; if (identity.value && props.active) void model.refresh()})
watch(() => props.active, active => { if (!active) {model.suspend();closeHistory()} else if (identity.value) void model.refresh(false) },{immediate:true})
onScopeDispose(closeHistory)
defineExpose({refresh,locateAppointment,invalidate})
</script>
<template>
  <section class="berth-workspace" :hidden="!active" aria-label="泊位计划工作区">
    <div v-if="!identity" class="bp-empty glass-panel"><PortIcon name="berth" :size="32"/><h2>泊位计划</h2><p>请在账号区登录后继续。</p></div>
    <div v-else class="bp-content">
      <header class="bp-heading"><h2>泊位排期</h2><div class="bp-date-controls"><button class="icon-button" aria-label="前一天" :disabled="loading" @click="changeDay(-1)"><PortIcon name="left"/></button><input v-model="date" type="date" aria-label="排期日期" required @change="model.loadSchedule"/><button class="icon-button" aria-label="后一天" :disabled="loading" @click="changeDay(1)"><PortIcon name="right"/></button><button class="bp-quiet" @click="today">今天</button></div><div class="bp-segment" role="group" aria-label="时间范围"><button v-for="h in [24,48]" :key="h" :aria-pressed="hours === h" @click="hours = h; model.loadSchedule()">{{ h }}小时</button></div></header>
      <div class="bp-work-grid">
        <div class="bp-chart-host" :aria-busy="loading" :inert="loading || undefined">
          <BerthTimeline v-if="schedule" ref="timeline" :schedule="schedule" :selected-id="selectedId" @select="model.select"/>
          <div v-else class="bp-empty glass-panel"><p v-if="error" role="alert">{{ error }}</p><p v-else>正在读取排期…</p><button v-if="error" class="button" @click="model.loadSchedule">重新读取</button></div>
        </div>
        <aside class="bp-detail glass-panel" :aria-busy="detailLoading">
          <template v-if="detail">
            <div class="bp-detail-scene" :style="{backgroundImage:`url(${scene})`}"></div>
            <div class="bp-detail-body" :inert="detailLoading || undefined">
              <header><div class="bp-identity"><h2>{{ detail.shipName }}</h2><span class="bp-state" :class="detail.planStatusCode?.toLowerCase()">{{ planLabel(detail.planStatusCode) }}</span></div><p class="bp-call">{{ detail.callNo }}</p></header>
              <div v-if="current" class="bp-arrangement"><span class="bp-berth-line"><PortIcon name="berth" :size="19"/>{{ currentResource?.name ?? `泊位 ${current.berthId}` }} <small>V{{ current.versionNo }}</small></span><div><small>计划靠泊</small><strong>{{ berthTime(current.plannedBerthingAt) }}</strong><span>{{ formatDate(current.plannedBerthingAt).split(' ')[0] }}</span></div><div><small>计划离泊</small><strong>{{ berthTime(current.plannedUnberthingAt) }}</strong><span>{{ formatDate(current.plannedUnberthingAt).split(' ')[0] }}</span></div></div>
              <div v-else class="bp-no-plan"><PortIcon name="berth" :size="24"/><strong>尚未编制泊位计划</strong></div>
              <p v-if="latestCandidate && ['CONFIRMED','EXECUTING'].includes(detail.planStatusCode ?? '')" class="bp-candidate-note">V{{ latestCandidate.versionNo }} 候选待确认</p>
              <dl class="bp-facts"><div><dt>船型</dt><dd>{{ detail.shipTypeName }}</dd></div><div><dt>船长 / 吃水</dt><dd>{{ detail.lengthM }} / {{ detail.declaredDraftM }} m</dd></div><div><dt>预计作业</dt><dd>{{ detail.estimatedOperationHours }} 小时</dd></div><div><dt>申请到港</dt><dd>{{ formatDate(detail.requestedArrivalAt) }}</dd></div></dl>
              <p v-if="detail.anchorageName" class="bp-source"><PortIcon name="anchor" :size="17"/>{{ detail.anchorageName }} · {{ detail.leftAt ? '已离锚' : '在锚' }}</p>
              <details v-if="detail.manualEstimatedBerthingAt" class="bp-reference"><summary>人工预计靠泊</summary><p>{{ formatDate(detail.manualEstimatedBerthingAt) }}</p></details>
              <button class="bp-history-link" @click="showHistory">本次到访与版本记录 <span>{{ detail.versions.length }} 个版本</span><PortIcon name="arrow" :size="17"/></button>
            </div>
            <footer class="bp-detail-actions"><button class="button" @click="emit('appointment',detail.appointmentId)">查看进港预约 <PortIcon name="arrow" :size="16"/></button><button v-if="detail.queueEntryId" class="bp-quiet" @click="emit('anchorage',detail.appointmentId)">查看锚地停留 <PortIcon name="anchor" :size="16"/></button></footer>
          </template>
          <div v-else class="bp-empty"><PortIcon name="ship" :size="34"/><p v-if="detailError" role="alert">{{ detailError }}</p><p v-else>{{ detailLoading ? '正在读取到访…' : '选择时间条或到访' }}</p><button v-if="detailError && selectedId" class="button" @click="model.select(selectedId)">重新读取</button></div>
        </aside>
      </div>
      <section class="bp-waiting glass-panel" :aria-busy="visitsLoading" aria-label="到访清单">
        <header><div class="bp-segment" role="group" aria-label="到访范围"><button :aria-pressed="scope === 'waiting'" @click="changeScope('waiting')">待安排到访</button><button :aria-pressed="scope === 'all'" @click="changeScope('all')">全部到访</button></div><strong class="bp-count">{{ visits?.totalCount ?? '—' }} 艘次</strong><form class="bp-search" @submit.prevent="search"><PortIcon name="search" :size="17"/><input v-model="input" type="search" aria-label="查找泊位到访" placeholder="船名 / 到访编号" @compositionstart="composing = true" @compositionend="composing = false"/><button type="submit">查询</button></form><div class="bp-pagination"><button class="icon-button" aria-label="上一页到访" :disabled="page <= 1 || visitsLoading" @click="changePage(-1)"><PortIcon name="left" :size="16"/></button><span>{{ page }} / {{ pageCount }}</span><button class="icon-button" aria-label="下一页到访" :disabled="page >= pageCount || visitsLoading" @click="changePage(1)"><PortIcon name="right" :size="16"/></button></div></header>
        <div v-if="keyword" class="bp-query">搜索：{{ keyword }}<button class="bp-quiet" @click="input = ''; search()">清除</button></div>
        <p v-if="visitsError" class="bp-list-error" role="alert">{{ visitsError }} <button class="bp-quiet" @click="model.loadVisits">重新读取</button></p>
        <div v-else class="bp-visits" :inert="visitsLoading || undefined"><button v-for="visit in visits?.items" :key="visit.appointmentId" :aria-pressed="selectedId === visit.appointmentId" @click="model.select(visit.appointmentId)"><span class="bp-ship-icon"><PortIcon name="ship" :size="26"/></span><span><strong>{{ visit.shipName }}</strong><small>{{ visit.callNo }} · {{ planLabel(visit.planStatusCode) }}</small></span></button><p v-if="visits && !visits.items.length" class="bp-list-empty">{{ keyword ? '未找到符合条件的到访' : '暂无符合条件的到访' }}</p></div>
      </section>
    </div>
    <dialog ref="historyDialog" class="bp-dialog" aria-labelledby="bp-history-title" @cancel.prevent="closeHistory"><header><h2 id="bp-history-title">{{ detail?.shipName }} · 本次到访</h2><button class="icon-button" aria-label="关闭到访记录" @click="closeHistory"><PortIcon name="close"/></button></header><div class="bp-journey"><details v-for="entry in journey" :key="entry.id"><summary><strong>{{ entry.name }}</strong><time>{{ formatDate(entry.time) }}</time></summary><p>{{ entry.description }}</p></details></div><h3>计划版本</h3><details v-for="version in detail?.versions" :key="version.revisionId" class="bp-version"><summary>V{{ version.versionNo }} · {{ version.isCurrent ? '当前版本' : '历史版本' }} · {{ version.confirmedAt ? '已确认' : '未确认' }}</summary><dl><dt>安排</dt><dd>{{ formatDate(version.plannedBerthingAt) }} → {{ formatDate(version.plannedUnberthingAt) }}</dd><dt>编制</dt><dd>{{ version.revisedByName }} · {{ formatDate(version.revisedAt) }}</dd><dt>原因</dt><dd>{{ version.revisionReason }}</dd><template v-if="version.confirmedAt"><dt>确认</dt><dd>{{ version.confirmedByName }} · {{ formatDate(version.confirmedAt) }}</dd></template></dl></details><p v-if="!detail?.versions.length" class="bp-list-empty">尚无计划版本</p></dialog>
  </section>
</template>
