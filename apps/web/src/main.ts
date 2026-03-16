import './style.css'

import {
  fetchAiDerivationList,
  dismissAlert,
  fetchAlertList,
  fetchDashboard,
  fetchExpenseDetail,
  fetchExpenseList,
  fetchHealthDetail,
  fetchHealthList,
  fetchKnowledgeDetail,
  fetchKnowledgeList,
  markAlertViewed,
  fetchPendingDetail,
  fetchPendingList,
  rerunHealthAiSummary,
  rerunKnowledgeAiSummary,
} from './api.ts'
import type {
  AlertResultItem,
  AiDerivationResultItem,
  DashboardData,
  DashboardRecentActivity,
  ExpenseDetail,
  ExpenseListItem,
  HealthDetail,
  HealthListItem,
  KnowledgeDetail,
  KnowledgeListItem,
  PaginatedResponse,
  PendingDetail,
  PendingListItem,
  PendingListResponse,
} from './api.ts'

type NavSection = 'dashboard' | 'capture' | 'pending' | 'expense' | 'knowledge' | 'health'
type SortOrder = 'asc' | 'desc'

interface BaseListQuery {
  page: number
  pageSize: number
  sortBy: string
  sortOrder: SortOrder
  dateFrom: string
  dateTo: string
}

interface PendingListQuery extends BaseListQuery {
  status: string
  targetDomain: string
}

interface ExpenseListQuery extends BaseListQuery {
  category: string
  keyword: string
}

interface KnowledgeListQuery extends BaseListQuery {
  keyword: string
  hasSourceText: '' | 'true' | 'false'
}

interface HealthListQuery extends BaseListQuery {
  metricType: string
  keyword: string
  focusAlerts: boolean
}

type Route =
  | { kind: 'dashboard'; section: NavSection; pageTitle: string; documentTitle: string }
  | { kind: 'capture'; section: NavSection; pageTitle: string; documentTitle: string }
  | { kind: 'pending-list'; section: NavSection; pageTitle: string; documentTitle: string }
  | {
      kind: 'pending-detail'
      section: NavSection
      pageTitle: string
      documentTitle: string
      id: string
    }
  | { kind: 'expense-list'; section: NavSection; pageTitle: string; documentTitle: string }
  | {
      kind: 'expense-detail'
      section: NavSection
      pageTitle: string
      documentTitle: string
      id: string
    }
  | { kind: 'knowledge-list'; section: NavSection; pageTitle: string; documentTitle: string }
  | {
      kind: 'knowledge-detail'
      section: NavSection
      pageTitle: string
      documentTitle: string
      id: string
    }
  | { kind: 'health-list'; section: NavSection; pageTitle: string; documentTitle: string }
  | {
      kind: 'health-detail'
      section: NavSection
      pageTitle: string
      documentTitle: string
      id: string
    }
  | { kind: 'redirect'; to: string }

const appElement = document.querySelector<HTMLDivElement>('#app')

if (!appElement) {
  throw new Error('#app not found')
}

const app = appElement

let renderToken = 0

window.addEventListener('popstate', () => {
  void renderApp()
})

app.addEventListener('click', (event) => {
  handleClick(event)
})

app.addEventListener('submit', (event) => {
  handleSubmit(event)
})

void renderApp()

async function renderApp(): Promise<void> {
  const route = parseRoute(window.location.pathname)

  if (route.kind === 'redirect') {
    navigate(route.to, { replace: true })
    return
  }

  const currentToken = ++renderToken
  document.title = `TraceFold - ${route.documentTitle}`
  app.innerHTML = renderShell(route.section, renderLoadingPage(route.pageTitle))

  const pageHtml = await renderRoute(route)
  if (currentToken !== renderToken) {
    return
  }

  app.innerHTML = renderShell(route.section, pageHtml)
}

function parseRoute(pathname: string): Route {
  const parts = pathname.replace(/^\/+|\/+$/g, '').split('/').filter(Boolean)

  if (parts.length === 0) {
    return { kind: 'redirect', to: '/dashboard' }
  }

  if (parts.length === 1) {
    switch (parts[0]) {
      case 'dashboard':
        return {
          kind: 'dashboard',
          section: 'dashboard',
          pageTitle: 'Dashboard',
          documentTitle: 'Dashboard',
        }
      case 'capture':
        return { kind: 'capture', section: 'capture', pageTitle: 'Capture', documentTitle: 'Capture' }
      case 'pending':
        return { kind: 'pending-list', section: 'pending', pageTitle: 'Pending', documentTitle: 'Pending' }
      case 'expense':
        return { kind: 'expense-list', section: 'expense', pageTitle: 'Expenses', documentTitle: 'Expenses' }
      case 'knowledge':
        return {
          kind: 'knowledge-list',
          section: 'knowledge',
          pageTitle: 'Knowledge',
          documentTitle: 'Knowledge',
        }
      case 'health':
        return { kind: 'health-list', section: 'health', pageTitle: 'Health', documentTitle: 'Health' }
      default:
        return { kind: 'redirect', to: '/dashboard' }
    }
  }

  if (parts.length === 2) {
    if (parts[0] === 'pending') {
      return {
        kind: 'pending-detail',
        section: 'pending',
        pageTitle: 'Pending Detail',
        documentTitle: 'Pending Detail',
        id: parts[1],
      }
    }
    if (parts[0] === 'expense') {
      return {
        kind: 'expense-detail',
        section: 'expense',
        pageTitle: 'Expense Detail',
        documentTitle: 'Expense Detail',
        id: parts[1],
      }
    }
    if (parts[0] === 'knowledge') {
      return {
        kind: 'knowledge-detail',
        section: 'knowledge',
        pageTitle: 'Knowledge Detail',
        documentTitle: 'Knowledge Detail',
        id: parts[1],
      }
    }
    if (parts[0] === 'health') {
      return {
        kind: 'health-detail',
        section: 'health',
        pageTitle: 'Health Detail',
        documentTitle: 'Health Detail',
        id: parts[1],
      }
    }
  }

  return { kind: 'redirect', to: '/dashboard' }
}

async function renderRoute(route: Exclude<Route, { kind: 'redirect' }>): Promise<string> {
  switch (route.kind) {
    case 'dashboard':
      return renderDashboardPage()
    case 'capture':
      return renderPlaceholderPage(route.pageTitle, 'Capture view is not implemented yet.')
    case 'pending-list':
      return renderPendingListPage()
    case 'pending-detail':
      return renderPendingDetailPage(route.id)
    case 'expense-list':
      return renderExpenseListPage()
    case 'expense-detail':
      return renderExpenseDetailPage(route.id)
    case 'knowledge-list':
      return renderKnowledgeListPage()
    case 'knowledge-detail':
      return renderKnowledgeDetailPage(route.id)
    case 'health-list':
      return renderHealthListPage()
    case 'health-detail':
      return renderHealthDetailPage(route.id)
  }
}

async function renderDashboardPage(): Promise<string> {
  try {
    const dashboard = await fetchDashboard()
    return renderDashboardView(dashboard)
  } catch (error) {
    return renderDashboardView(null, toErrorMessage(error))
  }
}

async function renderPendingListPage(): Promise<string> {
  const query = parsePendingListQuery(new URLSearchParams(window.location.search))

  try {
    const response = await fetchPendingList(buildPendingApiParams(query))
    return renderPendingListView(query, response)
  } catch (error) {
    return renderPendingListView(query, null, toErrorMessage(error))
  }
}

async function renderPendingDetailPage(id: string): Promise<string> {
  try {
    const detail = await fetchPendingDetail(id)
    return renderPendingDetailView(detail)
  } catch (error) {
    return renderDetailErrorView('Pending Detail', '/pending', 'Pending', toErrorMessage(error))
  }
}

