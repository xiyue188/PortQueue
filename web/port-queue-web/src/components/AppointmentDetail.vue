<script setup lang="ts">
// 详情先显示记录事实，完整申报读取成功后才允许审核；空值不解释为零。
import { computed } from 'vue'
import PortIcon from './PortIcon.vue'
import AppointmentProgress from './AppointmentProgress.vue'
import AppointmentReview from './AppointmentReview.vue'
import type { AppointmentItem, AppointmentDetail } from '../types/appointment'
import type { SessionClient } from '../composables/useSession'
import { formatDate } from '../utils/appointmentPresentation'
const props = defineProps<{ item: AppointmentItem; accountSession: SessionClient }>()
const emit = defineEmits<{ saved: [id: string, decision: 'APPROVED' | 'REJECTED']; reconcile: [id: string] }>()
const detail = computed(() => 'estimatedOperationHours' in props.item ? props.item as AppointmentDetail : null)
const quantity = (value: number | null, unit: string) => value === null ? '未申报' : `${value.toLocaleString('zh-CN', { maximumFractionDigits: 2 })} ${unit}`
</script>
<template>
  <aside id="appointment-detail" class="glass-panel detail-panel" aria-labelledby="detail-title" tabindex="-1">
    <div class="panel-heading"><h2 id="detail-title">预约详情</h2><PortIcon name="document" :size="20" /></div>
    <AppointmentProgress :item="item"/>
    <AppointmentReview v-if="detail" :item="detail" :account-session="accountSession" @saved="(id, decision) => emit('saved', id, decision)" @reconcile="id => emit('reconcile', id)"/>
    <template v-if="detail">
      <section class="declaration-section"><h3>船舶资料</h3><dl class="detail-facts">
        <div><dt>IMO / MMSI</dt><dd>{{ item.imoNo }} / {{ item.mmsi }}</dd></div>
        <div><dt>船型</dt><dd>{{ detail.shipType }}</dd></div><div><dt>船公司</dt><dd>{{ detail.companyName }}</dd></div>
        <div><dt>船长 / 船宽</dt><dd>{{ detail.lengthM }} / {{ detail.breadthM }} 米</dd></div><div><dt>最大吃水</dt><dd>{{ detail.maxDraftM }} 米</dd></div>
        <div><dt>总吨 / 载重吨</dt><dd>{{ detail.grossTonnage ?? '未登记' }} / {{ quantity(detail.deadweightT, '吨') }}</dd></div>
      </dl></section>
      <section class="declaration-section"><h3>申报作业</h3><dl class="detail-facts">
        <div><dt>申报吃水</dt><dd>{{ detail.declaredDraftM }} 米</dd></div><div><dt>预计作业</dt><dd>{{ detail.estimatedOperationHours }} 小时</dd></div>
        <div><dt>优先级</dt><dd>{{ detail.priorityLevel }} 级</dd></div><div><dt>危险品</dt><dd :class="{ 'dangerous-goods': detail.dangerousGoodsFlag }">{{ detail.dangerousGoodsFlag ? '涉及危险品' : '不涉及' }}</dd></div>
        <div><dt>箱量 / 货重</dt><dd>{{ quantity(detail.cargoTeu, 'TEU') }} / {{ quantity(detail.cargoWeightT, '吨') }}</dd></div>
        <div><dt>特殊要求</dt><dd class="long-value">{{ detail.specialRequirement ?? '无' }}</dd></div><div><dt>提交时间</dt><dd>{{ formatDate(detail.submittedAt) }}</dd></div>
      </dl></section>
      <details class="call-details"><summary>到访计划 <span>{{ detail.callNo }}</span></summary><dl class="detail-facts">
        <div><dt>前一港 / 下一港</dt><dd>{{ detail.previousPortName ?? '未申报' }} / {{ detail.nextPortName ?? '未申报' }}</dd></div>
        <div><dt>计划到港</dt><dd>{{ formatDate(detail.plannedArrivalAt) }}</dd></div><div><dt>计划离港</dt><dd>{{ formatDate(detail.plannedDepartureAt) }}</dd></div>
      </dl></details>
    </template>
    <div class="review-section"><h3>审核记录</h3>
      <div v-if="item.reviewedAt" class="review-line"><span class="review-icon"><PortIcon name="document" :size="18" /></span><span><strong>{{ item.reviewerName }}</strong><small>{{ formatDate(item.reviewedAt) }}</small></span></div>
      <p class="review-comment">{{ item.reviewedAt ? item.reviewComment ?? '未填写审核意见' : '尚未审核' }}</p>
    </div>
  </aside>
</template>
