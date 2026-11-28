// 状态来自计划；候选来自版本是否确认。实际事件不按计划时钟推算。
import type { BerthRevision } from '../types/berth'
export const planLabel = (status: string | null) => ({ DRAFT:'草稿', CONFIRMED:'已确认', EXECUTING:'执行中', COMPLETED:'已完成', CANCELLED:'已取消' }[status ?? ''] ?? '待安排')
export const revisionLabel = (revision: BerthRevision) => !revision.confirmedAt && revision.planStatusCode !== 'DRAFT' ? '候选' : planLabel(revision.planStatusCode)
export const berthTime = (value: string) => new Intl.DateTimeFormat('zh-CN', {timeZone:'Asia/Shanghai',hour:'2-digit',minute:'2-digit',hour12:false}).format(new Date(value))
export const localMinute = (value: string) => new Date(Date.parse(value) + 8 * 3600_000).toISOString().slice(0,16)
export const berthDay = (value:string) => localMinute(value).slice(5,10).replace('-','/')
export const berthRange = (from:string,to:string) => berthDay(from) === berthDay(to) ? `${berthTime(from)}—${berthTime(to)}` : `${berthDay(from)} ${berthTime(from)}—${berthDay(to)} ${berthTime(to)}`
