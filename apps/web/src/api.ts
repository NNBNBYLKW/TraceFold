const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL || 'http://localhost:8000').replace(/\/$/, '')

export class ApiRequestError extends Error {
  statusCode: number | null

  constructor(message: string, statusCode: number | null = null) {
    super(message)
    this.name = 'ApiRequestError'
    this.statusCode = statusCode
  }
}

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

export interface DashboardAlertSummaryItem {
  id: number
  source_record_id: number
  severity: string
  title: string
  message: string
  triggered_at: string
  href: string
}

export interface DashboardAlertSummary {
  open_count: number
  recent_open_items: DashboardAlertSummaryItem[]
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
  alert_summary: DashboardAlertSummary
  quick_links: DashboardQuickLink[]
  expense_summary: DashboardExpenseSummary
  knowledge_summary: DashboardKnowledgeSummary
  health_summary: DashboardHealthSummary
  recent_activity: DashboardRecentActivity[]
}

export interface WorkbenchTemplate {
  template_id: number
  template_type: string
  name: string
  default_module: string
  default_view_key: string | null
  default_query_json: unknown
  description: string | null
  scoped_shortcut_ids: number[]
  sort_order: number
  is_enabled: boolean
  created_at: string
  updated_at: string
}

export interface WorkbenchShortcut {
  shortcut_id: number
  label: string
  target_type: string
  target_payload_json: unknown
  sort_order: number
  is_enabled: boolean
  created_at: string
  updated_at: string
}

export interface WorkbenchRecentContext {
  recent_id: number
  object_type: string
  object_id: string
  action_type: string
  title_snapshot: string
  route_snapshot: string
  context_payload_json: unknown
  occurred_at: string
}

export interface WorkbenchPreferences {
  default_template_id: number | null
  active_template_id: number | null
  updated_at: string
}

export interface WorkbenchCurrentMode {
  template_id: number | null
  template_name: string | null
  default_module: string | null
  default_view_key: string | null
  default_query_json: unknown
}

export interface WorkbenchHomeData {
  current_mode: WorkbenchCurrentMode
  templates: WorkbenchTemplate[]
  pinned_shortcuts: WorkbenchShortcut[]
  recent_contexts: WorkbenchRecentContext[]
  dashboard_summary: DashboardData
}

export interface WorkbenchTemplateList {
  items: WorkbenchTemplate[]
}

export interface WorkbenchShortcutList {
  items: WorkbenchShortcut[]
}

export interface WorkbenchRecentList {
  items: WorkbenchRecentContext[]
  limit: number
  total: number
}

