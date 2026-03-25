import './style.css'

import {
  acknowledgeAlert,
  ApiRequestError,
  applyWorkbenchTemplate,
  createWorkbenchShortcut,
  createWorkbenchTemplate,
  fetchAiDerivationDetail,
  fetchAlertList,
  fetchDashboard,
  fetchExpenseDetail,
  fetchExpenseList,
  fetchHealthDetail,
  fetchHealthList,
  fetchKnowledgeDetail,
  fetchKnowledgeList,
  fetchRuntimeStatus,
  fetchWorkbenchHome,
  fetchWorkbenchPreferences,
  fetchWorkbenchShortcuts,
  updateWorkbenchShortcut,
  updateWorkbenchTemplate,
  deleteWorkbenchShortcut,
  fetchPendingDetail,
  fetchPendingList,
  resolveAlert,
  requestAiDerivationRecompute,
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
  RuntimeStatusData,
  WorkbenchHomeData,
  WorkbenchPreferences,
  WorkbenchShortcut,
  WorkbenchTemplate,
} from './api.ts'

type NavSection = 'workbench' | 'dashboard' | 'capture' | 'pending' | 'expense' | 'knowledge' | 'health'
type SortOrder = 'asc' | 'desc'

interface WorkbenchFlash {
  kind: 'success' | 'error'
  message: string
}

interface WorkbenchUiState {
  editingTemplateId: number | null
  editingShortcutId: number | null
  flash: WorkbenchFlash | null
}

interface FailureSignal {
  message: string
  recoveryHint: string | null
  formalFactsNote: string | null
}

interface KnowledgeAiSummaryState {
  kind: 'ready' | 'pending' | 'failed' | 'invalidated' | 'not-generated' | 'unavailable'
  derivation: AiDerivationResultItem | null
  errorMessage?: string
}

type AlertActionType = 'acknowledge' | 'resolve'

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
  | { kind: 'workbench'; section: NavSection; pageTitle: string; documentTitle: string }
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
const workbenchUiState: WorkbenchUiState = {
  editingTemplateId: null,
  editingShortcutId: null,
  flash: null,
}

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
    return { kind: 'redirect', to: '/workbench' }
  }

  if (parts.length === 1) {
    switch (parts[0]) {
      case 'workbench':
        return {
          kind: 'workbench',
          section: 'workbench',
          pageTitle: 'Workbench',
          documentTitle: 'Workbench',
        }
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
        return { kind: 'redirect', to: '/workbench' }
    }
  }

  if (parts.length === 2) {
    if (parts[0] === 'pending') {
      return {
        kind: 'pending-detail',
        section: 'pending',
        pageTitle: 'Pending Item',
        documentTitle: 'Pending Item',
        id: parts[1],
      }
    }
    if (parts[0] === 'expense') {
      return {
        kind: 'expense-detail',
        section: 'expense',
        pageTitle: 'Expense Record',
        documentTitle: 'Expense Record',
        id: parts[1],
      }
    }
    if (parts[0] === 'knowledge') {
      return {
        kind: 'knowledge-detail',
        section: 'knowledge',
        pageTitle: 'Knowledge Record',
        documentTitle: 'Knowledge Record',
        id: parts[1],
      }
    }
    if (parts[0] === 'health') {
      return {
        kind: 'health-detail',
        section: 'health',
        pageTitle: 'Health Record',
        documentTitle: 'Health Record',
        id: parts[1],
      }
    }
  }

  return { kind: 'redirect', to: '/workbench' }
}