async function renderExpenseListPage(): Promise<string> {
  const query = parseExpenseListQuery(new URLSearchParams(window.location.search))

  try {
    const response = await fetchExpenseList(buildExpenseApiParams(query))
    return renderExpenseListView(query, response)
  } catch (error) {
    return renderExpenseListView(query, null, toErrorMessage(error))
  }
}

async function renderExpenseDetailPage(id: string): Promise<string> {
  try {
    const detail = await fetchExpenseDetail(id)
    return renderExpenseDetailView(detail)
  } catch (error) {
    return renderDetailErrorView('Expense Detail', '/expense', 'Expenses', toErrorMessage(error))
  }
}

async function renderKnowledgeListPage(): Promise<string> {
  const query = parseKnowledgeListQuery(new URLSearchParams(window.location.search))

  try {
    const response = await fetchKnowledgeList(buildKnowledgeApiParams(query))
    return renderKnowledgeListView(query, response)
  } catch (error) {
    return renderKnowledgeListView(query, null, toErrorMessage(error))
  }
}

async function renderKnowledgeDetailPage(id: string): Promise<string> {
  const [detailResult, aiResult] = await Promise.allSettled([
    fetchKnowledgeDetail(id),
    fetchAiDerivationList({ target_domain: 'knowledge', target_record_id: id }),
  ])

  if (detailResult.status === 'rejected') {
    return renderDetailErrorView('Knowledge Detail', '/knowledge', 'Knowledge', toErrorMessage(detailResult.reason))
  }

  return renderKnowledgeDetailView(
    detailResult.value,
    aiResult.status === 'fulfilled' ? getKnowledgeSummaryDerivation(aiResult.value.items) : null,
    aiResult.status === 'rejected' ? toErrorMessage(aiResult.reason) : undefined,
  )
}

async function renderHealthListPage(): Promise<string> {
  const query = parseHealthListQuery(new URLSearchParams(window.location.search))

  const [healthResult, alertResult] = await Promise.allSettled([
    fetchHealthList(buildHealthApiParams(query)),
    fetchAlertList({ source_domain: 'health' }),
  ])

  return renderHealthListView(
    query,
    healthResult.status === 'fulfilled' ? healthResult.value : null,
    alertResult.status === 'fulfilled' ? alertResult.value.items : [],
    healthResult.status === 'rejected' ? toErrorMessage(healthResult.reason) : undefined,
    alertResult.status === 'rejected' ? toErrorMessage(alertResult.reason) : undefined,
  )
}

async function renderHealthDetailPage(id: string): Promise<string> {
  const [detailResult, alertResult, aiResult] = await Promise.allSettled([
    fetchHealthDetail(id),
    fetchAlertList({ source_domain: 'health', source_record_id: id }),
    fetchAiDerivationList({ target_domain: 'health', target_record_id: id }),
  ])

  if (detailResult.status === 'rejected') {
    return renderDetailErrorView('Health Detail', '/health', 'Health', toErrorMessage(detailResult.reason))
  }

  return renderHealthDetailView(
    detailResult.value,
    alertResult.status === 'fulfilled' ? alertResult.value.items : [],
    aiResult.status === 'fulfilled' ? getHealthSummaryDerivation(aiResult.value.items) : null,
    alertResult.status === 'rejected' ? toErrorMessage(alertResult.reason) : undefined,
    aiResult.status === 'rejected' ? toErrorMessage(aiResult.reason) : undefined,
  )
}

function handleClick(event: MouseEvent): void {
  const target = event.target instanceof Element ? event.target : null

  if (!target) {
    return
  }

  const aiActionButton = target.closest<HTMLButtonElement>('[data-ai-action]')
  if (aiActionButton) {
    const action = aiActionButton.dataset.aiAction
    const recordId = Number.parseInt(aiActionButton.dataset.recordId || '', 10)
    if (
      (action === 'rerun-health-summary' || action === 'rerun-knowledge-summary') &&
      Number.isFinite(recordId)
    ) {
      event.preventDefault()
      void handleAiDerivationAction(recordId, action)
    }
    return
  }

  const alertActionButton = target.closest<HTMLButtonElement>('[data-alert-action]')
  if (alertActionButton) {
    const action = alertActionButton.dataset.alertAction
    const alertId = Number.parseInt(alertActionButton.dataset.alertId || '', 10)
    if ((action === 'viewed' || action === 'dismissed') && Number.isFinite(alertId)) {
      event.preventDefault()
      void handleAlertAction(alertId, action)
    }
    return
  }

  const navLink = target.closest<HTMLElement>('[data-nav="true"]')
  if (navLink) {
    const href = navLink.getAttribute('href')
    if (href) {
      event.preventDefault()
      navigate(href)
    }
    return
  }

  const paginationButton = target.closest<HTMLButtonElement>('[data-page-target]')
  if (paginationButton) {
    const pageTarget = paginationButton.dataset.pageTarget
    const path = paginationButton.dataset.path
    if (pageTarget && path) {
      event.preventDefault()
      const params = new URLSearchParams(window.location.search)
      params.set('page', pageTarget)
      navigate(buildUrl(path, params))
    }
    return
  }

  const resetButton = target.closest<HTMLButtonElement>('[data-reset-path]')
  if (resetButton) {
    const path = resetButton.dataset.resetPath
    if (path) {
      event.preventDefault()
      navigate(path)
    }
    return
  }

  const retryButton = target.closest<HTMLButtonElement>('[data-retry="true"]')
  if (retryButton) {
    event.preventDefault()
    void renderApp()
  }
}

function handleSubmit(event: SubmitEvent): void {
  const form = event.target instanceof HTMLFormElement ? event.target : null

  if (!form || form.dataset.listForm !== 'true') {
    return
  }

  event.preventDefault()

  const path = form.dataset.path
  if (!path) {
    return
  }

  const formData = new FormData(form)
  const params = new URLSearchParams()

  for (const [key, rawValue] of formData.entries()) {
    const value = String(rawValue).trim()
    if (value) {
      params.set(key, value)
    }
  }

  params.set('page', '1')
  navigate(buildUrl(path, params))
}

function navigate(path: string, options: { replace?: boolean } = {}): void {
  if (options.replace) {
    window.history.replaceState(null, '', path)
  } else {
    window.history.pushState(null, '', path)
  }

  void renderApp()
}

function renderShell(activeSection: NavSection, content: string): string {
  return `
    <div class="workspace-shell">
      <header class="workspace-header">
        <div class="workspace-brand">
          <span class="brand-mark">TraceFold</span>
          <span class="brand-caption">Local-first workspace</span>
        </div>
        <nav class="workspace-nav" aria-label="Primary">
          ${renderNavLink('Dashboard', '/dashboard', activeSection === 'dashboard')}
          ${renderNavLink('Capture', '/capture', activeSection === 'capture')}
          ${renderNavLink('Pending', '/pending', activeSection === 'pending')}
          ${renderNavLink('Expense', '/expense', activeSection === 'expense')}
          ${renderNavLink('Knowledge', '/knowledge', activeSection === 'knowledge')}
          ${renderNavLink('Health', '/health', activeSection === 'health')}
        </nav>
      </header>
      <main class="workspace-main">
        ${content}
      </main>
    </div>
  `
}

function renderNavLink(label: string, href: string, isActive: boolean): string {
  return `
    <a
      class="nav-link${isActive ? ' is-active' : ''}"
      href="${href}"
      data-nav="true"
    >
      ${escapeHtml(label)}
    </a>
  `
}

function renderLoadingPage(title: string): string {
  return `
    <section class="page-header">
      <h1>${escapeHtml(title)}</h1>
    </section>
    ${renderLoadingState()}
  `
}

