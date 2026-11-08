<script setup lang="ts">
// 仅呈现当前选中记录的事实；API 没有提供的泊位、航线和实到时间不补造。
import PortIcon from './PortIcon.vue'
import type { AppointmentItem } from '../types/appointment'
import { formatDate, statusClass, statusLabel } from '../utils/appointmentPresentation'
import vesselImage from '../assets/vessel-reference.png'
defineProps<{ item: AppointmentItem }>()
</script>

<template>
  <aside id="appointment-detail" class="glass-panel detail-panel" aria-labelledby="detail-title" tabindex="-1">
    <div class="panel-heading"><h2 id="detail-title">预约详情</h2><PortIcon name="document" :size="20" /></div>
    <div class="detail-identity"><h3>{{ item.shipName }}</h3><span class="status-badge" :class="statusClass(item.appointmentStatusCode)"><i></i>{{ statusLabel(item.appointmentStatusCode) }}</span></div>
    <figure class="detail-vessel"><img :src="vesselImage" alt="" /></figure>
    <dl class="detail-facts">
      <div><dt>预约编号</dt><dd>{{ item.appointmentNo }}</dd></div>
      <div><dt>IMO</dt><dd>{{ item.imoNo ?? '—' }}</dd></div>
      <div><dt>MMSI</dt><dd>{{ item.mmsi ?? '—' }}</dd></div>
      <div><dt>申请到港</dt><dd>{{ formatDate(item.requestedArrivalAt) }}</dd></div>
      <div><dt>申请离港</dt><dd>{{ formatDate(item.requestedDepartureAt) }}</dd></div>
    </dl>
    <div class="review-section">
      <h3>审核记录</h3>
      <div class="review-line"><span class="review-icon" :class="item.reviewedAt ? statusClass(item.appointmentStatusCode) : ''"><PortIcon :name="item.appointmentStatusCode === 'REJECTED' ? 'reset' : item.reviewedAt ? 'check' : 'clock'" :size="16" /></span>
        <span><strong>{{ item.reviewerName ?? '尚无审核信息' }}</strong><small>{{ item.reviewedAt ? formatDate(item.reviewedAt) : '—' }}</small></span>
      </div>
      <p class="review-comment">{{ item.reviewComment ?? '暂无审核意见' }}</p>
    </div>

  </aside>
</template>
