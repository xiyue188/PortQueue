<script setup lang="ts">
// 总览只呈现全港概要；状态入口进入完整预约清单，不轮播单条预约。
import { computed } from 'vue'
import type { PortOverview } from '../composables/useOverview'
import type { AppointmentStatus } from '../types/appointment'
import { statusLabel, statusClass } from '../utils/appointmentPresentation'
import PortIcon from './PortIcon.vue'
import portImage from '../assets/port-scene.png'
const props = defineProps<{ overview: PortOverview | null; loading: boolean }>()
const emit = defineEmits<{ appointments: [status: AppointmentStatus | ''] }>()
const colors: Record<string, string> = { DRAFT: '#b6cbd9', PENDING: '#719ebc', APPROVED: '#3d759d', COMPLETED: '#183f60', REJECTED: '#5988a8', CANCELLED: '#93b1c5' }
const total = computed(() => props.overview?.appointmentStates.reduce((sum, state) => sum + state.count, 0))
const segments = computed(() => {
  let offset = 0
  return (props.overview?.appointmentStates ?? []).map(state => {
    const percent = total.value ? state.count / total.value * 100 : 0
    const segment = { ...state, percent, offset, color: colors[state.code] }; offset += percent; return segment
  })
})
</script>

<template>
  <div class="overview-focus" :aria-busy="loading">
    <section class="glass-panel vessel-spotlight port-summary" aria-labelledby="port-summary-title">
      <img class="spotlight-photo" :src="portImage" alt="" />
      <div class="spotlight-wash"></div>
      <div class="port-summary-title"><h2 id="port-summary-title">港口业务概况</h2></div>
      <div class="port-business-flow" aria-label="业务环节">
        <span><PortIcon name="ship" />进港预约</span><span><PortIcon name="anchor" />锚地排队</span><span><PortIcon name="berth" />泊位计划</span><span><PortIcon name="document" />进出港记录</span>
      </div>
    </section>
    <section class="glass-panel status-summary" aria-labelledby="status-summary-title">
      <div class="panel-heading"><h2 id="status-summary-title">预约分布</h2></div>
      <div class="distribution-body">
      <div class="distribution-chart" role="img" :aria-label="overview ? `全部预约 ${total} 条，各状态数量见右侧` : '预约分布暂未读取'">
        <svg viewBox="0 0 160 160" aria-hidden="true"><circle class="chart-track" cx="80" cy="80" r="62"/><circle v-for="segment in segments" :key="segment.code" cx="80" cy="80" r="62" pathLength="100" fill="none" :stroke="segment.color" :stroke-dasharray="`${Math.max(0, segment.percent - 1)} ${100 - Math.max(0, segment.percent - 1)}`" :stroke-dashoffset="-segment.offset - Math.min(.5, segment.percent / 2)" transform="rotate(-90 80 80)"/></svg>
        <div><strong>{{ total ?? '—' }}</strong><span>全部预约</span></div>
      </div>
      <div class="state-distribution">
        <button v-for="state in overview?.appointmentStates" :key="state.code" :disabled="loading" :aria-label="`查看${statusLabel(state.code)}预约，共 ${state.count} 条`" @click="emit('appointments', state.code)">
          <span class="status-text" :class="statusClass(state.code)" :style="{ '--state-color': colors[state.code] }"><i></i>{{ statusLabel(state.code) }}</span>
          <strong>{{ state.count }}</strong><PortIcon name="arrow" :size="14" />
        </button>
        <p v-if="!overview" class="summary-empty">{{ loading ? '正在读取…' : '暂时无法读取' }}</p>
      </div>
      </div>
      <button class="text-action" @click="emit('appointments', '')">查看全部预约<PortIcon name="arrow" :size="16" /></button>
    </section>
  </div>
</template>