function renderPlaceholderPage(title: string, message: string): string {
  return `
    <section class="page-header">
      <h1>${escapeHtml(title)}</h1>
    </section>
    <section class="panel page-section">
      <p class="placeholder-copy">${escapeHtml(message)}</p>
    </section>
  `
}

async function handleAiDerivationAction(
  recordId: number,
  action: 'rerun-health-summary' | 'rerun-knowledge-summary',
): Promise<void> {
  try {
    if (action === 'rerun-health-summary') {
      await rerunHealthAiSummary(recordId)
    } else {
      await rerunKnowledgeAiSummary(recordId)
    }
    await renderApp()
  } catch (error) {
    window.alert(toErrorMessage(error))
  }
}

async function handleAlertAction(alertId: number, action: 'viewed' | 'dismissed'): Promise<void> {
  try {
    if (action === 'viewed') {
      await markAlertViewed(alertId)
    } else {
      await dismissAlert(alertId)
    }
    await renderApp()
  } catch (error) {
    window.alert(toErrorMessage(error))
  }
}

function renderDashboardView(dashboard: DashboardData | null, errorMessage?: string): string {
  return `
    <section class="page-header">
      <h1>Dashboard</h1>
      <p class="page-copy">Workspace overview for pending review, formal records, and recent context.</p>
    </section>
    ${
      errorMessage
        ? renderErrorState(errorMessage)
        : dashboard
          ? `
              ${renderPendingSummarySection(dashboard)}
              ${renderAlertSummarySection(dashboard)}
              ${renderQuickLinksSection(dashboard)}
              ${renderFormalSummariesSection(dashboard)}
              ${renderRecentActivitySection(dashboard)}
            `
          : renderEmptyState('Dashboard data is not available.')
    }
  `
}

function renderAlertSummarySection(dashboard: DashboardData): string {
  return `
    <section class="panel page-section">
      <div class="section-header">
        <h2>Rule Alerts</h2>
      </div>
      <div class="summary-grid">
        <article class="summary-card summary-card--alert">
          <div class="summary-card__header">
            <h3>Open health alerts</h3>
            <a class="record-action" href="${escapeHtml(dashboard.alert_summary.href)}" data-nav="true">View alerts</a>
          </div>
          <div class="summary-stack">
            <p class="summary-value">${dashboard.alert_summary.open_count}</p>
            ${
              dashboard.alert_summary.recent_open_items.length > 0
                ? `
                    <div class="alert-summary-list">
                      ${dashboard.alert_summary.recent_open_items
                        .map(
                          (item) => `
                            <article class="alert-summary-item">
                              <div class="record-badges">
                                ${renderSeverityBadge(item.severity)}
                              </div>
                              <p class="alert-summary-item__title">${escapeHtml(item.title)}</p>
                              <p class="section-copy">${escapeHtml(item.message)}</p>
                              <div class="alert-summary-item__footer">
                                <span class="record-meta">${escapeHtml(formatDateTime(item.triggered_at))}</span>
                                <a class="record-action" href="${escapeHtml(item.href)}" data-nav="true">Open record</a>
                              </div>
                            </article>
                          `,
                        )
                        .join('')}
                    </div>
                  `
                : '<p class="section-copy">No open rule alerts.</p>'
            }
          </div>
        </article>
      </div>
    </section>
  `
}

function renderPendingSummarySection(dashboard: DashboardData): string {
  return `
    <section class="panel page-section">
      <div class="section-header">
        <h2>Pending Summary</h2>
      </div>
      <div class="summary-grid">
        <article class="summary-card">
          <div class="summary-card__header">
            <h3>Open pending items</h3>
            <a class="record-action" href="${escapeHtml(dashboard.pending_summary.href)}" data-nav="true">View pending</a>
          </div>
          <div class="summary-stack">
            <p class="summary-value">${dashboard.pending_summary.open_count}</p>
            <div class="field-grid">
              ${renderField('Opened in last 7 days', String(dashboard.pending_summary.opened_in_last_7_days))}
              ${renderField('Resolved in last 7 days', String(dashboard.pending_summary.resolved_in_last_7_days))}
            </div>
            <div>
              <span class="field__label">Open by target domain</span>
              ${renderDataList(
                Object.entries(dashboard.pending_summary.open_count_by_target_domain).map(([domain, count]) => ({
                  label: formatDomainLabel(domain),
                  value: String(count),
                })),
                'No open pending items by domain.',
              )}
            </div>
          </div>
        </article>
      </div>
    </section>
  `
}

function renderQuickLinksSection(dashboard: DashboardData): string {
  return `
    <section class="panel page-section">
      <div class="section-header">
        <h2>Quick Links</h2>
      </div>
      ${
        dashboard.quick_links.length > 0
          ? `
              <div class="summary-grid">
                ${dashboard.quick_links
                  .map(
                    (link) => `
                      <a class="quick-link-card" href="${escapeHtml(link.href)}" data-nav="true">
                        <span class="quick-link-card__label">${escapeHtml(link.label)}</span>
                        <span class="quick-link-card__meta">Open context</span>
                      </a>
                    `,
                  )
                  .join('')}
              </div>
            `
          : renderEmptyState('No quick links available.')
      }
    </section>
  `
}

function renderFormalSummariesSection(dashboard: DashboardData): string {
  return `
    <section class="panel page-section">
      <div class="section-header">
        <h2>Expense / Knowledge / Health Summaries</h2>
      </div>
      <div class="summary-grid">
        <article class="summary-card">
          <div class="summary-card__header">
            <h3>Expense</h3>
            <a class="record-action" href="${escapeHtml(dashboard.expense_summary.href)}" data-nav="true">View expenses</a>
          </div>
          <div class="summary-stack">
            ${renderField('Created this month', String(dashboard.expense_summary.created_in_current_month))}
            ${renderField(
              'Latest expense',
              dashboard.expense_summary.latest_expense_created_at
                ? formatDateTime(dashboard.expense_summary.latest_expense_created_at)
                : null,
              true,
            )}
            <div>
              <span class="field__label">Amount by currency</span>
              ${renderDataList(
                Object.entries(dashboard.expense_summary.amount_by_currency_current_month).map(([currency, amount]) => ({
                  label: currency,
                  value: amount,
                })),
                'No expense amounts for the current month.',
              )}
            </div>
          </div>
        </article>
        <article class="summary-card">
          <div class="summary-card__header">
            <h3>Knowledge</h3>
            <a class="record-action" href="${escapeHtml(dashboard.knowledge_summary.href)}" data-nav="true">View knowledge</a>
          </div>
          <div class="summary-stack">
            <div class="field-grid">
              ${renderField('Created in last 7 days', String(dashboard.knowledge_summary.created_in_last_7_days))}
              ${renderField('Created in last 30 days', String(dashboard.knowledge_summary.created_in_last_30_days))}
            </div>
            ${renderField(
              'Latest knowledge entry',
              dashboard.knowledge_summary.latest_knowledge_created_at
                ? formatDateTime(dashboard.knowledge_summary.latest_knowledge_created_at)
                : null,
              true,
            )}
          </div>
        </article>
        <article class="summary-card">
          <div class="summary-card__header">
            <h3>Health</h3>
            <a class="record-action" href="${escapeHtml(dashboard.health_summary.href)}" data-nav="true">View health</a>
          </div>
          <div class="summary-stack">
            ${renderField('Created in last 7 days', String(dashboard.health_summary.created_in_last_7_days))}
            ${renderField(
              'Latest health record',
              dashboard.health_summary.latest_health_created_at
                ? formatDateTime(dashboard.health_summary.latest_health_created_at)
                : null,
              true,
            )}
            <div>
              <span class="field__label">Recent metric types</span>
              ${renderInlineList(
                dashboard.health_summary.recent_metric_types.map((metricType) => formatDomainLabel(metricType)),
                'No recent metric types.',
              )}
            </div>
          </div>
        </article>
      </div>
    </section>
  `
}

