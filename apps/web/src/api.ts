const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL || 'http://localhost:8000').replace(/\/$/, '')

export interface ApiErrorPayload {
  code: string
  details: unknown
}

export interface ApiResponse<T> {
  success: boolean
  message: string
  data: T | null
  error: ApiErrorPayload | null
}

export interface PaginatedResponse<T> {
  items: T[]
  page: number
  page_size: number
  total: number
}

export interface DashboardPendingSummary {
  open_count: number
  open_count_by_target_domain: Record<string, number>
  opened_in_last_7_days: number
  resolved_in_last_7_days: number
  href: string
}

export interface DashboardQuickLink {
  label: string
  href: string
}

export interface DashboardExpenseSummary {
  created_in_current_month: number
  amount_by_currency_current_month: Record<string, string>
  latest_expense_created_at: string | null
  href: string
}

export interface DashboardKnowledgeSummary {
  created_in_last_7_days: number
  created_in_last_30_days: number
  latest_knowledge_created_at: string | null
  href: string
}

export interface DashboardHealthSummary {
  created_in_last_7_days: number
  latest_health_created_at: string | null
  recent_metric_types: string[]
  href: string
}

export interface DashboardRecentActivity {
  activity_type: string
  occurred_at: string
  target_domain: string
  target_id: number
  title_or_preview: string | null
  action_label: string
  href: string
}

export interface DashboardData {
  pending_summary: DashboardPendingSummary
  quick_links: DashboardQuickLink[]
  expense_summary: DashboardExpenseSummary
  knowledge_summary: DashboardKnowledgeSummary
  health_summary: DashboardHealthSummary
  recent_activity: DashboardRecentActivity[]
}

export interface PendingListItem {
  id: number
  status: string
  target_domain: string
  reason_preview: string | null
  created_at: string
  has_corrected_payload: boolean
  source_capture_id: number
  is_next_to_review: boolean
}

export interface PendingListResponse extends PaginatedResponse<PendingListItem> {
  next_pending_item_id: number | null
}

export interface PendingDetail {
  id: number
  status: string
  target_domain: string
  reason: string | null
  proposed_payload_json: unknown
  corrected_payload_json: unknown
  created_at: string
  resolved_at: string | null
  source_capture_id: number
  parse_result_id: number
}

export interface ExpenseListItem {
  id: number
  created_at: string
  amount: string
  currency: string
  category: string | null
  note_preview: string | null
  has_source_pending: boolean
}

export interface ExpenseDetail {
  id: number
  created_at: string
  amount: string
  currency: string
  category: string | null
  note: string | null
  source_capture_id: number
  source_pending_id: number | null
}

export interface KnowledgeListItem {
  id: number
  created_at: string
  display_title: string
  content_preview: string | null
  has_source_text: boolean
  has_source_pending: boolean
}

export interface KnowledgeDetail {
  id: number
  created_at: string
  title: string
  content: string | null
  source_text: string | null
  source_capture_id: number
  source_pending_id: number | null
}

export interface HealthListItem {
  id: number
  created_at: string
  metric_type: string
  value_text_preview: string | null
  note_preview: string | null
  has_source_pending: boolean
}

export interface HealthDetail {
  id: number
  created_at: string
  metric_type: string
  value_text: string | null
  note: string | null
  source_capture_id: number
  source_pending_id: number | null
}

export async function fetchDashboard(): Promise<DashboardData> {
  return request<DashboardData>('/api/dashboard')
}

export async function fetchPendingList(
  params: Record<string, string>,
): Promise<PendingListResponse> {
  return request<PendingListResponse>('/api/pending', params)
}

export async function fetchPendingDetail(id: string): Promise<PendingDetail> {
  return request<PendingDetail>(`/api/pending/${id}`)
}

export async function fetchExpenseList(
  params: Record<string, string>,
): Promise<PaginatedResponse<ExpenseListItem>> {
  return request<PaginatedResponse<ExpenseListItem>>('/api/expense', params)
}

export async function fetchExpenseDetail(id: string): Promise<ExpenseDetail> {
  return request<ExpenseDetail>(`/api/expense/${id}`)
}

export async function fetchKnowledgeList(
  params: Record<string, string>,
): Promise<PaginatedResponse<KnowledgeListItem>> {
  return request<PaginatedResponse<KnowledgeListItem>>('/api/knowledge', params)
}

export async function fetchKnowledgeDetail(id: string): Promise<KnowledgeDetail> {
  return request<KnowledgeDetail>(`/api/knowledge/${id}`)
}

export async function fetchHealthList(
  params: Record<string, string>,
): Promise<PaginatedResponse<HealthListItem>> {
  return request<PaginatedResponse<HealthListItem>>('/api/health', params)
}

export async function fetchHealthDetail(id: string): Promise<HealthDetail> {
  return request<HealthDetail>(`/api/health/${id}`)
}

async function request<T>(path: string, params?: Record<string, string>): Promise<T> {
  const url = new URL(`${apiBaseUrl}${path}`)

  if (params) {
    for (const [key, value] of Object.entries(params)) {
      if (value) {
        url.searchParams.set(key, value)
      }
    }
  }

  const response = await fetch(url.toString(), {
    headers: {
      Accept: 'application/json',
    },
  })

  const payload = (await response.json()) as ApiResponse<T>

  if (!response.ok || !payload.success || payload.data === null) {
    throw new Error(payload.message || `Request failed with status ${response.status}.`)
  }

  return payload.data
}
