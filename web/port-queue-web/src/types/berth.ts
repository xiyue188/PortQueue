// 泊位只读协议：所有标识以字符串传输，时间必须包含 UTC offset。
export interface BerthResource {
  berthId: string; code: string; name: string; terminalName: string
  statusCode: string; terminalStatusCode: string; portStatusCode: string
  lengthM: number; maxDraftM: number; maxDwt: number | null
  shipTypes: { itemId: string; name: string }[]
}
export interface BerthRevision {
  revisionId: string; berthPlanId: string; appointmentId: string; shipName: string; callNo: string
  berthId: string; versionNo: number; plannedBerthingAt: string; plannedUnberthingAt: string
  planStatusCode: string; isCurrent: boolean; confirmedAt: string | null
  revisedAt: string; revisionReason: string; revisedByName: string; confirmedByName: string | null
}
export interface BerthOccupancy {
  appointmentId: string; shipName: string; callNo: string; berthId: string
  executedRevisionId: string; actualBerthingAt: string; actualUnberthingAt: string | null
}
export interface BerthSchedule {
  observedAt: string; from: string; to: string
  resources: BerthResource[]; revisions: BerthRevision[]; occupancies: BerthOccupancy[]
}
export interface BerthVisitSummary {
  appointmentId: string; appointmentNo: string; portCallId: string; callNo: string
  shipId: string; shipName: string; appointmentStatusCode: string; requestedArrivalAt: string
  estimatedOperationHours: number; shipTypeItemId: string; shipTypeName: string
  lengthM: number; declaredDraftM: number; deadweightT: number | null
  planStatusCode: string | null; berthPlanId: string | null; queueEntryId: string | null
  anchorageName: string | null; manualEstimatedBerthingAt: string | null
}
export interface BerthVisitPage {
  items: BerthVisitSummary[]; page: number; pageSize: number; totalCount: number
}
export interface BerthVisitDetail extends BerthVisitSummary {
  submittedAt: string | null; reviewedAt: string | null; reviewerName: string | null
  enteredAt: string | null; leftAt: string | null; versions: BerthRevision[]
  events: {
    eventId: string; eventTypeCode: string; eventTime: string; queueEntryId: string | null
    executedRevisionId: string | null; recordedByName: string | null
  }[]
}