function renderRecentActivitySection(dashboard: DashboardData): string {
  return `
    <section class="panel page-section">
      <div class="section-header">
        <h2>Recent Activity</h2>
      </div>
      ${
        dashboard.recent_activity.length > 0
          ? renderRecentActivityRecords(dashboard.recent_activity)
          : renderEmptyState('No recent activity available.')
      }
    </section>
  `
}

function renderPendingListView(
  query: PendingListQuery,
  response: PendingListResponse | null,
  errorMessage?: string,
): string {
  return `
    <section class="page-header">
      <h1>Pending</h1>
      <p class="page-copy">Minimal read view for open and resolved pending items.</p>
    </section>
    ${renderPendingFilters(query)}
    <section class="panel page-section">
      <div class="section-header">
        <h2>List</h2>
      </div>
      ${
        response?.next_pending_item_id
          ? `<p class="section-copy">Next to review uses the earliest open pending item rule. Current hint: #${response.next_pending_item_id}.</p>`
          : ''
      }
      ${
        errorMessage
          ? renderErrorState(errorMessage)
          : response && response.items.length > 0
            ? renderPendingRecords(response.items)
            : renderEmptyState('No pending items found.')
      }
    </section>
    ${renderPagination('/pending', response?.page ?? query.page, response?.page_size ?? query.pageSize, response?.total ?? 0)}
  `
}

function renderPendingDetailView(detail: PendingDetail): string {
  return `
    <section class="page-header">
      <a class="back-link" href="/pending" data-nav="true">Back to Pending</a>
      <h1>Pending Detail</h1>
    </section>
    <section class="panel page-section">
      <div class="section-header">
        <h2>Detail</h2>
      </div>
      <div class="field-grid">
        ${renderField('ID', String(detail.id))}
        ${renderField('Status', formatStatusLabel(detail.status))}
        ${renderField('Target Domain', formatDomainLabel(detail.target_domain))}
        ${renderField('Created At', formatDateTime(detail.created_at))}
        ${renderField('Resolved At', detail.resolved_at ? formatDateTime(detail.resolved_at) : null)}
        ${renderField('source_capture_id', String(detail.source_capture_id))}
        ${renderField('parse_result_id', String(detail.parse_result_id))}
        ${renderField('Reason', detail.reason, true)}
      </div>
    </section>
    <section class="panel page-section">
      <div class="section-header">
        <h2>Proposed Payload</h2>
      </div>
      ${renderTextBlock(formatJson(detail.proposed_payload_json))}
    </section>
    <section class="panel page-section">
      <div class="section-header">
        <h2>Corrected Payload</h2>
      </div>
      ${renderTextBlock(formatJson(detail.corrected_payload_json))}
    </section>
  `
}

function renderExpenseListView(
  query: ExpenseListQuery,
  response: PaginatedResponse<ExpenseListItem> | null,
  errorMessage?: string,
): string {
  return `
    <section class="page-header">
      <h1>Expenses</h1>
    </section>
    ${renderExpenseFilters(query)}
    <section class="panel page-section">
      <div class="section-header">
        <h2>List</h2>
      </div>
      ${
        errorMessage
          ? renderErrorState(errorMessage)
          : response && response.items.length > 0
            ? renderExpenseRecords(response.items)
            : renderEmptyState('No expense records found.')
      }
    </section>
    ${renderPagination('/expense', response?.page ?? query.page, response?.page_size ?? query.pageSize, response?.total ?? 0)}
  `
}

function renderKnowledgeListView(
  query: KnowledgeListQuery,
  response: PaginatedResponse<KnowledgeListItem> | null,
  errorMessage?: string,
): string {
  return `
    <section class="page-header">
      <h1>Knowledge</h1>
    </section>
    ${renderKnowledgeFilters(query)}
    <section class="panel page-section">
      <div class="section-header">
        <h2>List</h2>
      </div>
      ${
        errorMessage
          ? renderErrorState(errorMessage)
          : response && response.items.length > 0
            ? renderKnowledgeRecords(response.items)
            : renderEmptyState('No knowledge entries found.')
      }
    </section>
    ${renderPagination(
      '/knowledge',
      response?.page ?? query.page,
      response?.page_size ?? query.pageSize,
      response?.total ?? 0,
    )}
  `
}

function renderHealthListView(
  query: HealthListQuery,
  response: PaginatedResponse<HealthListItem> | null,
  alerts: AlertResultItem[],
  errorMessage?: string,
  alertsErrorMessage?: string,
): string {
  const factsSection = `
    <section class="panel page-section">
      <div class="section-header">
        <h2>Formal Records</h2>
      </div>
      ${
        errorMessage
          ? renderErrorState(errorMessage)
          : response && response.items.length > 0
            ? renderHealthRecords(response.items)
            : renderEmptyState('No health records found.')
      }
    </section>
  `
  const alertsSection = renderHealthAlertSection(
    alerts,
    alertsErrorMessage,
    {
      heading: 'Rule Alerts',
      emptyMessage: 'No rule alerts for health records.',
      emphasize: query.focusAlerts,
    },
  )

  return `
    <section class="page-header">
      <h1>Health</h1>
    </section>
    ${renderHealthFilters(query)}
    ${factsSection}
    ${alertsSection}
    ${renderPagination('/health', response?.page ?? query.page, response?.page_size ?? query.pageSize, response?.total ?? 0)}
  `
}

function renderExpenseDetailView(detail: ExpenseDetail): string {
  return `
    <section class="page-header">
      <a class="back-link" href="/expense" data-nav="true">Back to Expenses</a>
      <h1>Expense Detail</h1>
    </section>
    <section class="panel page-section">
      <div class="section-header">
        <h2>Detail</h2>
      </div>
      <div class="field-grid">
        ${renderField('ID', String(detail.id))}
        ${renderField('Created At', formatDateTime(detail.created_at))}
        ${renderField('Amount', detail.amount)}
        ${renderField('Currency', detail.currency)}
        ${renderField('Category', detail.category)}
        ${renderField('Note', detail.note, true)}
      </div>
    </section>
    ${renderSourceSection(detail.source_capture_id, detail.source_pending_id)}
  `
}

function renderKnowledgeDetailView(
  detail: KnowledgeDetail,
  aiSummary: AiDerivationResultItem | null,
  aiErrorMessage?: string,
): string {
  return `
    <section class="page-header">
      <a class="back-link" href="/knowledge" data-nav="true">Back to Knowledge</a>
      <h1>Knowledge Detail</h1>
    </section>
    <section class="panel page-section">
      <div class="section-header">
        <h2>Content</h2>
      </div>
      <div class="field-grid">
        ${renderField('Created At', formatDateTime(detail.created_at))}
        ${renderField('Title', detail.title, true)}
        ${renderField('Content', detail.content, true)}
      </div>
    </section>
    <section class="panel page-section">
      <div class="section-header">
        <h2>Source</h2>
      </div>
      <div class="field-grid">
        ${renderField('source_capture_id', String(detail.source_capture_id))}
        ${renderField('source_pending_id', detail.source_pending_id === null ? null : String(detail.source_pending_id))}
        ${renderField('source_text', detail.source_text, true)}
      </div>
    </section>
    ${renderKnowledgeAiSummarySection(detail.id, aiSummary, aiErrorMessage)}
  `
}

function renderHealthDetailView(
  detail: HealthDetail,
  alerts: AlertResultItem[],
  aiSummary: AiDerivationResultItem | null,
  alertsErrorMessage?: string,
  aiErrorMessage?: string,
): string {
  return `
    <section class="page-header">
      <a class="back-link" href="/health" data-nav="true">Back to Health</a>
      <h1>Health Detail</h1>
    </section>
    <section class="panel page-section">
      <div class="section-header">
        <h2>Detail</h2>
      </div>
      <div class="field-grid">
        ${renderField('ID', String(detail.id))}
        ${renderField('Created At', formatDateTime(detail.created_at))}
        ${renderField('Metric Type', detail.metric_type)}
        ${renderField('Value Text', detail.value_text, true)}
        ${renderField('Note', detail.note, true)}
      </div>
    </section>
    ${renderHealthAlertSection(alerts, alertsErrorMessage, {
      heading: 'Rule Alerts',
      emptyMessage: 'No rule alerts for this health record.',
      sourceRecordId: detail.id,
    })}
    ${renderSourceSection(detail.source_capture_id, detail.source_pending_id)}
    ${renderHealthAiSummarySection(detail.id, aiSummary, aiErrorMessage)}
  `
}

function renderDetailErrorView(title: string, backPath: string, backLabel: string, message: string): string {
  return `
    <section class="page-header">
      <a class="back-link" href="${backPath}" data-nav="true">Back to ${escapeHtml(backLabel)}</a>
      <h1>${escapeHtml(title)}</h1>
    </section>
    ${renderErrorState(message)}
  `
}

function renderPendingFilters(query: PendingListQuery): string {
  return renderFilterSection(
    '/pending',
    `
      ${renderDateInput('date_from', 'Date From', query.dateFrom)}
      ${renderDateInput('date_to', 'Date To', query.dateTo)}
      ${renderSelectInput('status', 'Status', query.status, [
        ['open', 'open'],
        ['confirmed', 'confirmed'],
        ['discarded', 'discarded'],
        ['forced', 'forced'],
      ])}
      ${renderSelectInput('target_domain', 'Target Domain', query.targetDomain, [
        ['', 'All'],
        ['expense', 'expense'],
        ['knowledge', 'knowledge'],
        ['health', 'health'],
        ['unknown', 'unknown'],
      ])}
      ${renderSelectInput('sort_by', 'Sort By', query.sortBy, [
        ['created_at', 'created_at'],
        ['resolved_at', 'resolved_at'],
        ['status', 'status'],
        ['target_domain', 'target_domain'],
      ])}
      ${renderSelectInput('sort_order', 'Sort Order', query.sortOrder, [
        ['desc', 'desc'],
        ['asc', 'asc'],
      ])}
      ${renderSelectInput('page_size', 'Page Size', String(query.pageSize), [
        ['20', '20'],
        ['50', '50'],
        ['100', '100'],
      ])}
    `,
  )
}

function renderExpenseFilters(query: ExpenseListQuery): string {
  return renderFilterSection(
    '/expense',
    `
      ${renderDateInput('date_from', 'Date From', query.dateFrom)}
      ${renderDateInput('date_to', 'Date To', query.dateTo)}
      ${renderTextInput('category', 'Category', query.category)}
      ${renderTextInput('keyword', 'Keyword', query.keyword)}
      ${renderSelectInput('sort_by', 'Sort By', query.sortBy, [
        ['created_at', 'created_at'],
        ['amount', 'amount'],
      ])}
      ${renderSelectInput('sort_order', 'Sort Order', query.sortOrder, [
        ['desc', 'desc'],
        ['asc', 'asc'],
      ])}
      ${renderSelectInput('page_size', 'Page Size', String(query.pageSize), [
        ['20', '20'],
        ['50', '50'],
        ['100', '100'],
      ])}
    `,
  )
}

function renderKnowledgeFilters(query: KnowledgeListQuery): string {
  return renderFilterSection(
    '/knowledge',
    `
      ${renderDateInput('date_from', 'Date From', query.dateFrom)}
      ${renderDateInput('date_to', 'Date To', query.dateTo)}
      ${renderTextInput('keyword', 'Keyword', query.keyword)}
      ${renderSelectInput('has_source_text', 'Has Source Text', query.hasSourceText, [
        ['', 'All'],
        ['true', 'Yes'],
        ['false', 'No'],
      ])}
      ${renderSelectInput('sort_by', 'Sort By', query.sortBy, [
        ['created_at', 'created_at'],
        ['title', 'title'],
      ])}
      ${renderSelectInput('sort_order', 'Sort Order', query.sortOrder, [
        ['desc', 'desc'],
        ['asc', 'asc'],
      ])}
      ${renderSelectInput('page_size', 'Page Size', String(query.pageSize), [
        ['20', '20'],
        ['50', '50'],
        ['100', '100'],
      ])}
    `,
  )
}

function renderHealthFilters(query: HealthListQuery): string {
  return renderFilterSection(
    '/health',
    `
      ${renderDateInput('date_from', 'Date From', query.dateFrom)}
      ${renderDateInput('date_to', 'Date To', query.dateTo)}
      ${renderTextInput('metric_type', 'Metric Type', query.metricType)}
      ${renderTextInput('keyword', 'Keyword', query.keyword)}
      ${renderSelectInput('sort_by', 'Sort By', query.sortBy, [
        ['created_at', 'created_at'],
        ['metric_type', 'metric_type'],
      ])}
      ${renderSelectInput('sort_order', 'Sort Order', query.sortOrder, [
        ['desc', 'desc'],
        ['asc', 'asc'],
      ])}
      ${renderSelectInput('page_size', 'Page Size', String(query.pageSize), [
        ['20', '20'],
        ['50', '50'],
        ['100', '100'],
      ])}
    `,
  )
}

function renderFilterSection(path: string, controls: string): string {
  return `
    <section class="panel page-section">
      <div class="section-header">
        <h2>Filters</h2>
      </div>
      <form class="filter-form" data-list-form="true" data-path="${path}">
        <div class="filter-grid">
          ${controls}
        </div>
        <div class="filter-actions">
          <button class="primary-button" type="submit">Apply filters</button>
          <button class="secondary-button" type="button" data-reset-path="${path}">Reset</button>
        </div>
      </form>
    </section>
  `
}

function renderPendingRecords(items: PendingListItem[]): string {
  return items
    .map(
      (item) => `
        <article class="record-card${item.is_next_to_review ? ' record-card--priority' : ''}">
          <div class="record-card__header">
            <div class="record-card__title-group">
              <h3>Pending #${item.id}</h3>
              <span class="record-meta">${escapeHtml(formatDateTime(item.created_at))}</span>
            </div>
            <a class="record-action" href="/pending/${item.id}" data-nav="true">Open</a>
          </div>
          <div class="record-badges">
            ${renderBadge(formatStatusLabel(item.status))}
            ${renderBadge(formatDomainLabel(item.target_domain), true)}
            ${item.has_corrected_payload ? renderBadge('Has corrected payload') : ''}
            ${item.is_next_to_review ? renderBadge('Next to review') : ''}
          </div>
          <div class="field-grid">
            ${renderField('Reason Preview', item.reason_preview, true)}
            ${renderField('source_capture_id', String(item.source_capture_id))}
            ${renderField('Has Corrected Payload', formatBoolean(item.has_corrected_payload))}
          </div>
        </article>
      `,
    )
    .join('')
}

function renderExpenseRecords(items: ExpenseListItem[]): string {
  return items
    .map(
      (item) => `
        <article class="record-card">
          <div class="record-card__header">
            <div class="record-card__title-group">
              <h3>${escapeHtml(`${item.amount} ${item.currency}`)}</h3>
              <span class="record-meta">${escapeHtml(formatDateTime(item.created_at))}</span>
            </div>
            <a class="record-action" href="/expense/${item.id}" data-nav="true">Open</a>
          </div>
          <div class="field-grid">
            ${renderField('Category', item.category)}
            ${renderField('Note Preview', item.note_preview, true)}
            ${renderField('Has Source Pending', formatBoolean(item.has_source_pending))}
          </div>
        </article>
      `,
    )
    .join('')
}

function renderKnowledgeRecords(items: KnowledgeListItem[]): string {
  return items
    .map(
      (item) => `
        <article class="record-card">
          <div class="record-card__header">
            <div class="record-card__title-group">
              <h3>${escapeHtml(item.display_title)}</h3>
              <span class="record-meta">${escapeHtml(formatDateTime(item.created_at))}</span>
            </div>
            <a class="record-action" href="/knowledge/${item.id}" data-nav="true">Open</a>
          </div>
          <div class="field-grid">
            ${renderField('Content Preview', item.content_preview, true)}
            ${renderField('Has Source Text', formatBoolean(item.has_source_text))}
            ${renderField('Has Source Pending', formatBoolean(item.has_source_pending))}
          </div>
        </article>
      `,
    )
    .join('')
}

function renderHealthRecords(items: HealthListItem[]): string {
  return items
    .map(
      (item) => `
        <article class="record-card">
          <div class="record-card__header">
            <div class="record-card__title-group">
              <h3>${escapeHtml(item.metric_type)}</h3>
              <span class="record-meta">${escapeHtml(formatDateTime(item.created_at))}</span>
            </div>
            <a class="record-action" href="/health/${item.id}" data-nav="true">Open</a>
          </div>
          <div class="field-grid">
            ${renderField('Value Text Preview', item.value_text_preview)}
            ${renderField('Note Preview', item.note_preview, true)}
            ${renderField('Has Source Pending', formatBoolean(item.has_source_pending))}
          </div>
        </article>
      `,
    )
    .join('')
}

function renderHealthAlertSection(
  alerts: AlertResultItem[],
  errorMessage: string | undefined,
  options: {
    heading: string
    emptyMessage: string
    sourceRecordId?: number
    emphasize?: boolean
  },
): string {
  const filteredAlerts =
    typeof options.sourceRecordId === 'number'
      ? alerts.filter((item) => item.source_record_id === options.sourceRecordId)
      : alerts

  return `
    <section class="panel page-section${options.emphasize ? ' page-section--alert-focus' : ''}" id="health-alerts">
      <div class="section-header">
        <h2>${escapeHtml(options.heading)}</h2>
      </div>
      ${
        errorMessage
          ? renderErrorState(errorMessage)
          : filteredAlerts.length > 0
            ? renderAlertRecords(filteredAlerts)
            : renderEmptyState(options.emptyMessage)
      }
    </section>
  `
}

function renderHealthAiSummarySection(
  healthId: number,
  aiSummary: AiDerivationResultItem | null,
  errorMessage?: string,
): string {
  return `
    <section class="panel page-section page-section--ai">
      <div class="section-header section-header--with-badge">
        <h2>AI Summary</h2>
        ${renderAiLabelBadge()}
      </div>
      ${
        errorMessage
          ? renderErrorState(errorMessage)
          : aiSummary
            ? renderHealthAiSummaryContent(aiSummary, healthId)
            : `
                <div class="status-panel is-empty">
                  <p class="status-copy">AI Summary is only available for subjective health records.</p>
                  <button class="secondary-button" type="button" data-ai-action="rerun-health-summary" data-record-id="${healthId}">
                    Generate AI Summary
                  </button>
                </div>
              `
      }
    </section>
  `
}

function renderKnowledgeAiSummarySection(
  knowledgeId: number,
  aiSummary: AiDerivationResultItem | null,
  errorMessage?: string,
): string {
  return `
    <section class="panel page-section page-section--ai">
      <div class="section-header section-header--with-badge">
        <h2>AI Summary</h2>
        ${renderAiLabelBadge()}
      </div>
      ${
        errorMessage
          ? renderErrorState(errorMessage)
          : aiSummary
            ? renderKnowledgeAiSummaryContent(aiSummary, knowledgeId)
            : `
                <div class="status-panel is-empty">
                  <p class="status-copy">AI Summary is not available for this knowledge entry yet.</p>
                  <button class="secondary-button" type="button" data-ai-action="rerun-knowledge-summary" data-record-id="${knowledgeId}">
                    Generate AI Summary
                  </button>
                </div>
              `
      }
    </section>
  `
}

function renderHealthAiSummaryContent(aiSummary: AiDerivationResultItem, healthId: number): string {
  const content = asHealthSummaryContent(aiSummary.content_json)
  const summaryText = content?.summary ?? null
  const observations = content?.observations ?? []
  const suggestedFollowUp = content?.suggested_follow_up ?? null
  const careLevelNote = content?.care_level_note ?? null

  return `
    <article class="record-card record-card--ai">
      <div class="record-card__header">
        <div class="record-card__title-group">
          <h3>Supportive interpretation</h3>
          <span class="record-meta">${escapeHtml(formatDateTime(aiSummary.generated_at || aiSummary.created_at))}</span>
        </div>
        <div class="record-badges">
          ${renderAiStatusBadge(aiSummary.status)}
        </div>
      </div>
      ${
        aiSummary.status === 'failed'
          ? `<p class="section-copy">${escapeHtml(aiSummary.error_message || 'AI summary generation failed.')}</p>`
          : aiSummary.status === 'pending'
            ? '<p class="section-copy">AI summary generation is in progress.</p>'
            : `
                <div class="field-grid">
                  ${renderField('Summary', summaryText, true)}
                  ${renderField('Suggested Follow-up', suggestedFollowUp, true)}
                  ${renderField('Care Level Note', careLevelNote, true)}
                </div>
                <section class="subsection">
                  <h3>Observations</h3>
                  ${renderDataList(
                    observations.map((item, index) => ({ label: `Observation ${index + 1}`, value: item })),
                    'No observations available.',
                  )}
                </section>
              `
      }
      <div class="alert-actions">
        <button class="secondary-button" type="button" data-ai-action="rerun-health-summary" data-record-id="${healthId}">
          Rerun AI Summary
        </button>
      </div>
    </article>
  `
}

function renderKnowledgeAiSummaryContent(aiSummary: AiDerivationResultItem, knowledgeId: number): string {
  const content = asKnowledgeSummaryContent(aiSummary.content_json)
  const summaryText = content?.summary ?? null
  const keyPoints = content?.key_points ?? []
  const keywords = content?.keywords ?? []

  return `
    <article class="record-card record-card--ai">
      <div class="record-card__header">
        <div class="record-card__title-group">
          <h3>Supportive interpretation</h3>
          <span class="record-meta">${escapeHtml(formatDateTime(aiSummary.generated_at || aiSummary.created_at))}</span>
        </div>
        <div class="record-badges">
          ${renderAiStatusBadge(aiSummary.status)}
        </div>
      </div>
      ${
        aiSummary.status === 'failed'
          ? `<p class="section-copy">${escapeHtml(aiSummary.error_message || 'AI summary generation failed.')}</p>`
          : aiSummary.status === 'pending'
            ? '<p class="section-copy">AI summary generation is in progress.</p>'
            : `
                <div class="field-grid">
                  ${renderField('Summary', summaryText, true)}
                </div>
                <section class="subsection">
                  <h3>Key Points</h3>
                  ${renderDataList(
                    keyPoints.map((item, index) => ({ label: `Point ${index + 1}`, value: item })),
                    'No key points available.',
                  )}
                </section>
                <section class="subsection">
                  <h3>Keywords</h3>
                  ${renderInlineList(keywords, 'No keywords available.')}
                </section>
              `
      }
      <div class="alert-actions">
        <button class="secondary-button" type="button" data-ai-action="rerun-knowledge-summary" data-record-id="${knowledgeId}">
          Rerun AI Summary
        </button>
      </div>
    </article>
  `
}

function renderAlertRecords(items: AlertResultItem[]): string {
  return items
    .map(
      (item) => `
        <article class="record-card record-card--alert">
          <div class="record-card__header">
            <div class="record-card__title-group">
              <h3>${escapeHtml(item.title)}</h3>
              <span class="record-meta">${escapeHtml(formatDateTime(item.triggered_at))}</span>
            </div>
            <a class="record-action" href="/health/${item.source_record_id}" data-nav="true">Open record</a>
          </div>
          <div class="record-badges">
            ${renderSeverityBadge(item.severity)}
            ${renderStatusBadge(item.status)}
          </div>
          <div class="field-grid">
            ${renderField('Rule Code', item.rule_code)}
            ${renderField('Source Record', `Health #${item.source_record_id}`)}
            ${renderField('Message', item.message, true)}
            ${renderField('Explanation', item.explanation, true)}
          </div>
          ${
            item.status === 'dismissed'
              ? ''
              : `
                  <div class="alert-actions">
                    ${
                      item.status === 'open'
                        ? `<button class="secondary-button" type="button" data-alert-action="viewed" data-alert-id="${item.id}">Mark viewed</button>`
                        : ''
                    }
                    <button class="secondary-button" type="button" data-alert-action="dismissed" data-alert-id="${item.id}">Dismiss</button>
                  </div>
                `
          }
        </article>
      `,
    )
    .join('')
}

function renderRecentActivityRecords(items: DashboardRecentActivity[]): string {
  return `
    <div class="activity-list">
      ${items
        .map(
          (item) => `
            <article class="activity-card">
              <div class="activity-card__body">
                <div class="activity-card__meta">
                  <span>${escapeHtml(formatDateTime(item.occurred_at))}</span>
                  <span>${escapeHtml(formatDomainLabel(item.target_domain))}</span>
                  <span>${escapeHtml(item.activity_type)}</span>
                </div>
                <p class="activity-card__target">${escapeHtml(item.title_or_preview || 'No preview available.')}</p>
                <p class="section-copy">Target #${item.target_id}</p>
              </div>
              <a class="record-action" href="${escapeHtml(item.href)}" data-nav="true">${escapeHtml(item.action_label)}</a>
            </article>
          `,
        )
        .join('')}
    </div>
  `
}

function renderDataList(items: Array<{ label: string; value: string }>, emptyMessage: string): string {
  if (items.length === 0) {
    return `<p class="section-copy">${escapeHtml(emptyMessage)}</p>`
  }

  return `
    <ul class="data-list">
      ${items
        .map(
          (item) => `
            <li class="data-list__item">
              <span>${escapeHtml(item.label)}</span>
              <strong>${escapeHtml(item.value)}</strong>
            </li>
          `,
        )
        .join('')}
    </ul>
  `
}

function renderInlineList(items: string[], emptyMessage: string): string {
  if (items.length === 0) {
    return `<p class="section-copy">${escapeHtml(emptyMessage)}</p>`
  }

  return `
    <div class="inline-list">
      ${items.map((item) => renderBadge(item, true)).join('')}
    </div>
  `
}

function renderBadge(label: string, muted = false): string {
  return `<span class="badge${muted ? ' is-muted' : ''}">${escapeHtml(label)}</span>`
}

function renderSeverityBadge(value: string): string {
  return `<span class="badge badge--severity badge--severity-${escapeHtml(value)}">${escapeHtml(formatStatusLabel(value))}</span>`
}

function renderStatusBadge(value: string): string {
  return `<span class="badge badge--status badge--status-${escapeHtml(value)}">${escapeHtml(formatStatusLabel(value))}</span>`
}

function renderAiLabelBadge(): string {
  return '<span class="badge badge--ai-label">AI</span>'
}

function renderAiStatusBadge(value: string): string {
  return `<span class="badge badge--ai-status badge--ai-status-${escapeHtml(value)}">${escapeHtml(formatStatusLabel(value))}</span>`
}

function renderPagination(path: string, page: number, pageSize: number, total: number): string {
  const totalPages = Math.max(1, Math.ceil(total / pageSize))
  const hasPrevious = page > 1
  const hasNext = page < totalPages

  return `
    <section class="panel pagination-panel">
      <div class="section-header">
        <h2>Pagination</h2>
      </div>
      <div class="pagination-row">
        <button
          class="secondary-button"
          type="button"
          data-path="${path}"
          data-page-target="${page - 1}"
          ${hasPrevious ? '' : 'disabled'}
        >
          Previous
        </button>
        <div class="pagination-summary">
          <span>Page ${page} of ${totalPages}</span>
          <span>${total} total</span>
        </div>
        <button
          class="secondary-button"
          type="button"
          data-path="${path}"
          data-page-target="${page + 1}"
          ${hasNext ? '' : 'disabled'}
        >
          Next
        </button>
      </div>
    </section>
  `
}

function renderSourceSection(sourceCaptureId: number, sourcePendingId: number | null): string {
  return `
    <section class="panel page-section">
      <div class="section-header">
        <h2>Source</h2>
      </div>
      <div class="field-grid">
        ${renderField('source_capture_id', String(sourceCaptureId))}
        ${renderField('source_pending_id', sourcePendingId === null ? null : String(sourcePendingId))}
      </div>
    </section>
  `
}

function renderLoadingState(): string {
  return `
    <section class="panel status-panel">
      <p class="status-copy">Loading...</p>
    </section>
  `
}

function renderEmptyState(message: string): string {
  return `
    <div class="status-panel is-empty">
      <p class="status-copy">${escapeHtml(message)}</p>
    </div>
  `
}

function renderErrorState(message: string): string {
  return `
    <section class="panel status-panel is-error">
      <p class="status-copy">${escapeHtml(message)}</p>
      <button class="secondary-button" type="button" data-retry="true">Retry</button>
    </section>
  `
}

function renderField(label: string, value: string | null, wide = false): string {
  return `
    <div class="field${wide ? ' field--wide' : ''}">
      <span class="field__label">${escapeHtml(label)}</span>
      <span class="field__value">${escapeHtml(value || '—')}</span>
    </div>
  `
}

function renderTextBlock(value: string | null): string {
  return `
    <pre class="text-block">${escapeHtml(value || '—')}</pre>
  `
}

function renderDateInput(name: string, label: string, value: string): string {
  return `
    <label class="filter-field">
      <span>${escapeHtml(label)}</span>
      <input type="date" name="${name}" value="${escapeHtml(value)}" />
    </label>
  `
}

function renderTextInput(name: string, label: string, value: string): string {
  return `
    <label class="filter-field">
      <span>${escapeHtml(label)}</span>
      <input type="text" name="${name}" value="${escapeHtml(value)}" />
    </label>
  `
}

function renderSelectInput(
  name: string,
  label: string,
  selectedValue: string,
  options: Array<[string, string]>,
): string {
  return `
    <label class="filter-field">
      <span>${escapeHtml(label)}</span>
      <select name="${name}">
        ${options
          .map(
            ([value, text]) => `
              <option value="${escapeHtml(value)}" ${value === selectedValue ? 'selected' : ''}>
                ${escapeHtml(text)}
              </option>
            `,
          )
          .join('')}
      </select>
    </label>
  `
}

function parsePendingListQuery(params: URLSearchParams): PendingListQuery {
  return {
    page: parsePositiveInt(params.get('page'), 1),
    pageSize: parsePageSize(params.get('page_size')),
    sortBy: parseSortBy(params.get('sort_by'), ['created_at', 'resolved_at', 'status', 'target_domain'], 'created_at'),
    sortOrder: parseSortOrder(params.get('sort_order')),
    dateFrom: params.get('date_from') ?? '',
    dateTo: params.get('date_to') ?? '',
    status: parseOption(params.get('status'), ['open', 'confirmed', 'discarded', 'forced'], 'open'),
    targetDomain: parseOption(params.get('target_domain'), ['expense', 'knowledge', 'health', 'unknown'], ''),
  }
}

function parseExpenseListQuery(params: URLSearchParams): ExpenseListQuery {
  return {
    page: parsePositiveInt(params.get('page'), 1),
    pageSize: parsePageSize(params.get('page_size')),
    sortBy: parseSortBy(params.get('sort_by'), ['created_at', 'amount'], 'created_at'),
    sortOrder: parseSortOrder(params.get('sort_order')),
    dateFrom: params.get('date_from') ?? '',
    dateTo: params.get('date_to') ?? '',
    category: params.get('category') ?? '',
    keyword: params.get('keyword') ?? '',
  }
}

function parseKnowledgeListQuery(params: URLSearchParams): KnowledgeListQuery {
  const hasSourceText = params.get('has_source_text')

  return {
    page: parsePositiveInt(params.get('page'), 1),
    pageSize: parsePageSize(params.get('page_size')),
    sortBy: parseSortBy(params.get('sort_by'), ['created_at', 'title'], 'created_at'),
    sortOrder: parseSortOrder(params.get('sort_order')),
    dateFrom: params.get('date_from') ?? '',
    dateTo: params.get('date_to') ?? '',
    keyword: params.get('keyword') ?? '',
    hasSourceText: hasSourceText === 'true' || hasSourceText === 'false' ? hasSourceText : '',
  }
}

function parseHealthListQuery(params: URLSearchParams): HealthListQuery {
  return {
    page: parsePositiveInt(params.get('page'), 1),
    pageSize: parsePageSize(params.get('page_size')),
    sortBy: parseSortBy(params.get('sort_by'), ['created_at', 'metric_type'], 'created_at'),
    sortOrder: parseSortOrder(params.get('sort_order')),
    dateFrom: params.get('date_from') ?? '',
    dateTo: params.get('date_to') ?? '',
    metricType: params.get('metric_type') ?? '',
    keyword: params.get('keyword') ?? '',
    focusAlerts: params.get('focus') === 'alerts',
  }
}

function buildPendingApiParams(query: PendingListQuery): Record<string, string> {
  return {
    page: String(query.page),
    page_size: String(query.pageSize),
    sort_by: query.sortBy,
    sort_order: query.sortOrder,
    status: query.status,
    target_domain: query.targetDomain,
    date_from: toDateTimeStart(query.dateFrom),
    date_to: toDateTimeEnd(query.dateTo),
  }
}

function buildExpenseApiParams(query: ExpenseListQuery): Record<string, string> {
  return {
    page: String(query.page),
    page_size: String(query.pageSize),
    sort_by: query.sortBy,
    sort_order: query.sortOrder,
    date_from: toDateTimeStart(query.dateFrom),
    date_to: toDateTimeEnd(query.dateTo),
    category: query.category,
    keyword: query.keyword,
  }
}

function buildKnowledgeApiParams(query: KnowledgeListQuery): Record<string, string> {
  return {
    page: String(query.page),
    page_size: String(query.pageSize),
    sort_by: query.sortBy,
    sort_order: query.sortOrder,
    date_from: toDateTimeStart(query.dateFrom),
    date_to: toDateTimeEnd(query.dateTo),
    keyword: query.keyword,
    has_source_text: query.hasSourceText,
  }
}

function buildHealthApiParams(query: HealthListQuery): Record<string, string> {
  return {
    page: String(query.page),
    page_size: String(query.pageSize),
    sort_by: query.sortBy,
    sort_order: query.sortOrder,
    date_from: toDateTimeStart(query.dateFrom),
    date_to: toDateTimeEnd(query.dateTo),
    metric_type: query.metricType,
    keyword: query.keyword,
  }
}

function parsePositiveInt(value: string | null, fallback: number): number {
  const parsed = Number.parseInt(value ?? '', 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

function parsePageSize(value: string | null): number {
  const parsed = parsePositiveInt(value, 20)
  if (parsed > 100) {
    return 100
  }
  return parsed
}

function parseSortBy(value: string | null, allowed: string[], fallback: string): string {
  if (value && allowed.includes(value)) {
    return value
  }
  return fallback
}

function parseSortOrder(value: string | null): SortOrder {
  return value === 'asc' ? 'asc' : 'desc'
}

function parseOption(value: string | null, allowed: string[], fallback: string): string {
  return value && allowed.includes(value) ? value : fallback
}

function toDateTimeStart(value: string): string {
  return value ? `${value}T00:00:00` : ''
}

function toDateTimeEnd(value: string): string {
  return value ? `${value}T23:59:59` : ''
}

function buildUrl(path: string, params: URLSearchParams): string {
  const search = params.toString()
  return search ? `${path}?${search}` : path
}

function formatDateTime(value: string): string {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }
  return date.toLocaleString()
}

function formatBoolean(value: boolean): string {
  return value ? 'Yes' : 'No'
}

function formatStatusLabel(value: string): string {
  return value
    .split('_')
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ')
}

function formatDomainLabel(value: string): string {
  return value
    .split('_')
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ')
}

function formatJson(value: unknown): string {
  if (value === undefined) {
    return '—'
  }
  try {
    return JSON.stringify(value, null, 2) ?? '—'
  } catch {
    return String(value)
  }
}

function getHealthSummaryDerivation(items: AiDerivationResultItem[]): AiDerivationResultItem | null {
  return items.find((item) => item.derivation_type === 'health_summary') ?? null
}

function getKnowledgeSummaryDerivation(items: AiDerivationResultItem[]): AiDerivationResultItem | null {
  return items.find((item) => item.derivation_type === 'knowledge_summary') ?? null
}

function asHealthSummaryContent(
  value: unknown,
): { summary: string; observations: string[]; suggested_follow_up: string; care_level_note: string } | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return null
  }

  const content = value as Record<string, unknown>
  if (
    typeof content.summary !== 'string' ||
    !Array.isArray(content.observations) ||
    content.observations.some((item) => typeof item !== 'string') ||
    typeof content.suggested_follow_up !== 'string' ||
    typeof content.care_level_note !== 'string'
  ) {
    return null
  }

  return {
    summary: content.summary,
    observations: content.observations as string[],
    suggested_follow_up: content.suggested_follow_up,
    care_level_note: content.care_level_note,
  }
}

function asKnowledgeSummaryContent(
  value: unknown,
): { summary: string; key_points: string[]; keywords: string[] } | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return null
  }

  const content = value as Record<string, unknown>
  if (
    typeof content.summary !== 'string' ||
    !Array.isArray(content.key_points) ||
    content.key_points.some((item) => typeof item !== 'string') ||
    !Array.isArray(content.keywords) ||
    content.keywords.some((item) => typeof item !== 'string')
  ) {
    return null
  }

  return {
    summary: content.summary,
    key_points: content.key_points as string[],
    keywords: content.keywords as string[],
  }
}

function escapeHtml(value: string): string {
  return value
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;')
}

function toErrorMessage(error: unknown): string {
  if (error instanceof Error) {
    return error.message
  }
  return 'Request failed.'
}
