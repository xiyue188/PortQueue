<script setup lang="ts">
// 两个页面分别缓存查询与选择，共用稳定工作区；页内查询不重建整个页面。
import { computed, nextTick, onMounted, onScopeDispose, ref, watch } from 'vue'
import PortIcon from '../components/PortIcon.vue'
import AppointmentSearch from '../components/AppointmentSearch.vue'
import AppointmentRecords from '../components/AppointmentRecords.vue'
import AppointmentDetail from '../components/AppointmentDetail.vue'
import PortOverview from '../components/PortOverview.vue'
import OperationDistribution from '../components/OperationDistribution.vue'
import BusinessRecords from '../components/BusinessRecords.vue'
import AppointmentRegistration from '../components/AppointmentRegistration.vue'
import SessionAccount from '../components/SessionAccount.vue'
import RecentPortEvents from '../components/RecentPortEvents.vue'
import { useSession } from '../composables/useSession'
import { useOverview } from '../composables/useOverview'
import { useAppointments } from '../composables/useAppointments'
import { useAppointmentSelection } from '../composables/useAppointmentSelection'
import type { AppointmentItem, AppointmentQuery, AppointmentStatus } from '../types/appointment'
import { formatDate, statusClass, statusLabel } from '../utils/appointmentPresentation'
import vesselImage from '../assets/vessel-reference.png'

type View = 'overview' | 'appointments' | 'queue' | 'plans' | 'events'
const viewFromHash = (): View => ['appointments', 'queue', 'plans', 'events'].includes(location.hash.slice(1)) ? location.hash.slice(1) as View : 'overview'
const activeView = ref<View>(viewFromHash())
const mobileMenuOpen = ref(false)
const keyword = ref('')
const selectedId = ref<string>()
const accountSession = useSession()
const saveFeedback = ref('')
const registration = ref<InstanceType<typeof AppointmentRegistration>>()
const businessRefreshKey = ref(0)
const businessViews = ['queue', 'plans', 'events'] as const
const list = useAppointments()
const detail = useAppointmentSelection()
// 审核反馈仅属于原预约；主动改变筛选或选择时不能挂到下一条记录上。
watch(detail.requestedId, (id, previous) => { if (id !== previous) saveFeedback.value = '' })
const { query, rows, totalCount, totalPages, loading, error, load, search } = list
const selected = computed(() => detail.loading.value || detail.error.value ? undefined
  : detail.item.value ?? (error.value ? undefined : rows.value.find(row => row.appointmentId === selectedId.value) ?? rows.value[0]))