export interface WorkbenchApplyResult {
  template_applied: boolean
  template_id: number
  active_template_id: number | null
  default_template_id: number | null
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

export interface AiDerivationResultItem {
  id: number
  target_domain: string
  target_record_id: number
  derivation_type: string
  status: string
  model_name: string | null
  model_version: string | null
  generated_at: string | null
  failed_at: string | null
  content_json: unknown
  error_message: string | null
  created_at: string
}

export interface AiDerivationResultList {
  items: AiDerivationResultItem[]
}

export interface AlertResultItem {
  id: number
  source_domain: string
  source_record_id: number
  rule_code: string
  severity: string
  status: string
  title: string
  message: string
  explanation: string | null
  triggered_at: string
  viewed_at: string | null
  dismissed_at: string | null
  created_at: string
}

export interface AlertResultList {
  items: AlertResultItem[]
}

export async function fetchDashboard(): Promise<DashboardData> {
  return request<DashboardData>('/api/dashboard')
}

export async function fetchWorkbenchHome(): Promise<WorkbenchHomeData> {
  return request<WorkbenchHomeData>('/api/workbench/home')
}

export async function fetchWorkbenchTemplates(): Promise<WorkbenchTemplateList> {
  return request<WorkbenchTemplateList>('/api/workbench/templates')
}

export async function createWorkbenchTemplate(payload: Record<string, unknown>): Promise<WorkbenchTemplate> {
  return request<WorkbenchTemplate>('/api/workbench/templates', undefined, 'POST', payload)
}

export async function updateWorkbenchTemplate(
  templateId: number,
  payload: Record<string, unknown>,
): Promise<WorkbenchTemplate> {
  return request<WorkbenchTemplate>(`/api/workbench/templates/${templateId}`, undefined, 'PATCH', payload)
}

export async function applyWorkbenchTemplate(
  templateId: number,
  payload: Record<string, unknown>,
): Promise<WorkbenchApplyResult> {
  return request<WorkbenchApplyResult>(
    `/api/workbench/templates/${templateId}/apply`,
    undefined,
    'POST',
    payload,
  )
}

export async function fetchWorkbenchShortcuts(): Promise<WorkbenchShortcutList> {
  return request<WorkbenchShortcutList>('/api/workbench/shortcuts')
}

export async function createWorkbenchShortcut(payload: Record<string, unknown>): Promise<WorkbenchShortcut> {
  return request<WorkbenchShortcut>('/api/workbench/shortcuts', undefined, 'POST', payload)
}

export async function updateWorkbenchShortcut(
  shortcutId: number,
  payload: Record<string, unknown>,
): Promise<WorkbenchShortcut> {
  return request<WorkbenchShortcut>(`/api/workbench/shortcuts/${shortcutId}`, undefined, 'PATCH', payload)
}

export async function deleteWorkbenchShortcut(shortcutId: number): Promise<void> {
  return requestVoid(`/api/workbench/shortcuts/${shortcutId}`, 'DELETE')
}

export async function fetchWorkbenchRecent(): Promise<WorkbenchRecentList> {
  return request<WorkbenchRecentList>('/api/workbench/recent')
}

export async function fetchWorkbenchPreferences(): Promise<WorkbenchPreferences> {
  return request<WorkbenchPreferences>('/api/workbench/preferences')
}

export async function updateWorkbenchPreferences(
  payload: Record<string, unknown>,
): Promise<WorkbenchPreferences> {
  return request<WorkbenchPreferences>('/api/workbench/preferences', undefined, 'PATCH', payload)
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

export async function fetchAiDerivationList(
  params: Record<string, string>,
): Promise<AiDerivationResultList> {
  return request<AiDerivationResultList>('/api/ai-derivations', params)
}

export async function fetchAlertList(params: Record<string, string>): Promise<AlertResultList> {
  return request<AlertResultList>('/api/alerts', params)
}

export async function rerunHealthAiSummary(id: number): Promise<AiDerivationResultList> {
  return request<AiDerivationResultList>(`/api/health/${id}/ai/health-summary/rerun`, undefined, 'POST')
}

export async function rerunKnowledgeAiSummary(id: number): Promise<AiDerivationResultList> {
  return request<AiDerivationResultList>(`/api/knowledge/${id}/ai/knowledge-summary/rerun`, undefined, 'POST')
}

export async function markAlertViewed(id: number): Promise<AlertResultItem> {
  return request<AlertResultItem>(`/api/alerts/${id}/viewed`, undefined, 'POST')
}

export async function dismissAlert(id: number): Promise<AlertResultItem> {
  return request<AlertResultItem>(`/api/alerts/${id}/dismissed`, undefined, 'POST')
}

async function request<T>(
  path: string,
  params?: Record<string, string>,
  method = 'GET',
  body?: unknown,
): Promise<T> {
  const url = new URL(`${apiBaseUrl}${path}`)

  if (params) {
    for (const [key, value] of Object.entries(params)) {
      if (value) {
        url.searchParams.set(key, value)
      }
    }
  }

  const headers: Record<string, string> = {
    Accept: 'application/json',
  }
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }

  let response: Response
  try {
    response = await fetch(url.toString(), {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch {
    throw new ApiRequestError(
      'TraceFold API is unavailable. Check /api/healthz and VITE_API_BASE_URL.',
    )
  }

  let payload: ApiResponse<T>
  try {
    payload = (await response.json()) as ApiResponse<T>
  } catch {
    throw new ApiRequestError(
      'TraceFold API returned an invalid response. Check the API process and try again.',
      response.status,
    )
  }

  if (!response.ok || !payload.success || payload.data === null) {
    throw new ApiRequestError(payload.message || `Request failed with status ${response.status}.`, response.status)
  }

  return payload.data
}

async function requestVoid(path: string, method: 'DELETE'): Promise<void> {
  const url = new URL(`${apiBaseUrl}${path}`)
  let response: Response
  try {
    response = await fetch(url.toString(), {
      method,
      headers: {
        Accept: 'application/json',
      },
    })
  } catch {
    throw new ApiRequestError(
      'TraceFold API is unavailable. Check /api/healthz and VITE_API_BASE_URL.',
    )
  }

  if (response.status === 204) {
    return
  }

  let payload: ApiResponse<null>
  try {
    payload = (await response.json()) as ApiResponse<null>
  } catch {
    throw new ApiRequestError(
      'TraceFold API returned an invalid response. Check the API process and try again.',
      response.status,
    )
  }

  if (!response.ok || !payload.success) {
    throw new ApiRequestError(payload.message || `Request failed with status ${response.status}.`, response.status)
  }
}
