<script setup lang="ts">
// 只读预约工作页：表单、结果和分页同屏，审核信息作为记录事实展示。
import { onMounted, ref } from 'vue'
import { useAppointments } from '../composables/useAppointments'
import { appointmentStatuses, type AppointmentStatus } from '../types/appointment'

const keyword = ref('')
const status = ref<AppointmentStatus | ''>('')
const { query, rows, totalCount, totalPages, loading, error, lastUpdated, load, search } = useAppointments()
const dateFormat = new Intl.DateTimeFormat('zh-CN', {
  timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit',
  hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
})
const clockFormat = new Intl.DateTimeFormat('zh-CN', {
  timeZone: 'Asia/Shanghai', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23',
})
const formatDate = (value: string | null) => value ? dateFormat.format(new Date(value)) : '—'
const statusLabel = (code: AppointmentStatus) => appointmentStatuses.find(item => item.code === code)?.label ?? code

function submit() { void search(keyword.value, status.value) }
function reset() { keyword.value = ''; status.value = ''; void search('', '') }
function changePageSize(event: Event) {
  const pageSize = Number((event.target as HTMLSelectElement).value)
  void load({ ...query.value, page: 1, pageSize })
}

onMounted(() => { void load() })
</script>

<template>
  <main id="main-content" class="appointment-page">
    <header class="page-heading">
      <div><h1>进港预约</h1><p>查询船舶申请记录与审核结果。</p></div>
      <p class="timezone">时间均为北京时间 <span>UTC+8</span></p>
    </header>
    <form class="query-form" aria-label="预约筛选" @submit.prevent="submit">
      <div class="field keyword-field">
        <label for="appointment-keyword">关键词</label>
        <input id="appointment-keyword" v-model="keyword" type="search" maxlength="100"
          placeholder="预约编号、船名、IMO 或 MMSI" autocomplete="off" />
      </div>
      <div class="field status-field">
        <label for="appointment-status">预约状态</label>
        <select id="appointment-status" v-model="status">
          <option value="">全部状态</option>
          <option v-for="item in appointmentStatuses" :key="item.code" :value="item.code">{{ item.label }}</option>
        </select>
      </div>
      <div class="form-actions">
        <button class="button button-primary" type="submit">查询</button>
        <button class="button" type="button" @click="reset">重置</button>
      </div>
    </form>
    <section class="results" aria-labelledby="results-title" :aria-busy="loading">
      <div class="results-toolbar">
        <h2 id="results-title">预约记录 <span v-if="!loading && !error">{{ totalCount }} 条</span></h2>
        <div class="results-actions">
          <span v-if="lastUpdated" class="updated-at">更新于 {{ clockFormat.format(lastUpdated) }}</span>
          <button class="button button-quiet" type="button" :disabled="loading" @click="load()">
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M20 7v5h-5M4 17v-5h5M5.2 8a7.5 7.5 0 0 1 12.3-3L20 8M4 16l2.5 3A7.5 7.5 0 0 0 18.8 16" /></svg>
            刷新
          </button>
        </div>
      </div>
      <div v-if="loading" class="result-message" role="status">
        <span class="loading-line" aria-hidden="true"></span>
        <h3>正在查询预约</h3><p>正在获取最新记录，请稍候。</p>
      </div>
      <div v-else-if="error" class="result-message error-message" role="alert">
        <h3>暂时无法显示预约</h3><p>{{ error }}</p>
        <button class="button button-primary" type="button" @click="load()">重新查询</button>
      </div>
      <div v-else-if="rows.length === 0" class="result-message" role="status">
        <h3>{{ totalCount > 0 ? '这一页没有预约记录' : '没有符合条件的预约' }}</h3>
        <p>{{ totalCount > 0 ? '记录数量可能已变化，请返回第一页查看。' : '可以调整关键词或状态，查看其他预约。' }}</p>
        <button v-if="totalCount > 0" class="button" type="button" @click="load({ ...query, page: 1 })">返回第一页</button>
        <button v-else class="button" type="button" @click="reset">清除筛选</button>
      </div>
      <div v-else class="table-result">
        <p class="table-scroll-hint">窄屏可在表格区域横向滚动。</p>
        <div class="table-scroll" tabindex="0" role="region" aria-label="预约结果表，可横向滚动">
        <table class="appointments-table">
          <caption class="sr-only">预约记录，按申请到港时间从近到远排列；同一时间按预约主键倒序。</caption>
          <thead><tr>
            <th scope="col">预约编号</th><th scope="col">船舶</th><th scope="col">申请到港 / 离港</th>
            <th scope="col">状态</th><th scope="col">审核记录</th><th scope="col">审核意见</th>
          </tr></thead>
          <tbody>
            <tr v-for="row in rows" :key="row.appointmentId">
              <td class="appointment-no">{{ row.appointmentNo }}</td>
              <td><strong class="ship-name">{{ row.shipName }}</strong>
                <span class="cell-secondary">IMO {{ row.imoNo ?? '—' }}</span>
                <span class="cell-secondary">MMSI {{ row.mmsi ?? '—' }}</span>
              </td>
              <td class="date-cell"><span class="date-line"><span class="date-label">到港</span>{{ formatDate(row.requestedArrivalAt) }}</span>
                <span class="cell-secondary date-line"><span class="date-label">离港</span>{{ formatDate(row.requestedDepartureAt) }}</span>
              </td>
              <td><span class="appointment-status" :class="`status-${row.appointmentStatusCode.toLowerCase()}`">{{ statusLabel(row.appointmentStatusCode) }}</span></td>
              <td><span>{{ row.reviewerName ?? '—' }}</span><span class="cell-secondary">{{ formatDate(row.reviewedAt) }}</span></td>
              <td class="review-comment">
                <details v-if="row.reviewComment"><summary>查看意见</summary><p>{{ row.reviewComment }}</p></details>
                <span v-else class="cell-secondary">—</span>
              </td>
            </tr>
          </tbody>
        </table>
        </div>
      </div>
      <footer class="pagination">
        <p role="status" aria-live="polite">{{ loading ? '正在加载' : error ? '查询未完成' : `共 ${totalCount} 条记录` }}</p>
        <div class="pagination-controls">
          <label for="page-size">每页</label>
          <select id="page-size" :value="query.pageSize" :disabled="loading" @change="changePageSize">
            <option :value="2">2 条</option><option :value="10">10 条</option>
            <option :value="20">20 条</option><option :value="50">50 条</option><option :value="100">100 条</option>
          </select>
          <button class="button" type="button" :disabled="loading || !!error || query.page <= 1" @click="load({ ...query, page: query.page - 1 })">上一页</button>
          <span class="page-number" aria-live="polite" :aria-label="loading || error ? '页码暂不可用' : `第 ${query.page} 页，共 ${totalPages} 页`">{{ loading || error ? '—' : `${query.page} / ${totalPages}` }}</span>
          <button class="button" type="button" :disabled="loading || !!error || query.page >= totalPages" @click="load({ ...query, page: query.page + 1 })">下一页</button>
        </div>
      </footer>
    </section>
    <p class="page-note">按申请到港时间倒序排列。审核记录为空表示尚无审核信息。</p>
  </main>
</template>
