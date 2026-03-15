import './style.css'

import {
  fetchExpenseDetail,
  fetchExpenseList,
  fetchHealthDetail,
  fetchHealthList,
  fetchKnowledgeDetail,
  fetchKnowledgeList,
} from './api.ts'
import type {
  ExpenseDetail,
  ExpenseListItem,
  HealthDetail,
  HealthListItem,
  KnowledgeDetail,
  KnowledgeListItem,
  PaginatedResponse,
} from './api.ts'

type NavSection = 'capture' | 'pending' | 'expense' | 'knowledge' | 'health'
type SortOrder = 'asc' | 'desc'

interface BaseListQuery {
  page: number
  pageSize: number
  sortBy: string
  sortOrder: SortOrder
  dateFrom: string
  dateTo: string
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
}

type Route =
  | { kind: 'capture'; section: NavSection; pageTitle: string; documentTitle: string }
  | { kind: 'pending'; section: NavSection; pageTitle: string; documentTitle: string }
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
    return { kind: 'redirect', to: '/expense' }
  }

  if (parts.length === 1) {
    switch (parts[0]) {
      case 'capture':
        return { kind: 'capture', section: 'capture', pageTitle: 'Capture', documentTitle: 'Capture' }
      case 'pending':
        return { kind: 'pending', section: 'pending', pageTitle: 'Pending', documentTitle: 'Pending' }
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
        return { kind: 'redirect', to: '/expense' }
    }
  }

  if (parts.length === 2) {
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

  return { kind: 'redirect', to: '/expense' }
}

async function renderRoute(route: Exclude<Route, { kind: 'redirect' }>): Promise<string> {
  switch (route.kind) {
    case 'capture':
      return renderPlaceholderPage(route.pageTitle, 'Capture view is not implemented yet.')
    case 'pending':
      return renderPlaceholderPage(route.pageTitle, 'Pending view is not implemented yet.')
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
  try {
    const detail = await fetchKnowledgeDetail(id)
    return renderKnowledgeDetailView(detail)
  } catch (error) {
    return renderDetailErrorView('Knowledge Detail', '/knowledge', 'Knowledge', toErrorMessage(error))
  }
}

async function renderHealthListPage(): Promise<string> {
  const query = parseHealthListQuery(new URLSearchParams(window.location.search))

  try {
    const response = await fetchHealthList(buildHealthApiParams(query))
    return renderHealthListView(query, response)
  } catch (error) {
    return renderHealthListView(query, null, toErrorMessage(error))
  }
}

async function renderHealthDetailPage(id: string): Promise<string> {
  try {
    const detail = await fetchHealthDetail(id)
    return renderHealthDetailView(detail)
  } catch (error) {
    return renderDetailErrorView('Health Detail', '/health', 'Health', toErrorMessage(error))
  }
}

function handleClick(event: MouseEvent): void {
  const target = event.target instanceof Element ? event.target : null

  if (!target) {
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
  errorMessage?: string,
): string {
  return `
    <section class="page-header">
      <h1>Health</h1>
    </section>
    ${renderHealthFilters(query)}
    <section class="panel page-section">
      <div class="section-header">
        <h2>List</h2>
      </div>
      ${
        errorMessage
          ? renderErrorState(errorMessage)
          : response && response.items.length > 0
            ? renderHealthRecords(response.items)
            : renderEmptyState('No health records found.')
      }
    </section>
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

function renderKnowledgeDetailView(detail: KnowledgeDetail): string {
  return `
    <section class="page-header">
      <a class="back-link" href="/knowledge" data-nav="true">Back to Knowledge</a>
      <h1>Knowledge Detail</h1>
    </section>
    <section class="panel page-section">
      <div class="section-header">
        <h2>Detail</h2>
      </div>
      <div class="field-grid">
        ${renderField('ID', String(detail.id))}
        ${renderField('Created At', formatDateTime(detail.created_at))}
        ${renderField('Title', detail.title)}
      </div>
    </section>
    <section class="panel page-section">
      <div class="section-header">
        <h2>Content</h2>
      </div>
      ${renderTextBlock(detail.content)}
    </section>
    <section class="panel page-section">
      <div class="section-header">
        <h2>Source Text</h2>
      </div>
      ${renderTextBlock(detail.source_text)}
    </section>
    ${renderSourceSection(detail.source_capture_id, detail.source_pending_id)}
  `
}

function renderHealthDetailView(detail: HealthDetail): string {
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
    ${renderSourceSection(detail.source_capture_id, detail.source_pending_id)}
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
