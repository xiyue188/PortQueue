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
const total = computed(() => props.overview?.appointmentStates.reduce((sum, state) => sum + state.count, 0))
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
      <div class="panel-heading"><h2 id="status-summary-title">预约分布</h2><span>{{ total === undefined ? '—' : `${total} 条` }}</span></div>
      <div class="state-distribution">
        <button v-for="state in overview?.appointmentStates" :key="state.code" :disabled="loading" :aria-label="`查看${statusLabel(state.code)}预约，共 ${state.count} 条`" @click="emit('appointments', state.code)">
          <span class="status-text" :class="statusClass(state.code)"><i></i>{{ statusLabel(state.code) }}</span>
          <strong>{{ state.count }}</strong><PortIcon name="arrow" :size="14" />
        </button>
        <p v-if="!overview" class="summary-empty">{{ loading ? '正在读取…' : '暂时无法读取' }}</p>
      </div>
      <button class="text-action" @click="emit('appointments', '')">查看全部预约<PortIcon name="arrow" :size="16" /></button>
    </section>
  </div>
</template>