const { overview, overviewError, overviewLoading, refreshOverview } = useOverview()
const title = computed(() => ({ overview: '总览', appointments: '进港预约', queue: '锚地排队', plans: '泊位计划', events: '进出港记录' })[activeView.value])
const dateLabel = new Intl.DateTimeFormat('zh-CN', { timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date())
const plannedNavigation = [
  { label: '锚地排队', icon: 'anchor', view: 'queue' as const }, { label: '泊位计划', icon: 'berth', view: 'plans' as const },
  { label: '进出港记录', icon: 'document', view: 'events' as const },
]
// 清单首次加载和明确筛选后，精确读取首项；独立详情/审核结果不被列表刷新覆盖。
watch(rows, records => {
  if (!detail.requestedId.value && records.length) detail.select(records.find(r => r.appointmentId === selectedId.value) ?? records[0]!)
})
async function reviewed(id: string, decision: 'APPROVED' | 'REJECTED') {
  saveFeedback.value = decision === 'APPROVED' ? '预约已批准。' : '预约已驳回。'
  await Promise.all([detail.open(id), load(), refreshOverview()])
  if (detail.error.value || error.value || overviewError.value) saveFeedback.value += '部分信息暂未刷新，可点击刷新重读；无需再次审核。'
  await nextTick(); document.getElementById('appointment-detail')?.focus({ preventScroll: true })
}
async function reconcileReview(id: string) {
  saveFeedback.value = ''
  await Promise.all([detail.open(id), load(), refreshOverview()])
  if (detail.item.value && detail.item.value.appointmentStatusCode !== 'PENDING') saveFeedback.value = '已读取最新审核结果。'
  if (detail.error.value || error.value || overviewError.value) saveFeedback.value = '部分信息暂未刷新，请重试读取后再操作。'
}
async function registered(id: string, status: string) {
  switchView('appointments'); await Promise.all([detail.open(id), load(), refreshOverview()])
  saveFeedback.value = status === 'DRAFT' ? '草稿已保存。' : status === 'PENDING' ? '预约已提交，等待审核。' : '已读取该登记请求的当前结果。'
  if (detail.error.value || error.value || overviewError.value) saveFeedback.value += '部分信息暂未刷新；保存已完成，请重试读取。'
  await nextTick(); document.getElementById('appointment-detail')?.focus({ preventScroll: true })
}
async function reconcileRegistration(id: string) {
  await Promise.all([detail.open(id), load(), refreshOverview()]); saveFeedback.value = detail.error.value ? '暂未读到最新内容，请重试读取。' : '已读取最新内容，请核对预约状态与申报资料。'
}
let listVisited = false
function ensureList() { if (!listVisited) { listVisited = true; void load() } }
function switchView(view: View) {
  if (view !== activeView.value) history.pushState(null, '', `#${view}`)
  activeView.value = view
  mobileMenuOpen.value = false
  if (view === 'appointments') ensureList()
}
function historyChange() { activeView.value = viewFromHash(); mobileMenuOpen.value = false; if (activeView.value === 'appointments') ensureList() }
// 连续操作基于最近已提交的意图合并；尚未提交的输入草稿不影响状态切换。
function submit() { detail.clear(); listVisited = true; switchView('appointments'); void search(keyword.value, (list.pendingQuery.value ?? query.value).status) }
function searchAll() { detail.clear(); listVisited = true; switchView('appointments'); void search(keyword.value, '') }
function openEvent(appointmentId: string) { saveFeedback.value = '';  switchView('appointments'); void detail.open(appointmentId) }
function chooseSuggestion(item: AppointmentItem) {
  saveFeedback.value = ''
  keyword.value = (list.pendingQuery.value ?? query.value).keyword
  selectedId.value = item.appointmentId
  detail.select(item)
  switchView('appointments')
}
function refresh() { businessRefreshKey.value++; detail.clear(); void refreshOverview(); if (listVisited) void load() }
function reset() { detail.clear(); keyword.value = ''; void search('', '') }
function clearKeyword() { detail.clear(); keyword.value = ''; void search('', (list.pendingQuery.value ?? query.value).status) }
function filterStatus(status: AppointmentStatus | '') { detail.clear(); void search((list.pendingQuery.value ?? query.value).keyword, status) }
function loadList(next?: AppointmentQuery) { detail.clear(); void load(next) }
function selectRow(item: AppointmentItem) { saveFeedback.value = ''; if (!loading.value) { detail.select(item); selectedId.value = item.appointmentId } }
// 总览状态是明确查询入口，清除旧关键词，不会被上次的筛选截断。
function openStatus(status: AppointmentStatus | '') {
  detail.clear(); keyword.value = ''; listVisited = true
  switchView('appointments'); void search('', status)
}
onMounted(() => { void accountSession.refresh(); refresh(); if (activeView.value === 'appointments') ensureList(); window.addEventListener('popstate', historyChange) })
onScopeDispose(() => window.removeEventListener('popstate', historyChange))
</script>

<template>
  <a class="skip-link" href="#main-content">跳至主要内容</a>
  <div class="port-workspace" :class="{ 'is-overview': activeView === 'overview' }">
    <aside class="sidebar" :class="{ 'is-open': mobileMenuOpen }">
      <a class="brand" href="#overview" @click.prevent="switchView('overview')" aria-label="PortQueue 总览">
        <svg class="brand-mark" viewBox="0 0 32 32" fill="none" aria-hidden="true"><path d="M5 13c3-7 8-7 12-3s7 5 10-2M5 20c3-7 8-7 12-3s7 5 10-2M5 27c3-7 8-7 12-3s7 5 10-2" stroke="currentColor" stroke-width="3.4" stroke-linecap="round"/></svg>
        <span>PORTQUEUE</span>
      </a>
      <nav aria-label="业务导航" class="side-nav">
        <button :aria-current="activeView === 'overview' ? 'page' : undefined" @click="switchView('overview')"><PortIcon name="home" /><span>总览</span></button>
        <button :aria-current="activeView === 'appointments' ? 'page' : undefined" @click="switchView('appointments')"><PortIcon name="ship" /><span>进港预约</span></button>
        <button v-for="nav in plannedNavigation" :key="nav.label" :aria-current="activeView === nav.view ? 'page' : undefined" @click="switchView(nav.view)"><PortIcon :name="nav.icon" /><span>{{ nav.label }}</span></button>
        <button disabled><PortIcon name="chart" /><span>查询统计</span><small>待接入</small></button>
      </nav>
      <div class="sidebar-bottom">
        <SessionAccount :account-session="accountSession" />
      </div>
    </aside>

    <main id="main-content" class="workspace-main" tabindex="-1">
      <header class="topbar">
        <button class="icon-button mobile-toggle" aria-label="展开业务导航" :aria-expanded="mobileMenuOpen" @click="mobileMenuOpen = !mobileMenuOpen"><PortIcon name="menu" /></button>
        <div class="page-title"><h1>{{ title }}</h1><span class="header-date">{{ dateLabel }}</span></div>
        <AppointmentSearch v-model="keyword" @search="submit" @all="searchAll" @select="chooseSuggestion" />
        <button class="button button-primary refresh-button" :disabled="loading || overviewLoading" @click="refresh"><PortIcon name="refresh" :size="17" /><span>刷新</span></button>
      </header>

      <div class="view-pane overview-pane" :hidden="activeView !== 'overview'" aria-label="港口总览">
        <section class="glass-panel metrics-strip wave-summary" aria-label="港口业务概况" :aria-busy="overviewLoading">
          <div class="metric"><span class="metric-icon"><PortIcon name="document" :size="25" /></span><div><span>待审核预约</span><strong>{{ overview?.pendingAppointments ?? '—' }}</strong></div></div>
          <div class="metric"><span class="metric-icon"><PortIcon name="anchor" :size="25" /></span><div><span>锚地在场</span><strong>{{ overview?.anchorageVessels ?? '—' }}</strong></div></div>
          <div class="metric"><span class="metric-icon"><PortIcon name="berth" :size="25" /></span><div><span>待靠泊计划</span><strong>{{ overview?.confirmedPlans ?? '—' }}</strong></div></div>
          <span v-if="overviewLoading" class="sr-only" role="status">正在更新总览</span>
        </section>
        <PortOverview :overview="overview" :loading="overviewLoading" @appointments="openStatus" @navigate="switchView" />
        <div class="overview-bottom">
          <OperationDistribution :overview="overview" @navigate="switchView" />
          <RecentPortEvents :overview="overview" :loading="overviewLoading" :error="overviewError" :active="activeView === 'overview'" @page="refreshOverview" @select="openEvent" />
        </div>
      </div>

      <div class="view-pane appointments-pane" :hidden="activeView !== 'appointments'" aria-label="进港预约工作区">
        <section class="glass-panel selection-strip" aria-label="当前选中预约" :aria-busy="loading">
          <template v-if="selected">
            <img :src="vesselImage" alt="" class="summary-vessel" />
            <div class="summary-identity"><strong>{{ selected.shipName }}</strong><span>{{ selected.appointmentNo }}</span></div>
            <div class="summary-time"><PortIcon name="calendar" :size="24" /><div><span>申请到港</span><strong>{{ formatDate(selected.requestedArrivalAt) }}</strong></div></div>
            <div class="summary-status"><span>预约状态</span><span class="status-badge" :class="statusClass(selected.appointmentStatusCode)"><i></i>{{ statusLabel(selected.appointmentStatusCode) }}</span></div>
          </template>
          <span v-else class="selection-placeholder">{{ loading || detail.loading.value ? '正在载入预约…' : error || detail.error.value ? '预约暂时无法读取' : '暂无选中预约' }}</span>
        </section>
        <div class="records-layout with-detail">
          <AppointmentRecords :rows="rows" :query="query" :loading="loading" :error="error" :total-count="totalCount" :total-pages="totalPages"
            :selected-id="selected?.appointmentId" @select="selectRow" @filter="filterStatus" @load="loadList" @reset="reset" @clear-keyword="clearKeyword">
            <template #actions><button v-if="accountSession.session.value?.permissions.canRegisterAppointment" class="button button-primary" @click="registration?.open()">登记预约<PortIcon name="arrow" :size="16" /></button></template>
          </AppointmentRecords>
          <div class="detail-host" :aria-busy="loading || detail.loading.value" :inert="loading || undefined">
            <p v-if="saveFeedback" class="save-feedback" role="status">{{ saveFeedback }}</p>
            <AppointmentRegistration ref="registration" :item="detail.item.value ?? undefined" :account-session="accountSession" @saved="registered" @reconcile="reconcileRegistration" />
            <AppointmentDetail v-if="selected" :item="selected" :account-session="accountSession" @saved="reviewed" @reconcile="reconcileReview" />
            <aside v-else class="glass-panel detail-panel detail-placeholder"><PortIcon name="document" :size="30" /><p v-if="detail.error.value" role="alert">{{ detail.error.value }}</p><p v-else>{{ loading || detail.loading.value ? '正在载入详情…' : '选择预约查看详情' }}</p><button v-if="detail.error.value" class="button" @click="detail.retry">重试详情</button></aside>
          </div>
        </div>
      </div>
      <div v-for="business in businessViews" :key="business" class="view-pane business-pane" :hidden="activeView !== business">
        <BusinessRecords :kind="business" :active="activeView === business" :refresh-key="businessRefreshKey" @select="openEvent" />
      </div>
    </main>
  </div>
</template>
