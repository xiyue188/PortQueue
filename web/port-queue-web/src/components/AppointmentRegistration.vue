<script setup lang="ts">
// 登记抽屉与提交确认共用工作区会话；已确认写入与后续刷新分开处理。
import { computed, nextTick, reactive, ref } from 'vue'
import type { AppointmentDetail } from '../types/appointment'
import type { SessionClient } from '../composables/useSession'
import { fetchAppointment } from '../api/appointments'
import ReferenceSelect, { type ReferenceOption } from './ReferenceSelect.vue'
import PortIcon from './PortIcon.vue'
const props = defineProps<{ accountSession: SessionClient; item?: AppointmentDetail }>()
const emit = defineEmits<{ saved: [id: string, status: string]; reconcile: [id: string] }>()
const allowed = computed(() => !!props.accountSession.session.value?.permissions.canRegisterAppointment)
const drawer = ref<HTMLDialogElement>(), confirmation = ref<HTMLDialogElement>(), form = ref<HTMLFormElement>()
const ship = ref<ReferenceOption | null>(null), previous = ref<ReferenceOption | null>(null), next = ref<ReferenceOption | null>(null)
const busy = ref(false), error = ref(''), uncertain = ref(false), mode = ref<'create' | 'edit'>('create')
const fields = reactive({ requestedArrivalAt: '', requestedDepartureAt: '', plannedArrivalAt: '', plannedDepartureAt: '', cargoTeu: '', cargoWeightT: '', estimatedOperationHours: '', declaredDraftM: '', priorityLevel: '3', dangerousGoodsFlag: false, specialRequirement: '' })
const target = ref<AppointmentDetail>()
let requestKey = '', pendingRequest: { path: string; method: string; body: object } | undefined
let returnFocus: HTMLElement | null = null
const local = (value: string | null) => value ? new Date(Date.parse(value) + 8 * 3600_000).toISOString().slice(0, 19) : ''
const iso = (value: string) => `${value.length === 16 ? value + ':00' : value}+08:00`
function fill(item?: AppointmentDetail) {
  target.value = item; ship.value = item ? { id: item.shipId, name: item.shipName, detail: `IMO ${item.imoNo}`, maxDraftM: item.maxDraftM } : null
  previous.value = item?.previousPortId ? { id: item.previousPortId, name: item.previousPortName ?? '', detail: '', maxDraftM: null } : null
  next.value = item?.nextPortId ? { id: item.nextPortId, name: item.nextPortName ?? '', detail: '', maxDraftM: null } : null
  Object.assign(fields, { requestedArrivalAt: local(item?.requestedArrivalAt ?? null), requestedDepartureAt: local(item?.requestedDepartureAt ?? null), plannedArrivalAt: local(item?.plannedArrivalAt ?? null), plannedDepartureAt: local(item?.plannedDepartureAt ?? null), cargoTeu: item?.cargoTeu?.toString() ?? '', cargoWeightT: item?.cargoWeightT?.toString() ?? '', estimatedOperationHours: item?.estimatedOperationHours.toString() ?? '', declaredDraftM: item?.declaredDraftM.toString() ?? '', priorityLevel: item?.priorityLevel.toString() ?? '3', dangerousGoodsFlag: item?.dangerousGoodsFlag ?? false, specialRequirement: item?.specialRequirement ?? '' })
}
async function open(item?: AppointmentDetail) {
  if (!allowed.value || busy.value) return
  returnFocus = document.activeElement as HTMLElement; mode.value = item ? 'edit' : 'create'; requestKey = crypto.randomUUID(); fill(item)
  error.value = ''; uncertain.value = false; pendingRequest = undefined; drawer.value?.showModal()
  await nextTick(); document.getElementById('registration-ship')?.focus()
}
function close() {
  if (busy.value || uncertain.value) return
  drawer.value?.close(); confirmation.value?.close(); returnFocus?.focus({ preventScroll: true })
}
function data() {
  return { shipId: ship.value!.id, previousPortId: previous.value?.id ?? null, nextPortId: next.value?.id ?? null,
    requestedArrivalAt: iso(fields.requestedArrivalAt), requestedDepartureAt: iso(fields.requestedDepartureAt),
    plannedArrivalAt: iso(fields.plannedArrivalAt), plannedDepartureAt: fields.plannedDepartureAt ? iso(fields.plannedDepartureAt) : null,
    cargoTeu: fields.cargoTeu === '' ? null : Number(fields.cargoTeu), cargoWeightT: fields.cargoWeightT === '' ? null : Number(fields.cargoWeightT),
    estimatedOperationHours: Number(fields.estimatedOperationHours), declaredDraftM: Number(fields.declaredDraftM),
    priorityLevel: Number(fields.priorityLevel), dangerousGoodsFlag: fields.dangerousGoodsFlag, specialRequirement: fields.specialRequirement.trim() || null }
}
function save(submit: boolean) {
  if (busy.value || uncertain.value || !form.value?.reportValidity()) return
  if (!ship.value) { error.value = '请从搜索结果中选择船舶。'; return }
  if (Date.parse(iso(fields.requestedDepartureAt)) <= Date.parse(iso(fields.requestedArrivalAt))) { error.value = '申请离港必须晚于申请到港。'; return }
  if (fields.plannedDepartureAt && Date.parse(iso(fields.plannedDepartureAt)) <= Date.parse(iso(fields.plannedArrivalAt))) { error.value = '预计离港时间必须晚于预计到港时间。'; return }
  if (Number(fields.declaredDraftM) > (ship.value.maxDraftM ?? Infinity)) { error.value = `申报吃水不能超过该船最大吃水 ${ship.value.maxDraftM} 米。`; return }
  pendingRequest = mode.value === 'create' ? { path: '/api/appointments', method: 'POST', body: { requestKey, submit, data: data() } }
    : { path: `/api/appointments/${target.value!.appointmentId}/draft`, method: 'PUT', body: { expectedVersion: target.value!.editVersion, data: data() } }
  void write()
}
function confirmSubmit() {
  if (!props.item || !allowed.value) return
  returnFocus = document.activeElement as HTMLElement; target.value = props.item; error.value = ''; uncertain.value = false
  pendingRequest = { path: `/api/appointments/${target.value.appointmentId}/submit`, method: 'POST', body: { expectedVersion: target.value.editVersion } }
  confirmation.value?.showModal()
}
async function write() {
  if (busy.value || !pendingRequest) return
  busy.value = true; error.value = ''
  const request = new AbortController(), timer = setTimeout(() => request.abort(), 25_000)
  try {
    const response = await fetch(pendingRequest.path, { method: pendingRequest.method, credentials: 'same-origin', signal: request.signal,
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': props.accountSession.session.value?.csrfToken ?? '' }, body: JSON.stringify(pendingRequest.body) })
    const result = await response.json()
    if (!response.ok) {
      error.value = response.status === 401 ? '登录已失效，请重新登录后继续。' : response.status === 403 ? '当前账号没有登记权限。'
        : result.errors ? Object.values(result.errors as Record<string, string[]>).flat().join(' ') : result.title ?? '保存暂时无法完成，请重试。'
      if ([401, 403].includes(response.status) || result.code === 'INVALID_CSRF_TOKEN') await props.accountSession.refresh()
      uncertain.value = response.status >= 500 || (response.status === 409 && result.code !== 'REQUEST_KEY_CONFLICT')
      return
    }
    if (!/^\d+$/.test(result.appointmentId) || !['DRAFT', 'PENDING', 'APPROVED', 'REJECTED', 'CANCELLED', 'COMPLETED'].includes(result.appointmentStatusCode)) throw new Error('响应无效')
    uncertain.value = false; drawer.value?.close(); confirmation.value?.close(); emit('saved', result.appointmentId, result.appointmentStatusCode)
  } catch { uncertain.value = true; error.value = '尚未确认保存结果，请先核对，勿重新登记。' }
  finally { clearTimeout(timer); busy.value = false }
}
async function reconcile() {
  // 新建原键原内容重放由数据库幂等保证；已有草稿先读事实，不重复覆盖用户数据。
  if (mode.value === 'create' && pendingRequest?.path === '/api/appointments') { await write(); return }
  if (!target.value || busy.value) return
  busy.value = true
  try {
    const latest = await fetchAppointment(target.value.appointmentId, AbortSignal.timeout(15_000))
    uncertain.value = false; error.value = ''
    if (drawer.value?.open && latest.appointmentStatusCode === 'DRAFT') fill(latest)
    else { drawer.value?.close(); confirmation.value?.close(); emit('reconcile', latest.appointmentId) }
  } catch { error.value = '最新内容暂时无法读取，请重试；原输入仍保留。' }
  finally { busy.value = false }
}
defineExpose({ open })
</script>
<template>
  <div v-if="allowed && item?.appointmentStatusCode === 'DRAFT'" class="draft-actions"><button class="button" @click="open(item)">编辑草稿</button><button class="button button-primary" @click="confirmSubmit">提交审核<PortIcon name="arrow" :size="16" /></button></div>
  <Teleport to="body">
    <dialog ref="drawer" class="registration-drawer" aria-labelledby="registration-title" @cancel.prevent="close">
      <div class="registration-header"><div><h2 id="registration-title">{{ mode === 'create' ? '登记进港预约' : '编辑预约草稿' }}</h2></div><button class="icon-button" aria-label="关闭登记" :disabled="busy || uncertain" @click="close"><PortIcon name="reset" /></button></div>
      <p class="registration-role">调度员依据船方提供的资料代为登记。</p>
      <form ref="form" class="registration-form" @submit.prevent="save(false)">
        <fieldset :disabled="busy || uncertain"><legend>船舶与本次到访</legend><div class="form-grid">
          <div class="form-span"><ReferenceSelect id="registration-ship" v-model="ship" kind="ships" label="船舶" required :disabled="busy || uncertain" /></div>
          <ReferenceSelect id="registration-previous" v-model="previous" kind="ports" label="上一港（选填）" :disabled="busy || uncertain" />
          <ReferenceSelect id="registration-next" v-model="next" kind="ports" label="下一港（选填）" :disabled="busy || uncertain" />
          <label>集装箱量（TEU）<input v-model="fields.cargoTeu" type="number" min="0" max="2147483647" step="1" placeholder="选填" /></label>
          <label>货重（吨）<input v-model="fields.cargoWeightT" type="number" min="0" max="9999999999.99" step="0.01" placeholder="选填" /></label>
        </div></fieldset>
        <fieldset :disabled="busy || uncertain"><legend>申请与作业</legend><div class="form-grid">
          <label>申请到港 *<input v-model="fields.requestedArrivalAt" type="datetime-local" step="1" required /></label><label>申请离港 *<input v-model="fields.requestedDepartureAt" type="datetime-local" step="1" required /></label>
          <label>预计作业时长（小时）*<input v-model="fields.estimatedOperationHours" type="number" min="0.01" max="9999.99" step="0.01" required /></label><label>申报吃水（米）*<input v-model="fields.declaredDraftM" type="number" min="0.01" :max="ship?.maxDraftM ?? 9999.99" step="0.01" required /></label>
          <div class="form-span planned-heading"><h3>本次到访预计时间</h3><button type="button" class="text-action" :disabled="!fields.requestedArrivalAt" @click="fields.plannedArrivalAt = fields.requestedArrivalAt; fields.plannedDepartureAt = fields.requestedDepartureAt">沿用申请时间</button></div>
          <label>预计到港 *<input v-model="fields.plannedArrivalAt" type="datetime-local" step="1" required /></label><label>预计离港（选填）<input v-model="fields.plannedDepartureAt" type="datetime-local" step="1" /></label>
        </div></fieldset>
        <fieldset :disabled="busy || uncertain"><legend>补充要求</legend><div class="form-grid"><label>优先级<select v-model="fields.priorityLevel"><option v-for="value in 5" :key="value" :value="String(value)">{{ value }} 级</option></select></label><label class="checkbox-field"><input v-model="fields.dangerousGoodsFlag" type="checkbox" />载有危险品</label><label class="form-span">特殊要求（选填）<textarea v-model="fields.specialRequirement" rows="3" maxlength="1000" /></label></div></fieldset>
        <div class="registration-footer"><p v-if="error" role="alert" class="login-error">{{ error }}</p><div class="dialog-actions"><button type="button" class="button" :disabled="busy || uncertain" @click="close">取消</button><button v-if="uncertain" type="button" class="button button-primary" :disabled="busy" @click="reconcile">{{ mode === 'create' ? '核对登记结果' : '读取最新内容' }}</button><template v-else><button type="submit" class="button" :disabled="busy">{{ busy ? '正在保存…' : '保存草稿' }}</button><button v-if="mode === 'create'" type="button" class="button button-primary" :disabled="busy" @click="save(true)">提交审核</button></template></div></div>
      </form>
    </dialog>
    <dialog ref="confirmation" class="review-dialog" aria-labelledby="submit-title" @cancel.prevent="close"><div class="panel-heading"><h2 id="submit-title">提交审核</h2><button class="icon-button" aria-label="关闭提交确认" :disabled="busy || uncertain" @click="close"><PortIcon name="reset" /></button></div><p class="review-target">{{ target?.shipName }}<span>{{ target?.appointmentNo }}</span></p><p class="submit-consequence">提交后进入待审核状态，申报内容将锁定。</p><p v-if="error" class="login-error" role="alert">{{ error }}</p><div class="dialog-actions"><button class="button" :disabled="busy || uncertain" @click="close">取消</button><button class="button button-primary" :disabled="busy" @click="uncertain ? reconcile() : write()">{{ busy ? '正在提交…' : uncertain ? '查看最新状态' : '确认提交' }}</button></div></dialog>
  </Teleport>
</template>