async function renderRoute(route: Exclude<Route, { kind: 'redirect' }>): Promise<string> {
  switch (route.kind) {
    case 'workbench':
      return renderWorkbenchPage()
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
  const [dashboardResult, runtimeStatusResult] = await Promise.allSettled([fetchDashboard(), fetchRuntimeStatus()])

  return renderDashboardView(
    dashboardResult.status === 'fulfilled' ? dashboardResult.value : null,
    dashboardResult.status === 'rejected' ? toErrorMessage(dashboardResult.reason) : null,
    runtimeStatusResult.status === 'fulfilled' ? runtimeStatusResult.value : null,
    runtimeStatusResult.status === 'rejected' ? toErrorMessage(runtimeStatusResult.reason) : null,
  )
}

async function renderWorkbenchPage(): Promise<string> {
  const [homeResult, dashboardResult, shortcutsResult, preferencesResult, runtimeStatusResult] = await Promise.allSettled([
    fetchWorkbenchHome(),
    fetchDashboard(),
    fetchWorkbenchShortcuts(),
    fetchWorkbenchPreferences(),
    fetchRuntimeStatus(),
  ])

  if (homeResult.status === 'rejected') {
    return renderPageShell(`
      ${renderPageHeaderBlock({
        title: 'Workbench',
        copy: 'Central entry layer for work modes, shortcuts, recent context, and summary.',
      })}
      ${renderRuntimeStatusSection(
        runtimeStatusResult.status === 'fulfilled' ? runtimeStatusResult.value : null,
        runtimeStatusResult.status === 'rejected' ? toErrorMessage(runtimeStatusResult.reason) : null,
      )}
      ${renderUnavailableState('Workbench home is unavailable.', toErrorMessage(homeResult.reason))}
    `)
  }

  return renderWorkbenchView(
    homeResult.value,
    dashboardResult.status === 'fulfilled' ? dashboardResult.value : null,
    shortcutsResult.status === 'fulfilled' ? shortcutsResult.value.items : [],
    preferencesResult.status === 'fulfilled' ? preferencesResult.value : null,
    runtimeStatusResult.status === 'fulfilled' ? runtimeStatusResult.value : null,
    {
      dashboardError: dashboardResult.status === 'rejected' ? toErrorMessage(dashboardResult.reason) : null,
      shortcutsError: shortcutsResult.status === 'rejected' ? toErrorMessage(shortcutsResult.reason) : null,
      preferencesError: preferencesResult.status === 'rejected' ? toErrorMessage(preferencesResult.reason) : null,
      runtimeStatusError: runtimeStatusResult.status === 'rejected' ? toErrorMessage(runtimeStatusResult.reason) : null,
    },
  )
}

function renderWorkbenchView(
  home: WorkbenchHomeData,
  dashboard: DashboardData | null,
  allShortcuts: WorkbenchShortcut[],
  preferences: WorkbenchPreferences | null,
  runtimeStatus: RuntimeStatusData | null,
  errors: {
    dashboardError: string | null
    shortcutsError: string | null
    preferencesError: string | null
    runtimeStatusError: string | null
  },
): string {
  const templates = home.templates
  const activeTemplate = templates.find((template) => template.template_id === home.current_mode.template_id) ?? null
  const defaultTemplate =
    templates.find((template) => template.template_id === preferences?.default_template_id) ?? activeTemplate
  const editingTemplate =
    templates.find((template) => template.template_id === workbenchUiState.editingTemplateId) ?? null
  const editingShortcut =
    allShortcuts.find((shortcut) => shortcut.shortcut_id === workbenchUiState.editingShortcutId) ?? null

  return renderPageShell(`
    ${renderPageHeaderBlock({
      title: 'Workbench',
      copy: 'Entry layer for current mode, what matters now, and where to go next.',
    })}
    ${renderWorkbenchFlash()}
    ${renderWorkbenchStateBanner(home, dashboard, errors.dashboardError)}
    ${renderWorkbenchCurrentModeSection(home, activeTemplate, defaultTemplate, preferences, errors.preferencesError)}
    ${renderWorkbenchDashboardSummarySection(dashboard, errors.dashboardError)}
    ${renderWorkbenchShortcutsSection(home, allShortcuts, editingShortcut, errors.shortcutsError)}
    ${renderWorkbenchRecentSection(home)}
    ${renderWorkbenchTemplatesSection(home, editingTemplate)}
    ${renderRuntimeStatusSection(runtimeStatus, errors.runtimeStatusError)}
  `)
}

function renderWorkbenchFlash(): string {
  if (!workbenchUiState.flash) {
    return ''
  }

  if (workbenchUiState.flash.kind === 'error') {
    return renderErrorState(workbenchUiState.flash.message)
  }

  return `
    <section class="panel status-panel">
      <p class="status-copy">${escapeHtml(workbenchUiState.flash.message)}</p>
    </section>
  `
}

function renderWorkbenchCurrentModeSection(
  home: WorkbenchHomeData,
  activeTemplate: WorkbenchTemplate | null,
  defaultTemplate: WorkbenchTemplate | null,
  preferences: WorkbenchPreferences | null,
  preferencesError?: string | null,
): string {
  const currentModeHref = buildWorkbenchModeHref({
    default_module: home.current_mode.default_module,
    default_view_key: home.current_mode.default_view_key,
    default_query_json: home.current_mode.default_query_json,
  })

  return renderSectionShell(
    {
      title: '1. Current Mode',
      copy: 'Current mode only changes entry context. It does not change formal record semantics.',
      className: 'workbench-section section-shell--primary',
    },
    `
      <div class="workbench-mode-grid">
        <article class="record-card">
          <div class="record-card__header">
            <div class="record-card__title-group">
              <h3>${escapeHtml(activeTemplate?.name ?? 'No active mode')}</h3>
              <span class="record-meta">Active mode</span>
            </div>
            <div class="inline-list">
              ${activeTemplate ? renderBadge(formatDomainLabel(activeTemplate.default_module)) : ''}
              ${activeTemplate?.template_type ? renderBadge(formatStatusLabel(activeTemplate.template_type), true) : ''}
            </div>
          </div>
          <p class="section-copy">${escapeHtml(activeTemplate?.description ?? 'Select a template to set the current work mode.')}</p>
          <div class="field-grid">
            ${renderField('Default View', activeTemplate?.default_view_key ?? 'list')}
            ${renderField('Scoped Shortcuts', String(activeTemplate?.scoped_shortcut_ids.length ?? 0))}
            ${renderField('Query Defaults', summarizeQuery(activeTemplate?.default_query_json))}
          </div>
          ${
            currentModeHref
              ? `<div class="filter-actions"><a class="record-action" href="${escapeHtml(currentModeHref)}" data-nav="true">Open current context</a></div>`
              : ''
          }
        </article>
        <article class="record-card">
          <div class="record-card__header">
            <div class="record-card__title-group">
              <h3>${escapeHtml(defaultTemplate?.name ?? 'No default mode')}</h3>
              <span class="record-meta">Default mode</span>
            </div>
            ${
              preferences
                ? `<span class="record-meta">Updated ${escapeHtml(formatDateTime(preferences.updated_at))}</span>`
                : ''
            }
          </div>
          <p class="section-copy">${
            defaultTemplate
              ? `Default entry context remains API-defined and can be changed only through workbench APIs.`
              : 'No default template is configured.'
          }</p>
          ${preferencesError ? `<p class="section-copy">${escapeHtml(preferencesError)}</p>` : ''}
          <div class="field-grid">
            ${renderField('Default Module', defaultTemplate ? formatDomainLabel(defaultTemplate.default_module) : 'Not set')}
            ${renderField('Enabled', defaultTemplate ? formatBoolean(defaultTemplate.is_enabled) : 'No')}
          </div>
        </article>
      </div>
    `,
  )
}

function renderWorkbenchTemplatesSection(home: WorkbenchHomeData, editingTemplate: WorkbenchTemplate | null): string {
  const builtinTemplates = home.templates.filter((template) => template.template_type === 'builtin')
  const userTemplates = home.templates.filter((template) => template.template_type === 'user')

  return renderSectionShell(
    {
      title: '5. Templates',
      copy: 'Templates are named work modes. Builtin templates are read-only. User templates stay lightweight.',
      className: 'workbench-section section-shell--primary',
    },
    `
      <div class="workbench-card-grid">
        ${builtinTemplates.map((template) => renderWorkbenchTemplateCard(template, home.current_mode.template_id)).join('')}
        ${userTemplates.map((template) => renderWorkbenchTemplateCard(template, home.current_mode.template_id)).join('')}
      </div>
      ${renderWorkbenchTemplateForm(home.templates, editingTemplate)}
    `,
  )
}

function renderWorkbenchTemplateCard(template: WorkbenchTemplate, activeTemplateId: number | null): string {
  const isActive = template.template_id === activeTemplateId
  return `
    <article class="record-card${isActive ? ' record-card--priority' : ''}">
      <div class="record-card__header">
        <div class="record-card__title-group">
          <h3>${escapeHtml(template.name)}</h3>
          <span class="record-meta">${escapeHtml(template.description ?? 'No description')}</span>
        </div>
        <div class="inline-list">
          ${isActive ? renderBadge('Active') : ''}
          ${renderBadge(formatStatusLabel(template.template_type), true)}
          ${renderBadge(formatDomainLabel(template.default_module))}
          ${renderStatusBadge(template.is_enabled ? 'open' : 'dismissed')}
        </div>
      </div>
      <div class="field-grid">
        ${renderField('Default View', template.default_view_key ?? 'list')}
        ${renderField('Query Defaults', summarizeQuery(template.default_query_json))}
        ${renderField('Scoped Shortcuts', String(template.scoped_shortcut_ids.length))}
      </div>
      <div class="filter-actions">
        <button class="primary-button" type="button" data-workbench-action="template-apply" data-template-id="${template.template_id}">
          Apply
        </button>
        <button class="secondary-button" type="button" data-workbench-action="template-apply-default" data-template-id="${template.template_id}">
          Apply as default
        </button>
        ${
          template.template_type === 'user'
            ? `
                <button class="secondary-button" type="button" data-workbench-action="template-edit" data-template-id="${template.template_id}">
                  Edit
                </button>
                <button
                  class="secondary-button"
                  type="button"
                  data-workbench-action="template-toggle"
                  data-template-id="${template.template_id}"
                  data-enabled="${template.is_enabled ? 'true' : 'false'}"
                >
                  ${template.is_enabled ? 'Disable' : 'Enable'}
                </button>
              `
            : ''
        }
      </div>
    </article>
  `
}

function renderWorkbenchTemplateForm(
  templates: WorkbenchTemplate[],
  editingTemplate: WorkbenchTemplate | null,
): string {
  const title = editingTemplate ? 'Edit user template' : 'Create user template'
  return `
    <section class="workbench-form-panel">
      <div class="section-header">
        <h3>${escapeHtml(title)}</h3>
        <p class="section-copy">Keep template defaults limited to existing formal read query semantics.</p>
      </div>
      <form class="filter-form" data-workbench-form="true" data-workbench-kind="template">
        <input type="hidden" name="template_id" value="${editingTemplate?.template_id ?? ''}" />
        <div class="filter-grid">
          ${renderTextInput('name', 'Name', editingTemplate?.name ?? '')}
          ${renderWorkbenchModuleSelect('default_module', editingTemplate?.default_module ?? 'dashboard')}
          ${renderTextInput('default_view_key', 'Default view key', editingTemplate?.default_view_key ?? '')}
          ${renderTextInput('sort_order', 'Sort order', String(editingTemplate?.sort_order ?? templates.length * 10 + 40))}
          ${renderTextInput('scoped_shortcut_ids', 'Scoped shortcut IDs', (editingTemplate?.scoped_shortcut_ids ?? []).join(', '))}
          ${renderTextInput('description', 'Description', editingTemplate?.description ?? '')}
        </div>
        <label class="filter-field">
          <span>Default query JSON</span>
          <textarea class="workbench-textarea" name="default_query_json">${escapeHtml(
            editingTemplate?.default_query_json ? JSON.stringify(editingTemplate.default_query_json, null, 2) : '',
          )}</textarea>
        </label>
        <label class="workbench-checkbox">
          <input type="checkbox" name="is_enabled" ${editingTemplate?.is_enabled ?? true ? 'checked' : ''} />
          <span>Enabled</span>
        </label>
        <div class="filter-actions">
          <button class="primary-button" type="submit">${editingTemplate ? 'Save template' : 'Create template'}</button>
          ${
            editingTemplate
              ? '<button class="secondary-button" type="button" data-workbench-action="template-cancel">Cancel edit</button>'
              : ''
          }
        </div>
      </form>
    </section>
  `
}

function renderWorkbenchShortcutsSection(
  home: WorkbenchHomeData,
  allShortcuts: WorkbenchShortcut[],
  editingShortcut: WorkbenchShortcut | null,
  shortcutsError?: string | null,
): string {
  return renderSectionShell(
    {
      title: '3. Fixed Shortcuts',
      copy: 'Use shortcuts to move into the next formal page or work context. They do not run action chains.',
      className: 'workbench-section section-shell--secondary',
    },
    `
      <div class="workbench-shortcut-highlight">
        ${
          home.pinned_shortcuts.length > 0
            ? home.pinned_shortcuts.map((shortcut) => renderWorkbenchShortcutCard(shortcut, true)).join('')
            : renderEmptyState('No pinned shortcuts for the current mode.')
        }
      </div>
      ${shortcutsError ? `<p class="section-copy">${escapeHtml(shortcutsError)}</p>` : ''}
      <div class="workbench-card-grid">
        ${allShortcuts.map((shortcut) => renderWorkbenchShortcutCard(shortcut, false)).join('')}
      </div>
      ${renderWorkbenchShortcutForm(editingShortcut)}
    `,
  )
}

function renderWorkbenchShortcutCard(shortcut: WorkbenchShortcut, highlighted: boolean): string {
  const href = buildWorkbenchShortcutHref(shortcut)
  return `
    <article class="record-card${highlighted ? ' record-card--priority' : ''}">
      <div class="record-card__header">
        <div class="record-card__title-group">
          <h3>${escapeHtml(shortcut.label)}</h3>
          <span class="record-meta">${escapeHtml(shortcut.target_type)}</span>
        </div>
        <div class="inline-list">
          ${renderStatusBadge(shortcut.is_enabled ? 'open' : 'dismissed')}
          ${renderBadge(`Order ${shortcut.sort_order}`, true)}
        </div>
      </div>
      <p class="section-copy">${escapeHtml(describeShortcutTarget(shortcut))}</p>
      <div class="filter-actions">
        ${
          href
            ? `<a class="record-action" href="${escapeHtml(href)}" data-nav="true">Open</a>`
            : '<span class="record-meta">No target route</span>'
        }
        <button class="secondary-button" type="button" data-workbench-action="shortcut-edit" data-shortcut-id="${shortcut.shortcut_id}">
          Edit
        </button>
        <button
          class="secondary-button"
          type="button"
          data-workbench-action="shortcut-toggle"
          data-shortcut-id="${shortcut.shortcut_id}"
          data-enabled="${shortcut.is_enabled ? 'true' : 'false'}"
        >
          ${shortcut.is_enabled ? 'Disable' : 'Enable'}
        </button>
        <button class="secondary-button" type="button" data-workbench-action="shortcut-delete" data-shortcut-id="${shortcut.shortcut_id}">
          Delete
        </button>
      </div>
    </article>
  `
}

function renderWorkbenchShortcutForm(editingShortcut: WorkbenchShortcut | null): string {
  const payload = asRecord(editingShortcut?.target_payload_json)
  const title = editingShortcut ? 'Edit shortcut' : 'Create shortcut'
  return `
    <section class="workbench-form-panel">
      <div class="section-header">
        <h3>${escapeHtml(title)}</h3>
        <p class="section-copy">Shortcut targets stay limited to route or module-view context.</p>
      </div>
      <form class="filter-form" data-workbench-form="true" data-workbench-kind="shortcut">
        <input type="hidden" name="shortcut_id" value="${editingShortcut?.shortcut_id ?? ''}" />
        <div class="filter-grid">
          ${renderTextInput('label', 'Label', editingShortcut?.label ?? '')}
          ${renderWorkbenchShortcutTypeSelect(editingShortcut?.target_type ?? 'module_view')}
          ${renderTextInput('route', 'Route', asString(payload?.route))}
          ${renderWorkbenchModuleSelect('module', asString(payload?.module) || 'dashboard')}
          ${renderTextInput('view_key', 'View key', asString(payload?.view_key))}
          ${renderTextInput('sort_order', 'Sort order', String(editingShortcut?.sort_order ?? 10))}
        </div>
        <label class="filter-field">
          <span>Query JSON</span>
          <textarea class="workbench-textarea" name="query_json">${escapeHtml(
            payload?.query ? JSON.stringify(payload.query, null, 2) : '',
          )}</textarea>
        </label>
        <label class="workbench-checkbox">
          <input type="checkbox" name="is_enabled" ${editingShortcut?.is_enabled ?? true ? 'checked' : ''} />
          <span>Enabled</span>
        </label>
        <div class="filter-actions">
          <button class="primary-button" type="submit">${editingShortcut ? 'Save shortcut' : 'Create shortcut'}</button>
          ${
            editingShortcut
              ? '<button class="secondary-button" type="button" data-workbench-action="shortcut-cancel">Cancel edit</button>'
              : ''
          }
        </div>
      </form>
    </section>
  `
}

function renderWorkbenchRecentSection(home: WorkbenchHomeData): string {
  return renderSectionShell(
    {
      title: '4. Recent Context',
      copy: 'Recent helps you continue active work after you know where to go next. It is not a history log or audit timeline.',
      className: 'workbench-section section-shell--secondary',
    },
    `
      ${
        home.recent_contexts.length > 0
          ? `
              <div class="activity-list">
                ${home.recent_contexts.map((recent) => renderWorkbenchRecentCard(recent)).join('')}
              </div>
            `
          : renderEmptyState('No recent work context yet.')
      }
    `,
  )
}

function renderWorkbenchRecentCard(recent: WorkbenchHomeData['recent_contexts'][number]): string {
  return `
    <article class="activity-card">
      <div class="activity-card__body">
        <div class="record-badges">
          ${renderBadge(formatDomainLabel(recent.object_type))}
          ${renderStatusBadge(recent.action_type)}
        </div>
        <p class="activity-card__target">${escapeHtml(recent.title_snapshot)}</p>
        <div class="activity-card__meta">
          <span>${escapeHtml(formatDateTime(recent.occurred_at))}</span>
          <span>#${escapeHtml(recent.object_id)}</span>
        </div>
      </div>
      <a class="record-action" href="${escapeHtml(recent.route_snapshot)}" data-nav="true">Resume</a>
    </article>
  `
}

function renderWorkbenchDashboardSummarySection(dashboard: DashboardData | null, errorMessage?: string | null): string {
  return renderSectionShell(
    {
      title: '2. Dashboard Summary',
      copy: 'Summary stays summary. Use it to see what matters now before stepping into a formal page.',
      className: 'workbench-section section-shell--support',
    },
    `
      ${
        errorMessage
          ? renderUnavailableState(
              'Dashboard summary is unavailable for the workbench right now.',
              errorMessage,
            )
          : dashboard
            ? isDashboardEmpty(dashboard)
              ? renderPageStatePanel({
                  tone: 'empty',
                  eyebrow: 'Empty',
                  title: 'Dashboard summary is currently empty.',
                  message:
                    'Empty means the shared API responded successfully, but this page does not have records or summary data yet.',
                })
              : `
                  <div class="summary-grid">
                    <article class="summary-card">
                      <div class="summary-card__header">
                        <h3>Pending</h3>
                        <a class="record-action" href="/pending" data-nav="true">Open</a>
                      </div>
                      <p class="summary-value">${dashboard.pending_summary.open_count}</p>
                      <p class="section-copy">${dashboard.pending_summary.resolved_in_last_7_days} resolved in the last 7 days.</p>
                    </article>
                    <article class="summary-card summary-card--alert">
                      <div class="summary-card__header">
                        <h3>Open alerts</h3>
                        <a class="record-action" href="/dashboard" data-nav="true">Dashboard</a>
                      </div>
                      <p class="summary-value">${dashboard.alert_summary.open_count}</p>
                      <p class="section-copy">Current high-signal alert count from the shared dashboard summary.</p>
                    </article>
                    <article class="summary-card">
                      <div class="summary-card__header">
                        <h3>Expense</h3>
                        <a class="record-action" href="/expense" data-nav="true">Open</a>
                      </div>
                      <p class="summary-value">${dashboard.expense_summary.created_in_current_month}</p>
                      <p class="section-copy">Created this month.</p>
                    </article>
                    <article class="summary-card">
                      <div class="summary-card__header">
                        <h3>Knowledge</h3>
                        <a class="record-action" href="/knowledge" data-nav="true">Open</a>
                      </div>
                      <p class="summary-value">${dashboard.knowledge_summary.created_in_last_7_days}</p>
                      <p class="section-copy">Created in the last 7 days.</p>
                    </article>
                    <article class="summary-card">
                      <div class="summary-card__header">
                        <h3>Health</h3>
                        <a class="record-action" href="/health" data-nav="true">Open</a>
                      </div>
                      <p class="summary-value">${dashboard.health_summary.created_in_last_7_days}</p>
                      <p class="section-copy">Created in the last 7 days.</p>
                    </article>
                  </div>
                `
            : renderPageStatePanel({
                tone: 'empty',
                eyebrow: 'Empty',
                title: 'Dashboard summary is currently empty.',
                message:
                  'Empty means the shared API responded successfully, but this page does not have records or summary data yet.',
              })
      }
    `,
  )
}

async function handleWorkbenchAction(button: HTMLButtonElement): Promise<void> {
  const action = button.dataset.workbenchAction

  try {
    switch (action) {
      case 'template-edit': {
        workbenchUiState.editingTemplateId = Number.parseInt(button.dataset.templateId || '', 10)
        workbenchUiState.flash = null
        await renderApp()
        return
      }
      case 'template-cancel': {
        workbenchUiState.editingTemplateId = null
        workbenchUiState.flash = null
        await renderApp()
        return
      }
      case 'template-apply': {
        const templateId = Number.parseInt(button.dataset.templateId || '', 10)
        await applyWorkbenchTemplate(templateId, { set_as_default: false })
        workbenchUiState.flash = { kind: 'success', message: 'Template applied.' }
        await renderApp()
        return
      }
      case 'template-apply-default': {
        const templateId = Number.parseInt(button.dataset.templateId || '', 10)
        await applyWorkbenchTemplate(templateId, { set_as_default: true })
        workbenchUiState.flash = { kind: 'success', message: 'Template applied as active and default.' }
        await renderApp()
        return
      }
      case 'template-toggle': {
        const templateId = Number.parseInt(button.dataset.templateId || '', 10)
        const enabled = button.dataset.enabled === 'true'
        await updateWorkbenchTemplate(templateId, { is_enabled: !enabled })
        workbenchUiState.flash = { kind: 'success', message: enabled ? 'Template disabled.' : 'Template enabled.' }
        await renderApp()
        return
      }
      case 'shortcut-edit': {
        workbenchUiState.editingShortcutId = Number.parseInt(button.dataset.shortcutId || '', 10)
        workbenchUiState.flash = null
        await renderApp()
        return
      }
      case 'shortcut-cancel': {
        workbenchUiState.editingShortcutId = null
        workbenchUiState.flash = null
        await renderApp()
        return
      }
      case 'shortcut-toggle': {
        const shortcutId = Number.parseInt(button.dataset.shortcutId || '', 10)
        const enabled = button.dataset.enabled === 'true'
        await updateWorkbenchShortcut(shortcutId, { is_enabled: !enabled })
        workbenchUiState.flash = { kind: 'success', message: enabled ? 'Shortcut disabled.' : 'Shortcut enabled.' }
        await renderApp()
        return
      }
      case 'shortcut-delete': {
        const shortcutId = Number.parseInt(button.dataset.shortcutId || '', 10)
        if (!window.confirm('Delete this shortcut?')) {
          return
        }
        await deleteWorkbenchShortcut(shortcutId)
        if (workbenchUiState.editingShortcutId === shortcutId) {
          workbenchUiState.editingShortcutId = null
        }
        workbenchUiState.flash = { kind: 'success', message: 'Shortcut deleted.' }
        await renderApp()
        return
      }
      default:
        return
    }
  } catch (error) {
    workbenchUiState.flash = { kind: 'error', message: toErrorMessage(error) }
    await renderApp()
  }
}

async function handleWorkbenchSubmit(form: HTMLFormElement): Promise<void> {
  const kind = form.dataset.workbenchKind
  const formData = new FormData(form)

  try {
    if (kind === 'template') {
      const templateId = parseOptionalNumber(String(formData.get('template_id') || ''))
      const payload = buildTemplatePayload(formData)
      if (templateId) {
        await updateWorkbenchTemplate(templateId, payload)
        workbenchUiState.flash = { kind: 'success', message: 'Template saved.' }
      } else {
        await createWorkbenchTemplate(payload)
        workbenchUiState.flash = { kind: 'success', message: 'Template created.' }
      }
      workbenchUiState.editingTemplateId = null
      await renderApp()
      return
    }

    if (kind === 'shortcut') {
      const shortcutId = parseOptionalNumber(String(formData.get('shortcut_id') || ''))
      const payload = buildShortcutPayload(formData)
      if (shortcutId) {
        await updateWorkbenchShortcut(shortcutId, payload)
        workbenchUiState.flash = { kind: 'success', message: 'Shortcut saved.' }
      } else {
        await createWorkbenchShortcut(payload)
        workbenchUiState.flash = { kind: 'success', message: 'Shortcut created.' }
      }
      workbenchUiState.editingShortcutId = null
      await renderApp()
    }
  } catch (error) {
    workbenchUiState.flash = { kind: 'error', message: toErrorMessage(error) }
    await renderApp()
  }
}

function buildTemplatePayload(formData: FormData): Record<string, unknown> {
  const payload: Record<string, unknown> = {
    name: requireFormValue(formData, 'name'),
    default_module: requireFormValue(formData, 'default_module'),
    sort_order: parseIntegerOrDefault(String(formData.get('sort_order') || ''), 0),
    is_enabled: formData.has('is_enabled'),
  }

  const defaultViewKey = optionalFormValue(formData, 'default_view_key')
  if (defaultViewKey) {
    payload.default_view_key = defaultViewKey
  }

  const description = optionalFormValue(formData, 'description')
  if (description) {
    payload.description = description
  }

  const scopedShortcutIds = parseIdList(optionalFormValue(formData, 'scoped_shortcut_ids'))
  if (scopedShortcutIds.length > 0) {
    payload.scoped_shortcut_ids = scopedShortcutIds
  }

  const defaultQuery = parseOptionalJson(optionalFormValue(formData, 'default_query_json'))
  if (defaultQuery !== undefined) {
    payload.default_query_json = defaultQuery
  }

  return payload
}

function buildShortcutPayload(formData: FormData): Record<string, unknown> {
  const targetType = requireFormValue(formData, 'target_type')
  const payload: Record<string, unknown> = {
    label: requireFormValue(formData, 'label'),
    target_type: targetType,
    sort_order: parseIntegerOrDefault(String(formData.get('sort_order') || ''), 0),
    is_enabled: formData.has('is_enabled'),
  }

  const route = optionalFormValue(formData, 'route')
  const module = optionalFormValue(formData, 'module')
  const viewKey = optionalFormValue(formData, 'view_key')
  const query = parseOptionalJson(optionalFormValue(formData, 'query_json'))

  if (targetType === 'route') {
    payload.target_payload_json = { route: route ?? '/workbench' }
    return payload
  }

  const targetPayload: Record<string, unknown> = {
    module: module ?? 'dashboard',
  }
  if (viewKey) {
    targetPayload.view_key = viewKey
  }
  if (query !== undefined) {
    targetPayload.query = query
  }
  payload.target_payload_json = targetPayload
  return payload
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
    return renderDetailErrorView('Pending Item', '/pending', 'Pending', toErrorMessage(error))
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
    return renderDetailErrorView('Expense Record', '/expense', 'Expenses', toErrorMessage(error))
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
    fetchAiDerivationDetail('knowledge', id),
  ])

  if (detailResult.status === 'rejected') {
    return renderDetailErrorView('Knowledge Record', '/knowledge', 'Knowledge', toErrorMessage(detailResult.reason))
  }

  return renderKnowledgeDetailView(
    detailResult.value,
    deriveKnowledgeAiSummaryState(aiResult),
  )
}

