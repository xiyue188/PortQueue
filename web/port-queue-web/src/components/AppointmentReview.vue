<script setup lang="ts">
// 每次确认绑定预约主键；写成功与读刷新分开，失败保留意见，网络不明不自动重放。
import { computed, nextTick, ref, watch } from 'vue'
import type { AppointmentDetail } from '../types/appointment'
import type { SessionClient } from '../composables/useSession'
import PortIcon from './PortIcon.vue'
const props = defineProps<{ item: AppointmentDetail; accountSession: SessionClient }>()
const emit = defineEmits<{ saved: [id: string, decision: 'APPROVED' | 'REJECTED']; reconcile: [id: string] }>()
const dialog = ref<HTMLDialogElement>(), commentInput = ref<HTMLTextAreaElement>()
const decision = ref<'APPROVED' | 'REJECTED'>('APPROVED'), comment = ref(''), error = ref(''), busy = ref(false), uncertain = ref(false)
const allowed = computed(() => props.item.appointmentStatusCode === 'PENDING' && props.accountSession.session.value?.permissions.canReviewAppointment)
let targetId = '', targetName = ''
async function open(value: 'APPROVED' | 'REJECTED') {
  decision.value = value; error.value = ''; uncertain.value = false
  targetId = props.item.appointmentId; targetName = props.item.shipName
  dialog.value?.showModal(); await nextTick(); commentInput.value?.focus()
}
function close() { if (!busy.value) dialog.value?.close() }
// 输入补全后仅清除本地必填提示；网络/提交结果提示必须保留。
watch(comment, value => { if (value.trim() && error.value === '驳回时请填写原因。') error.value = '' })
watch(() => props.item.appointmentId, () => { if (!busy.value) { close(); comment.value = ''; error.value = '' } })
async function submit() {
  if (busy.value || uncertain.value) return
  if (decision.value === 'REJECTED' && !comment.value.trim()) { error.value = '驳回时请填写原因。'; return }
  busy.value = true; error.value = ''
  const request = new AbortController(), timer = setTimeout(() => request.abort(), 25_000)
  try {
    const response = await fetch(`/api/appointments/${targetId}/review`, { method: 'POST', credentials: 'same-origin', signal: request.signal,
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': props.accountSession.session.value?.csrfToken ?? '' },
      body: JSON.stringify({ decision: decision.value, reviewComment: comment.value.trim() || null }) })
    const result = await response.json() as { title?: string; code?: string; appointmentId?: string; appointmentStatusCode?: string }
    if (!response.ok) {
      error.value = response.status === 400 ? result.title ?? '提交内容无效，请检查审核意见。' : response.status === 401 ? '登录已失效，请重新登录后操作。' : response.status === 403 ? '当前账号没有审核权限。' : response.status === 409 ? result.title ?? '该预约已经处理，请查看最新状态。' : response.status === 404 ? '该预约不存在。' : '审核暂时无法完成，请稍后查看最新状态。'
      if ([401, 403].includes(response.status) || result.code === 'INVALID_CSRF_TOKEN') await props.accountSession.refresh()
      if (response.status === 409 || response.status >= 500) uncertain.value = true
      return
    }
    if (result.appointmentId !== targetId || result.appointmentStatusCode !== decision.value) throw new Error('Unexpected review response')
    dialog.value?.close(); comment.value = ''
    emit('saved', targetId, decision.value)
  } catch { uncertain.value = true; error.value = '尚未确认审核结果，请先刷新该预约，勿重复提交。' }
  finally { clearTimeout(timer); busy.value = false }
}
function reconcile() { close(); emit('reconcile', targetId) }
</script>
<template>
  <div v-if="allowed" class="review-actions"><button class="button" @click="open('REJECTED')">驳回</button><button class="button button-primary" @click="open('APPROVED')">批准预约<PortIcon name="arrow" :size="16"/></button></div>
  <Teleport to="body"><dialog ref="dialog" class="review-dialog" aria-labelledby="review-dialog-title" @cancel.prevent="close">
    <div class="panel-heading"><h2 id="review-dialog-title">{{ decision === 'APPROVED' ? '批准预约' : '驳回预约' }}</h2><button class="icon-button" aria-label="关闭审核" :disabled="busy" @click="close"><PortIcon name="reset" :size="18"/></button></div>
    <p class="review-target">{{ targetName }} <span>{{ item.appointmentNo }}</span></p>
    <form @submit.prevent="submit"><label for="review-comment">审核意见{{ decision === 'REJECTED' ? '（必填）' : '（选填）' }}</label><textarea id="review-comment" ref="commentInput" v-model="comment" maxlength="1000" rows="4" :disabled="busy"/><p v-if="error" class="login-error" role="alert">{{ error }}</p>
      <div class="dialog-actions"><button class="button" type="button" :disabled="busy" @click="close">取消</button><button v-if="uncertain" class="button button-primary" type="button" @click="reconcile">查看最新状态</button><button v-else class="button button-primary" type="submit" :disabled="busy">{{ busy ? '正在提交…' : decision === 'APPROVED' ? '确认批准' : '确认驳回' }}</button></div>
    </form>
  </dialog></Teleport>
</template>
