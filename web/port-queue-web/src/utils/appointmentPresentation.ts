// 全页面共用展示口径：北京时间、状态名称和审核描述，不推断实际到港事件。
import { appointmentStatuses, type AppointmentStatus } from '../types/appointment'

const fullDate = new Intl.DateTimeFormat('zh-CN', {
  timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit',
  hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
})
export const formatDate = (value: string | null) => value ? fullDate.format(new Date(value)) : '—'
export const statusLabel = (code: AppointmentStatus) => appointmentStatuses.find(item => item.code === code)?.label ?? code
export const statusClass = (code: AppointmentStatus) => `status-${code.toLowerCase()}`