async function renderHealthListPage(): Promise<string> {
  const query = parseHealthListQuery(new URLSearchParams(window.location.search))

  const [healthResult, alertResult] = await Promise.allSettled([
    fetchHealthList(buildHealthApiParams(query)),
    fetchAlertList({ domain: 'health' }),
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
  const [detailResult, alertResult] = await Promise.allSettled([
    fetchHealthDetail(id),
    fetchAlertList({ domain: 'health', source_record_id: id }),
  ])

  if (detailResult.status === 'rejected') {
    return renderDetailErrorView('Health Record', '/health', 'Health', toErrorMessage(detailResult.reason))
  }

  return renderHealthDetailView(
    detailResult.value,
    alertResult.status === 'fulfilled' ? alertResult.value.items : [],
    alertResult.status === 'rejected' ? toErrorMessage(alertResult.reason) : undefined,
  )
}

function handleClick(event: MouseEvent): void {
  const target = event.target instanceof Element ? event.target : null

  if (!target) {
    return
  }

  const workbenchActionButton = target.closest<HTMLButtonElement>('[data-workbench-action]')
  if (workbenchActionButton) {
    event.preventDefault()
    void handleWorkbenchAction(workbenchActionButton)
    return
  }

  const aiActionButton = target.closest<HTMLButtonElement>('[data-ai-action]')
  if (aiActionButton) {
    const action = aiActionButton.dataset.aiAction
    const recordId = Number.parseInt(aiActionButton.dataset.recordId || '', 10)
    if (action === 'recompute-knowledge-summary' && Number.isFinite(recordId)) {
      event.preventDefault()
      void handleAiDerivationAction(aiActionButton, recordId)
    }
    return
  }

  const alertActionButton = target.closest<HTMLButtonElement>('[data-alert-action]')
  if (alertActionButton) {
    const action = alertActionButton.dataset.alertAction
    const alertId = Number.parseInt(alertActionButton.dataset.alertId || '', 10)
    if ((action === 'acknowledge' || action === 'resolve') && Number.isFinite(alertId)) {
      event.preventDefault()
      void handleAlertAction(alertActionButton, alertId, action)
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

  if (!form) {
    return
  }

  if (form.dataset.workbenchForm === 'true') {
    event.preventDefault()
    void handleWorkbenchSubmit(form)
    return
  }

  if (form.dataset.listForm !== 'true') {
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
          ${renderNavLink('Workbench', '/workbench', activeSection === 'workbench')}
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

function renderPageShell(content: string): string {
  return `
    <div class="page-shell">
      ${content}
    </div>
  `
}

function renderPageHeaderBlock(options: {
  title: string
  copy?: string
  backHref?: string
  backLabel?: string
}): string {
  return `
    <section class="page-header page-header-block">
      ${
        options.backHref && options.backLabel
          ? `<a class="back-link" href="${escapeHtml(options.backHref)}" data-nav="true">Back to ${escapeHtml(options.backLabel)}</a>`
          : ''
      }
      <h1>${escapeHtml(options.title)}</h1>
      ${options.copy ? `<p class="page-copy">${escapeHtml(options.copy)}</p>` : ''}
    </section>
  `
}

function renderSectionActionRow(actions: string): string {
  return `
    <div class="section-action-row">
      ${actions}
    </div>
  `
}

function renderSectionShell(
  options: {
    title: string
    copy?: string
    className?: string
    id?: string
    badge?: string
  },
  body: string,
): string {
  const sectionClasses = ['panel', 'page-section', 'section-shell']
  if (options.className) {
    sectionClasses.push(options.className)
  }

  return `
    <section class="${sectionClasses.join(' ')}"${options.id ? ` id="${escapeHtml(options.id)}"` : ''}>
      <div class="section-header${options.badge ? ' section-header--with-badge' : ''}">
        <h2>${escapeHtml(options.title)}</h2>
        ${options.badge ?? ''}
      </div>
      ${options.copy ? `<p class="section-copy">${escapeHtml(options.copy)}</p>` : ''}
      <div class="section-shell__body">
        ${body}
      </div>
    </section>
  `
}

function renderLoadingPage(title: string): string {
  return renderPageShell(`
    ${renderPageHeaderBlock({ title })}
    ${renderLoadingState()}
  `)
}

function renderPlaceholderPage(title: string, message: string): string {
  return renderPageShell(`
    ${renderPageHeaderBlock({ title })}
    ${renderSectionShell(
      {
        title: 'Placeholder',
        className: 'section-shell--contextual',
      },
      `<p class="placeholder-copy">${escapeHtml(message)}</p>`,
    )}
  `)
}

async function handleAiDerivationAction(
  button: HTMLButtonElement,
  recordId: number,
): Promise<void> {
  const originalLabel = button.textContent || ''
  button.disabled = true
  button.textContent = 'Requesting recompute...'

  try {
    await requestAiDerivationRecompute('knowledge', recordId)
    await renderApp()
  } catch (error) {
    window.alert(toErrorMessage(error))
  } finally {
    button.disabled = false
    button.textContent = originalLabel
  }
}

async function handleAlertAction(
  button: HTMLButtonElement,
  alertId: number,
  action: AlertActionType,
): Promise<void> {
  const originalLabel = button.textContent || ''
  button.disabled = true
  button.textContent = action === 'acknowledge' ? 'Acknowledging...' : 'Resolving...'

  try {
    if (action === 'acknowledge') {
      await acknowledgeAlert(alertId)
    } else {
      await resolveAlert(alertId)
    }
    await renderApp()
  } catch (error) {
    window.alert(toErrorMessage(error))
  } finally {
    button.disabled = false
    button.textContent = originalLabel
  }
}

function renderDashboardView(
  dashboard: DashboardData | null,
  errorMessage?: string | null,
  runtimeStatus?: RuntimeStatusData | null,
  runtimeStatusError?: string | null,
): string {
  return renderPageShell(`
    ${renderPageHeaderBlock({
      title: 'Dashboard',
      copy: 'Summary layer for pending review, formal records, and recent context.',
    })}
    ${renderRuntimeStatusSection(runtimeStatus ?? null, runtimeStatusError ?? null)}
    ${
      !errorMessage && dashboard && isDashboardEmpty(dashboard)
        ? renderPageStatePanel({
            tone: 'empty',
            eyebrow: 'Empty',
            title: 'Dashboard is currently empty.',
            message:
              'Empty means the shared API responded successfully, but this page does not have records or summary data yet.',
          })
        : ''
    }
    ${
      errorMessage
        ? renderUnavailableState('Dashboard is unavailable.', errorMessage)
        : dashboard
          ? `
              ${renderPendingSummarySection(dashboard)}
              ${renderFormalSummariesSection(dashboard)}
              ${renderAlertSummarySection(dashboard)}
              ${renderRecentActivitySection(dashboard)}
              ${renderQuickLinksSection(dashboard)}
            `
          : renderEmptyState('Dashboard data is not available.')
    }
  `)
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
      <h1>Pending Item</h1>
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
        ${renderField('Source Capture ID', String(detail.source_capture_id))}
        ${renderField('Parse Result ID', String(detail.parse_result_id))}
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
  if (errorMessage) {
    return renderPageShell(`
      ${renderPageHeaderBlock({
        title: 'Health',
        copy: 'Formal health records with separate rule-based reminders.',
      })}
      ${renderHealthFilters(query)}
      ${renderUnavailableState('Health records are unavailable.', errorMessage)}
    `)
  }

  const factsSection = renderSectionShell(
    {
      title: 'Formal Records',
      copy: 'Formal health records remain the primary read layer for this page.',
      className: 'section-shell--primary',
    },
    response && response.items.length > 0
      ? renderHealthRecords(response.items)
      : renderEmptyState('No health records found.'),
  )
  const alertsSection = renderHealthAlertSection(
    alerts,
    alertsErrorMessage,
    {
      heading: 'Rule Alerts',
      emptyMessage: 'No rule alerts for health records.',
      emphasize: query.focusAlerts,
    },
  )

  return renderPageShell(`
    ${renderPageHeaderBlock({
      title: 'Health',
      copy: 'Formal health records with separate rule-based reminders.',
    })}
    ${renderHealthFilters(query)}
    ${factsSection}
    ${alertsSection}
    ${renderPagination('/health', response?.page ?? query.page, response?.page_size ?? query.pageSize, response?.total ?? 0)}
  `)
}

function renderExpenseDetailView(detail: ExpenseDetail): string {
  return `
    <section class="page-header">
      <a class="back-link" href="/expense" data-nav="true">Back to Expenses</a>
      <h1>Expense Record</h1>
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
  aiSummaryState: KnowledgeAiSummaryState,
): string {
  return renderPageShell(`
    ${renderPageHeaderBlock({
      title: 'Knowledge Record',
      backHref: '/knowledge',
      backLabel: 'Knowledge',
      copy: 'Read the formal record first, then use source context and AI-derived summary as support.',
    })}
    ${renderSectionShell(
      {
        title: 'Formal Content',
        copy: 'Formal content remains the record of truth for this knowledge entry.',
        className: 'section-shell--primary',
      },
      `
      <div class="field-grid">
        ${renderField('Created At', formatDateTime(detail.created_at))}
        ${renderField('Title', detail.title, true)}
        ${renderField('Content', detail.content, true)}
      </div>
    `,
    )}
    ${renderSectionShell(
      {
        title: 'Source Reference',
        copy: 'Source reference stays contextual. It helps trace origin without replacing the formal record.',
        className: 'section-shell--contextual source-reference-block',
      },
      `
      <div class="field-grid">
        ${renderField('Source Capture ID', String(detail.source_capture_id))}
        ${renderField('Source Pending ID', detail.source_pending_id === null ? null : String(detail.source_pending_id))}
        ${renderField('Source Text', detail.source_text, true)}
      </div>
    `,
    )}
    ${renderKnowledgeAiSummarySection(detail.id, aiSummaryState)}
  `)
}

function renderHealthDetailView(
  detail: HealthDetail,
  alerts: AlertResultItem[],
  alertsErrorMessage?: string,
): string {
  return renderPageShell(`
    ${renderPageHeaderBlock({
      title: 'Health Record',
      backHref: '/health',
      backLabel: 'Health',
      copy: 'Read the formal record first, then use source context and rule alerts as support.',
    })}
    ${renderSectionShell(
      {
        title: 'Formal Record',
        copy: 'Formal health record values remain the record of truth for this page.',
        className: 'section-shell--primary',
      },
      `
      <div class="field-grid">
        ${renderField('ID', String(detail.id))}
        ${renderField('Created At', formatDateTime(detail.created_at))}
        ${renderField('Metric Type', detail.metric_type)}
        ${renderField('Value Text', detail.value_text, true)}
        ${renderField('Note', detail.note, true)}
      </div>
    `,
    )}
    ${renderSourceSection(detail.source_capture_id, detail.source_pending_id)}
    ${renderHealthAlertSection(alerts, alertsErrorMessage, {
      heading: 'Rule Alerts',
      emptyMessage: 'No rule alerts for this health record.',
      sourceRecordId: detail.id,
    })}
  `)
}

function renderDetailErrorView(title: string, backPath: string, backLabel: string, message: string): string {
  return renderPageShell(`
    ${renderPageHeaderBlock({
      title,
      backHref: backPath,
      backLabel,
    })}
    ${renderUnavailableState(`${title} is unavailable.`, message)}
  `)
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
            ${renderField('Source Capture ID', String(item.source_capture_id))}
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
  const openAlerts = filteredAlerts.filter((item) => item.status === 'open')
  const acknowledgedAlerts = filteredAlerts.filter((item) => item.status === 'acknowledged')
  const resolvedAlerts = filteredAlerts.filter((item) => item.status === 'resolved')
  const otherAlerts = filteredAlerts.filter(
    (item) => item.status !== 'open' && item.status !== 'acknowledged' && item.status !== 'resolved',
  )

  return renderSectionShell(
    {
      title: options.heading,
      copy: 'Rule-based alerts are reminders derived from formal health records. They do not replace or rewrite the formal record itself.',
      className: `${options.emphasize ? 'page-section--alert-focus ' : ''}section-shell--secondary`.trim(),
      id: 'health-alerts',
    },
    `
      ${
        errorMessage
          ? renderUnavailableState('Health alerts are unavailable right now.', errorMessage)
          : filteredAlerts.length > 0
            ? `
                ${renderAlertStatusGroup('Open Alerts', openAlerts, 'Open alerts still need attention. This does not mean the formal record was changed.')}
                ${renderAlertStatusGroup('Acknowledged Alerts', acknowledgedAlerts, 'Acknowledged means the alert was seen. It does not mean the formal health record was modified.')}
                ${renderAlertStatusGroup('Resolved Alerts', resolvedAlerts, 'Resolved means the reminder was handled. It does not mean the health fact was automatically corrected.')}
                ${
                  otherAlerts.length > 0
                    ? renderAlertStatusGroup('Other Alert Lifecycle States', otherAlerts, 'Additional alert lifecycle states remain reminders rather than formal fact changes.')
                    : ''
                }
              `
            : renderEmptyState(options.emptyMessage, 'Health alerts are currently empty.')
      }
    `,
  )
}

function renderAlertStatusGroup(title: string, alerts: AlertResultItem[], emptyMessage: string): string {
  return `
    <section class="subsection">
      <h3>${escapeHtml(title)}</h3>
      ${alerts.length > 0 ? renderAlertRecords(alerts) : `<p class="section-copy">${escapeHtml(emptyMessage)}</p>`}
    </section>
  `
}

function renderKnowledgeAiSummarySection(
  knowledgeId: number,
  aiSummaryState: KnowledgeAiSummaryState,
): string {
  return renderSectionShell(
    {
      title: 'AI-derived Summary',
      copy: 'AI-derived summary is generated from the formal record. It does not replace the formal content.',
      className: 'page-section--ai section-shell--secondary',
      badge: renderAiLabelBadge(),
    },
    renderKnowledgeAiSummaryBody(aiSummaryState, knowledgeId),
  )
}

function renderKnowledgeAiSummaryBody(
  aiSummaryState: KnowledgeAiSummaryState,
  knowledgeId: number,
): string {
  switch (aiSummaryState.kind) {
    case 'unavailable':
      return renderUnavailableState(
        'AI-derived summary is unavailable right now.',
        aiSummaryState.errorMessage || 'TraceFold AI derivation request failed.',
      )
    case 'not-generated':
      return renderPageStatePanel({
        tone: 'empty',
        eyebrow: 'Not Generated',
        title: 'AI-derived summary is not available yet.',
        message: 'The formal content remains available.',
        actions: `<button class="secondary-button" type="button" data-ai-action="recompute-knowledge-summary" data-record-id="${knowledgeId}">Generate AI-derived Summary</button>`,
      })
    default:
      return renderKnowledgeAiSummaryContent(aiSummaryState.derivation, knowledgeId)
  }
}

function renderKnowledgeAiSummaryContent(aiSummary: AiDerivationResultItem | null, knowledgeId: number): string {
  if (!aiSummary) {
    return renderPageStatePanel({
      tone: 'empty',
      eyebrow: 'Not Generated',
      title: 'AI-derived summary is not available yet.',
      message: 'The formal content remains available.',
      actions: `<button class="secondary-button" type="button" data-ai-action="recompute-knowledge-summary" data-record-id="${knowledgeId}">Generate AI-derived Summary</button>`,
    })
  }

  const content = asKnowledgeSummaryContent(aiSummary.content_json)
  const summaryText = content?.summary ?? null
  const keyPoints = content?.key_points ?? []
  const keywords = content?.keywords ?? []
  const generationTime =
    aiSummary.generated_at || aiSummary.invalidated_at || aiSummary.failed_at || aiSummary.updated_at || aiSummary.created_at

  return `
    <article class="record-card record-card--ai">
      <div class="record-card__header">
        <div class="record-card__title-group">
          <h3>Generated summary</h3>
          <span class="record-meta">${escapeHtml(formatDateTime(generationTime))}</span>
        </div>
      <div class="record-badges">
        ${renderAiStatusBadge(aiSummary.status)}
      </div>
      </div>
      ${
        aiSummary.status === 'failed'
          ? `
              <p class="section-copy">AI-derived summary generation failed. The formal content remains available.</p>
              <p class="section-copy">${escapeHtml(aiSummary.error_message || 'AI derivation failed. The formal record remains available.')}</p>
            `
          : aiSummary.status === 'invalidated'
            ? `
                <p class="section-copy">AI-derived summary is invalidated and should be recomputed before relying on it.</p>
                ${
                  summaryText
                    ? `
                        <div class="field-grid">
                          ${renderField('Last generated summary', summaryText, true)}
                        </div>
                      `
                    : ''
                }
              `
            : aiSummary.status === 'pending' || aiSummary.status === 'running'
              ? '<p class="section-copy">AI-derived summary recompute has been requested. Refresh the page if the status remains pending.</p>'
            : `
                <section class="subsection">
                  <h3>Summary</h3>
                  ${renderTextBlock(summaryText)}
                </section>
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
      <section class="subsection">
        <h3>Derivation Context</h3>
        <div class="field-grid">
          ${renderField('Derivation Status', formatStatusLabel(aiSummary.status))}
          ${renderField('Model Key', aiSummary.model_key || aiSummary.model_name)}
          ${renderField('Model Version', aiSummary.model_version)}
        </div>
      </section>
      <div class="section-action-row alert-actions">
        <button class="secondary-button" type="button" data-ai-action="recompute-knowledge-summary" data-record-id="${knowledgeId}">
          Recompute AI-derived Summary
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
              <h3>${escapeHtml(item.title || 'Rule alert')}</h3>
              <span class="record-meta">${escapeHtml(formatDateTime(item.triggered_at))}</span>
            </div>
            <a class="record-action" href="/health/${item.source_record_id}" data-nav="true">Open record</a>
          </div>
          <div class="record-badges">
            ${renderSeverityBadge(item.severity)}
            ${renderStatusBadge(item.status)}
          </div>
          <div class="field-grid">
            ${renderField('Rule Key', item.rule_key || item.rule_code || null)}
            ${renderField('Source Record', `Health #${item.source_record_id}`)}
            ${renderField('Status', formatStatusLabel(item.status))}
            ${renderField('Severity', formatStatusLabel(item.severity))}
            ${renderField('Triggered At', formatDateTime(item.triggered_at))}
            ${
              item.acknowledged_at
                ? renderField('Acknowledged At', formatDateTime(item.acknowledged_at))
                : ''
            }
            ${item.resolved_at ? renderField('Resolved At', formatDateTime(item.resolved_at)) : ''}
            ${renderField('Message', item.message, true)}
            ${renderField('Explanation', item.explanation, true)}
            ${item.resolution_note ? renderField('Resolution Note', item.resolution_note, true) : ''}
          </div>
          ${
            item.status === 'open'
              ? `
                  <div class="section-action-row alert-actions">
                    <button class="secondary-button" type="button" data-alert-action="acknowledge" data-alert-id="${item.id}">Acknowledge Alert</button>
                    <button class="secondary-button" type="button" data-alert-action="resolve" data-alert-id="${item.id}">Resolve Alert</button>
                  </div>
                `
              : item.status === 'acknowledged'
                ? `
                    <div class="section-action-row alert-actions">
                      <button class="secondary-button" type="button" data-alert-action="resolve" data-alert-id="${item.id}">Resolve Alert</button>
                    </div>
                  `
                : ''
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
  const normalizedValue =
    value === 'acknowledged'
      ? 'acknowledged'
      : value === 'resolved'
        ? 'resolved'
        : value
  return `<span class="badge badge--status badge--status-${escapeHtml(normalizedValue)}">${escapeHtml(formatStatusLabel(value))}</span>`
}

function renderAiLabelBadge(): string {
  return '<span class="badge badge--ai-label">AI</span>'
}

function renderAiStatusBadge(value: string): string {
  const normalizedValue =
    value === 'ready'
      ? 'completed'
      : value === 'running'
        ? 'pending'
        : value === 'invalidated'
          ? 'invalidated'
          : value
  return `<span class="badge badge--ai-status badge--ai-status-${escapeHtml(normalizedValue)}">${escapeHtml(formatStatusLabel(value))}</span>`
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
  return renderSectionShell(
    {
      title: 'Source Reference',
      copy: 'Source reference stays contextual. It supports traceability without replacing the formal record.',
      className: 'section-shell--contextual source-reference-block',
    },
    `
      <div class="field-grid">
        ${renderField('Source Capture ID', String(sourceCaptureId))}
        ${renderField('Source Pending ID', sourcePendingId === null ? null : String(sourcePendingId))}
      </div>
    `,
  )
}

function renderLoadingState(): string {
  return renderPageStatePanel({
    tone: 'loading',
    eyebrow: 'Loading',
    title: 'Loading shared page inputs.',
    message: 'Loading state is shown while the current route is still fetching its shared API inputs.',
  })
}

function renderEmptyState(message: string, title = 'This section is currently empty.'): string {
  return renderPageStatePanel({
    tone: 'empty',
    eyebrow: 'Empty',
    title,
    message,
  })
}

function renderErrorState(message: string): string {
  const signal = classifyFailureSignal(message)
  return `
    <section class="panel status-panel is-error">
      <p class="status-copy">${escapeHtml(signal.message)}</p>
      ${signal.recoveryHint ? `<p class="section-copy">${escapeHtml(signal.recoveryHint)}</p>` : ''}
      ${signal.formalFactsNote ? `<p class="section-copy">${escapeHtml(signal.formalFactsNote)}</p>` : ''}
      <button class="secondary-button" type="button" data-retry="true">Retry</button>
    </section>
  `
}

function renderUnavailableState(title: string, message: string): string {
  const signal = classifyFailureSignal(message)
  return renderPageStatePanel({
    tone: 'unavailable',
    eyebrow: 'Unavailable',
    title,
    message:
      'Unavailable means the shared API route could not be reached or returned an unusable response.',
    details: [signal.message, signal.recoveryHint, signal.formalFactsNote].filter((value): value is string => Boolean(value)),
    retry: true,
  })
}

function renderDegradedState(title: string, message: string, details: string[]): string {
  return renderPageStatePanel({
    tone: 'degraded',
    eyebrow: 'Degraded',
    title,
    message,
    details,
  })
}

function renderPageStatePanel(options: {
  tone: 'loading' | 'empty' | 'degraded' | 'unavailable' | 'ready'
  eyebrow: string
  title: string
  message: string
  details?: string[]
  retry?: boolean
  actions?: string
}): string {
  const toneClass =
    options.tone === 'degraded'
      ? ' is-warning'
      : options.tone === 'unavailable'
        ? ' is-error'
        : options.tone === 'ready'
          ? ' is-ready'
          : options.tone === 'empty'
            ? ' is-empty'
            : ''

  const actionMarkup = [options.actions, options.retry ? '<button class="secondary-button" type="button" data-retry="true">Retry</button>' : '']
    .filter((value): value is string => Boolean(value))
    .join('')

  return `
    <section class="panel status-panel state-block${toneClass}">
      <span class="status-eyebrow">${escapeHtml(options.eyebrow)}</span>
      <h2 class="status-title">${escapeHtml(options.title)}</h2>
      <p class="status-copy">${escapeHtml(options.message)}</p>
      ${
        options.details && options.details.length > 0
          ? `
              <div class="status-detail-list">
                ${options.details.map((detail) => `<p class="section-copy">${escapeHtml(detail)}</p>`).join('')}
              </div>
            `
          : ''
      }
      ${actionMarkup ? renderSectionActionRow(actionMarkup) : ''}
    </section>
  `
}

function renderRuntimeStatusSection(runtimeStatus: RuntimeStatusData | null, runtimeStatusError?: string | null): string {
  if (runtimeStatusError) {
    return renderUnavailableState('System status is unavailable.', runtimeStatusError)
  }

  if (!runtimeStatus) {
    return renderPageStatePanel({
      tone: 'empty',
      eyebrow: 'Empty',
      title: 'System status has not been loaded yet.',
      message:
        'Empty means the shared API responded successfully, but this page does not have records or summary data yet.',
    })
  }

  const statusDetails = [
    `API: ${formatStatusLabel(runtimeStatus.api_status)}`,
    `Database: ${formatStatusLabel(runtimeStatus.db_status)}`,
    `Migration: ${formatStatusLabel(runtimeStatus.migration_status)}`,
    `Task runtime: ${formatStatusLabel(runtimeStatus.task_runtime_status)}`,
    `Last checked: ${formatDateTime(runtimeStatus.last_checked_at)}`,
  ]

  if (isRuntimeStatusDegraded(runtimeStatus)) {
    return renderDegradedState(
      'System status is degraded.',
      'Degraded means /api/system/status reported a shared runtime warning even though reads may still succeed.',
      [
        ...statusDetails,
        ...runtimeStatus.degraded_reasons.map(
          (reason) => `Degraded reason: ${formatStatusLabel(reason)}`,
        ),
      ],
    )
  }

  return renderPageStatePanel({
    tone: 'ready',
    eyebrow: 'Ready',
    title: 'System status is ready.',
    message: 'The shared API, database, migrations, and task runtime are available for workbench and dashboard reads.',
    details: statusDetails,
  })
}

function renderWorkbenchStateBanner(
  home: WorkbenchHomeData,
  dashboard: DashboardData | null,
  dashboardError?: string | null,
): string {
  if (dashboardError) {
    return renderUnavailableState('Workbench summary inputs are partially unavailable.', dashboardError)
  }

  if (!isWorkbenchEmpty(home, dashboard)) {
    return ''
  }

  return renderPageStatePanel({
    tone: 'empty',
    eyebrow: 'Empty',
    title: 'Workbench is currently empty.',
    message:
      'Empty means the shared API responded successfully, but this page does not have records or summary data yet.',
  })
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

function renderWorkbenchModuleSelect(name: string, value: string): string {
  const options = [
    ['dashboard', 'Dashboard'],
    ['pending', 'Pending'],
    ['expense', 'Expense'],
    ['knowledge', 'Knowledge'],
    ['health', 'Health'],
    ['alerts', 'Alerts'],
  ] as const

  return `
    <label class="filter-field">
      <span>${escapeHtml(formatStatusLabel(name.replaceAll('_', ' ')))}</span>
      <select name="${name}">
        ${options
          .map(
            ([optionValue, label]) =>
              `<option value="${optionValue}" ${optionValue === value ? 'selected' : ''}>${escapeHtml(label)}</option>`,
          )
          .join('')}
      </select>
    </label>
  `
}

function renderWorkbenchShortcutTypeSelect(value: string): string {
  return `
    <label class="filter-field">
      <span>Target type</span>
      <select name="target_type">
        <option value="module_view" ${value === 'module_view' ? 'selected' : ''}>Module view</option>
        <option value="route" ${value === 'route' ? 'selected' : ''}>Route</option>
      </select>
    </label>
  `
}

function buildWorkbenchModeHref(mode: {
  default_module: string | null
  default_view_key: string | null
  default_query_json: unknown
}): string | null {
  if (!mode.default_module) {
    return null
  }
  return buildWorkbenchContextHref(mode.default_module, mode.default_view_key, mode.default_query_json)
}

function buildWorkbenchShortcutHref(shortcut: WorkbenchShortcut): string | null {
  const payload = asRecord(shortcut.target_payload_json)
  if (!payload) {
    return null
  }

  if (shortcut.target_type === 'route') {
    const route = asString(payload.route)
    return route ? route : null
  }

  return buildWorkbenchContextHref(asString(payload.module), asString(payload.view_key), payload.query)
}

function buildWorkbenchContextHref(
  moduleValue: string | null,
  _viewKey: string | null,
  queryValue: unknown,
): string | null {
  if (!moduleValue) {
    return null
  }

  const path = resolveWorkbenchModulePath(moduleValue)
  if (!path) {
    return null
  }

  const params = new URLSearchParams()
  const query = asRecord(queryValue)
  if (query) {
    for (const [key, value] of Object.entries(query)) {
      if (value !== null && value !== undefined) {
        params.set(key, String(value))
      }
    }
  }
  if (moduleValue === 'alerts') {
    params.set('focusAlerts', 'true')
  }
  return buildUrl(path, params)
}

function resolveWorkbenchModulePath(moduleValue: string): string | null {
  switch (moduleValue) {
    case 'dashboard':
      return '/dashboard'
    case 'pending':
      return '/pending'
    case 'expense':
      return '/expense'
    case 'knowledge':
      return '/knowledge'
    case 'health':
    case 'alerts':
      return '/health'
    default:
      return null
  }
}

function describeShortcutTarget(shortcut: WorkbenchShortcut): string {
  const payload = asRecord(shortcut.target_payload_json)
  if (!payload) {
    return 'No target payload.'
  }
  if (shortcut.target_type === 'route') {
    return `Route: ${asString(payload.route) || 'not set'}`
  }
  const parts = [formatDomainLabel(asString(payload.module) || 'dashboard')]
  const viewKey = asString(payload.view_key)
  if (viewKey) {
    parts.push(`view ${viewKey}`)
  }
  const querySummary = summarizeQuery(payload.query)
  if (querySummary !== 'None') {
    parts.push(querySummary)
  }
  return parts.join(' · ')
}

function summarizeQuery(value: unknown): string {
  const record = asRecord(value)
  if (!record || Object.keys(record).length === 0) {
    return 'None'
  }
  return Object.entries(record)
    .map(([key, item]) => `${key}=${String(item)}`)
    .join(', ')
}

function requireFormValue(formData: FormData, key: string): string {
  const value = optionalFormValue(formData, key)
  if (!value) {
    throw new Error(`${formatStatusLabel(key.replaceAll('_', ' '))} is required.`)
  }
  return value
}

function optionalFormValue(formData: FormData, key: string): string | undefined {
  const rawValue = formData.get(key)
  if (rawValue === null) {
    return undefined
  }
  const value = String(rawValue).trim()
  return value ? value : undefined
}

function parseOptionalJson(value: string | undefined): unknown {
  if (!value) {
    return undefined
  }
  try {
    return JSON.parse(value)
  } catch {
    throw new Error('JSON input is invalid.')
  }
}

function parseIdList(value: string | undefined): number[] {
  if (!value) {
    return []
  }
  return value
    .split(',')
    .map((part) => Number.parseInt(part.trim(), 10))
    .filter((item) => Number.isFinite(item))
}

function parseIntegerOrDefault(value: string, fallback: number): number {
  const parsed = Number.parseInt(value, 10)
  return Number.isFinite(parsed) ? parsed : fallback
}

function parseOptionalNumber(value: string): number | null {
  const parsed = Number.parseInt(value, 10)
  return Number.isFinite(parsed) ? parsed : null
}

function asRecord(value: unknown): Record<string, unknown> | null {
  if (typeof value === 'object' && value !== null && !Array.isArray(value)) {
    return value as Record<string, unknown>
  }
  return null
}

function asString(value: unknown): string {
  return typeof value === 'string' ? value : ''
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

function isDashboardEmpty(dashboard: DashboardData): boolean {
  return (
    dashboard.pending_summary.open_count === 0 &&
    dashboard.alert_summary.open_count === 0 &&
    dashboard.quick_links.length === 0 &&
    dashboard.expense_summary.created_in_current_month === 0 &&
    dashboard.knowledge_summary.created_in_last_30_days === 0 &&
    dashboard.health_summary.created_in_last_7_days === 0 &&
    dashboard.recent_activity.length === 0
  )
}

function isWorkbenchEmpty(home: WorkbenchHomeData, dashboard: DashboardData | null): boolean {
  return (
    home.templates.length === 0 &&
    home.pinned_shortcuts.length === 0 &&
    home.recent_contexts.length === 0 &&
    (dashboard ? isDashboardEmpty(dashboard) : true)
  )
}

function isRuntimeStatusDegraded(runtimeStatus: RuntimeStatusData): boolean {
  return (
    runtimeStatus.api_status === 'degraded' ||
    runtimeStatus.db_status === 'unavailable' ||
    runtimeStatus.migration_status !== 'ok' ||
    runtimeStatus.task_runtime_status === 'degraded' ||
    runtimeStatus.degraded_reasons.length > 0
  )
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

function deriveKnowledgeAiSummaryState(
  result: PromiseSettledResult<AiDerivationResultItem>,
): KnowledgeAiSummaryState {
  if (result.status === 'fulfilled') {
    const derivation = result.value
    if (derivation.status === 'invalidated') {
      return { kind: 'invalidated', derivation }
    }
    if (derivation.status === 'failed') {
      return { kind: 'failed', derivation }
    }
    if (derivation.status === 'pending' || derivation.status === 'running') {
      return { kind: 'pending', derivation }
    }
    return { kind: 'ready', derivation }
  }

  const message = toErrorMessage(result.reason)
  if (result.reason instanceof ApiRequestError && result.reason.statusCode === 404) {
    return { kind: 'not-generated', derivation: null }
  }

  return { kind: 'unavailable', derivation: null, errorMessage: message }
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
  return 'TraceFold request failed. Check the shared API and try again.'
}

function classifyFailureSignal(message: string): FailureSignal {
  const normalized = message.toLowerCase()

  if (normalized.includes('api is unavailable') || normalized.includes('service is unavailable')) {
    return {
      message,
      recoveryHint: 'Check /api/healthz first. If the API is healthy, confirm the API base URL in .env.',
      formalFactsNote: 'This entry-side failure does not change existing formal records.',
    }
  }

  if (normalized.includes('invalid response')) {
    return {
      message,
      recoveryHint: 'Check API health first, then inspect the API process or logs.',
      formalFactsNote: 'This response failure does not change existing formal records.',
    }
  }

  if (normalized.includes('workbench url could not be opened')) {
    return {
      message,
      recoveryHint: 'Check the Desktop workbench URL and confirm the Web dev server is running.',
      formalFactsNote: 'This shell-side failure does not change existing formal records.',
    }
  }

  if (normalized.includes('workbench url is invalid') || normalized.includes('workbench url is not configured')) {
    return {
      message,
      recoveryHint: 'Check the Desktop .env workbench URL setting.',
      formalFactsNote: 'This shell-side configuration failure does not change existing formal records.',
    }
  }

  if (normalized.includes('request failed with status')) {
    return {
      message,
      recoveryHint: 'Check API health first. If the API is healthy, inspect the requested route or filters.',
      formalFactsNote: 'Formal records remain unchanged unless a successful write already completed.',
    }
  }

  return {
    message,
    recoveryHint: 'Retry once. If it still fails, check API health and the relevant .env settings.',
    formalFactsNote: 'This entry-side failure does not imply formal facts are damaged.',
  }
}
