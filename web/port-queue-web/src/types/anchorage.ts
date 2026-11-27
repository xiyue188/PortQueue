// 锚地查询协议：标识保持字符串，时间由服务端以UTC返回，顺位不在客户端重新生成。
export interface AnchorageSummary {
  anchorageId: string; code: string; name: string; statusCode: string
  capacity: number | null; occupiedCount: number; queueCount: number
}
export interface AnchorageList {
  observedAt: string; portId: string; portCode: string; portName: string
  locatedStay: { queueEntryId: string; anchorageId: string } | null; items: AnchorageSummary[]
}
export interface AnchorageStay {
  queueEntryId: string; anchorageId: string; appointmentId: string; portCallId: string
  callNo: string; appointmentNo: string; shipId: string; shipName: string
  queuePosition: number | null; priorityLevel: number; statusCode: string
  enteredAt: string | null; leftAt: string | null; legacyEstimatedBerthingAt: string | null
  manualEstimatedBerthingAt: string | null; estimateVersion: string
  confirmedBerthingAt: string | null; confirmedUnberthingAt: string | null
  berthName: string | null; confirmedRevisionId: string | null
}
export interface AnchorageQueue {
  anchorageId: string; observedAt: string; snapshot: string
  occupiedCount: number; capacity: number | null; statusCode: string
  items: AnchorageStay[]; cancelled: AnchorageStay[]
}
export interface AnchorageDetail extends AnchorageStay {
  anchorageName: string; requestedArrivalAt: string; submittedAt: string | null
  reviewedAt: string | null; reviewerName: string | null; appointmentStatusCode: string
  events: { eventId: string; eventTypeCode: string; eventTime: string; queueEntryId: string | null; recordedByName: string | null }[]
}

export interface AnchorageCandidate {
  appointmentId: string; appointmentNo: string; portCallId: string; callNo: string
  shipId: string; shipName: string; requestedArrivalAt: string; priorityLevel: number
}
export interface AnchorageCandidates {
  items: AnchorageCandidate[]; page: number; pageSize: number; totalCount: number
}
export interface AnchorageWriteResult {
  queueEntryId: string; anchorageId?: string; appointmentId?: string
  eventId?: string; eventTypeCode?: string; eventTime?: string
  manualEstimatedBerthingAt?: string | null; estimateVersion?: string
}
