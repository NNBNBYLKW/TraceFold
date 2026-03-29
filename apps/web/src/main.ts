import './style.css'

import {
  acknowledgeAlert,
  ApiRequestError,
  applyWorkbenchTemplate,
  confirmPending,
  createLocalBackup,
  createWorkbenchShortcut,
  createWorkbenchTemplate,
  discardPending,
  exportCaptureBundle,
  fetchCaptureDetail,
  fetchCaptureList,
  importBulkCapture,
  fetchAiDerivationDetail,
  fetchAlertList,
  fetchDashboard,
  fetchExpenseDetail,
  fetchExpenseList,
  fetchHealthDetail,
  fetchHealthList,
  fetchKnowledgeDetail,
  fetchKnowledgeList,
  fetchLocalOperability,
  fetchRuntimeStatus,
  fetchWorkbenchHome,
  fetchWorkbenchPreferences,
  fetchWorkbenchShortcuts,
  importCaptureBundle,
  previewBulkCapture,
  restoreLocalBackup,
  submitCapture,
  updateWorkbenchShortcut,
  updateWorkbenchTemplate,
  deleteWorkbenchShortcut,
  fetchPendingDetail,
  fetchPendingList,
  fixPending,
  forceInsertPending,
  resolveAlert,
  requestAiDerivationRecompute,
} from './api.ts'
import type {
  AlertResultItem,
  AiDerivationResultItem,
  BulkCaptureImportResult,
  BulkCapturePreviewResult,
  CaptureDetail,
  CaptureListItem,
  CaptureListResponse,
  CaptureSubmitResult,
  DashboardData,
  DashboardRecentActivity,
  ExpenseDetail,
  ExpenseListItem,
  HealthDetail,
  HealthListItem,
  KnowledgeDetail,
  KnowledgeListItem,
  LocalOperabilityData,
  PaginatedResponse,
  PendingActionResult,
  PendingDetail,
  PendingFormalResult,
  PendingListItem,
  PendingListResponse,
  PendingReviewAction,
  RuntimeStatusData,
  WorkbenchHomeData,
  WorkbenchPreferences,
  WorkbenchShortcut,
  WorkbenchTemplate,
} from './api.ts'
import { getInitialLocale, persistLocale } from './locale.ts'
import type { UiLocale } from './locale.ts'

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

interface CaptureSubmissionFeedback {
  kind: 'success' | 'error'
  title: string
  message: string
  captureId?: number
}

interface CaptureUiState {
  feedback: CaptureSubmissionFeedback | null
  submissionDraft: {
    rawText: string
    sourceRef: string
  }
}

interface QuickCaptureUiState {
  feedback: CaptureSubmissionFeedback | null
  draftText: string
}

interface BulkIntakeFeedback {
  kind: 'success' | 'error'
  title: string
  message: string
  details?: string[]
}

interface BulkIntakeUiState {
  selectedFileName: string
  fileText: string
  preview: BulkCapturePreviewResult | null
  feedback: BulkIntakeFeedback | null
}

interface PendingReviewFeedback {
  kind: 'success' | 'error'
  title: string
  message: string
}

interface PendingUiState {
  feedbackById: Record<number, PendingReviewFeedback | undefined>
  fixDraftById: Record<number, string | undefined>
}

interface LocalOperabilityFeedback {
  kind: 'success' | 'error'
  title: string
  message: string
  details?: string[]
}

interface LocalOperabilityUiState {
  feedback: LocalOperabilityFeedback | null
  backupDestinationPath: string
  restoreSourcePath: string
  restoreCreateSafetyBackup: boolean
  restoreConfirmed: boolean
  exportDestinationPath: string
  importSourcePath: string
}

interface KnowledgeAiSummaryState {
  kind: 'ready' | 'pending' | 'failed' | 'invalidated' | 'not-generated' | 'unavailable'
  derivation: AiDerivationResultItem | null
  errorMessage?: string
}

type AlertActionType = 'acknowledge' | 'resolve'
type PendingReviewActionType = 'confirm' | 'discard' | 'force_insert'

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

interface CaptureListQuery extends BaseListQuery {
  status: string
  sourceType: string
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
  | { kind: 'quick-capture'; section: NavSection; pageTitle: string; documentTitle: string }
  | { kind: 'bulk-intake'; section: NavSection; pageTitle: string; documentTitle: string }
  | { kind: 'capture-list'; section: NavSection; pageTitle: string; documentTitle: string }
  | {
      kind: 'capture-detail'
      section: NavSection
      pageTitle: string
      documentTitle: string
      id: string
    }
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

const localeState: { current: UiLocale } = {
  current: getInitialLocale(),
}

const SHARED_COPY = {
  Workbench: { en: 'Workbench', zh: '工作台' },
  Dashboard: { en: 'Dashboard', zh: '总览' },
  Capture: { en: 'Capture', zh: '采集' },
  'Quick Capture': { en: 'Quick Capture', zh: '快速采集' },
  'Bulk Intake': { en: 'Bulk Intake', zh: '批量导入' },
  Pending: { en: 'Pending', zh: '待审' },
  Expenses: { en: 'Expenses', zh: '支出' },
  Knowledge: { en: 'Knowledge', zh: '知识' },
  Health: { en: 'Health', zh: '健康' },
  'Capture Record': { en: 'Capture Record', zh: '采集记录' },
  'Pending Item': { en: 'Pending Item', zh: '待审项' },
  'Expense Record': { en: 'Expense Record', zh: '支出记录' },
  'Knowledge Record': { en: 'Knowledge Record', zh: '知识记录' },
  'Health Record': { en: 'Health Record', zh: '健康记录' },
  'Local-first workspace': { en: 'Local-first workspace', zh: '本地优先工作台' },
  Primary: { en: 'Primary', zh: '主导航' },
  Language: { en: 'Language', zh: '语言' },
  English: { en: 'English', zh: 'English' },
  Chinese: { en: '中文', zh: '中文' },
  'Back to {label}': { en: 'Back to {label}', zh: '返回{label}' },
  Loading: { en: 'Loading', zh: '加载中' },
  Empty: { en: 'Empty', zh: '空' },
  Unavailable: { en: 'Unavailable', zh: '不可用' },
  Degraded: { en: 'Degraded', zh: '降级' },
  Ready: { en: 'Ready', zh: '就绪' },
  Complete: { en: 'Complete', zh: '完成' },
  Resolved: { en: 'Resolved', zh: '已处理' },
  Result: { en: 'Result', zh: '结果' },
  Retry: { en: 'Retry', zh: '重试' },
  Save: { en: 'Save', zh: '保存' },
  Cancel: { en: 'Cancel', zh: '取消' },
  Confirm: { en: 'Confirm', zh: '确认' },
  Discard: { en: 'Discard', zh: '丢弃' },
  Restore: { en: 'Restore', zh: '恢复' },
  Export: { en: 'Export', zh: '导出' },
  Import: { en: 'Import', zh: '导入' },
  Fix: { en: 'Fix', zh: '修正' },
  'Apply Fix': { en: 'Apply Fix', zh: '应用修正' },
  'Force Insert': { en: 'Force Insert', zh: '强制写入' },
  'Action Complete': { en: 'Action Complete', zh: '操作完成' },
  'Action Failed': { en: 'Action Failed', zh: '操作失败' },
  'Loading shared page inputs.': { en: 'Loading shared page inputs.', zh: '正在加载共享页面输入。' },
  'Loading state is shown while the current route is still fetching its shared API inputs.': {
    en: 'Loading state is shown while the current route is still fetching its shared API inputs.',
    zh: '当前路由仍在请求共享 API 输入时，会显示加载状态。',
  },
  'This section is currently empty.': { en: 'This section is currently empty.', zh: '此分区当前为空。' },
  'Unavailable means the shared API route could not be reached or returned an unusable response.': {
    en: 'Unavailable means the shared API route could not be reached or returned an unusable response.',
    zh: '不可用表示共享 API 路由无法访问，或返回了不可用的响应。',
  },
  'TraceFold request failed. Check the shared API and try again.': {
    en: 'TraceFold request failed. Check the shared API and try again.',
    zh: 'TraceFold 请求失败。请检查共享 API 后重试。',
  },
  'Check /api/healthz first. If the API is healthy, confirm the API base URL in .env.': {
    en: 'Check /api/healthz first. If the API is healthy, confirm the API base URL in .env.',
    zh: '先检查 /api/healthz。如果 API 正常，再确认 .env 中的 API 基础地址。',
  },
  'This entry-side failure does not change existing formal records.': {
    en: 'This entry-side failure does not change existing formal records.',
    zh: '此入口侧失败不会改动已有正式记录。',
  },
  'Check API health first, then inspect the API process or logs.': {
    en: 'Check API health first, then inspect the API process or logs.',
    zh: '先检查 API 健康状态，再查看 API 进程或日志。',
  },
  'This response failure does not change existing formal records.': {
    en: 'This response failure does not change existing formal records.',
    zh: '此响应失败不会改动已有正式记录。',
  },
  'Check the Desktop workbench URL and confirm the Web dev server is running.': {
    en: 'Check the Desktop workbench URL and confirm the Web dev server is running.',
    zh: '请检查 Desktop 的工作台地址，并确认 Web 开发服务器正在运行。',
  },
  'This shell-side failure does not change existing formal records.': {
    en: 'This shell-side failure does not change existing formal records.',
    zh: '此壳层侧失败不会改动已有正式记录。',
  },
  'Check the Desktop .env workbench URL setting.': {
    en: 'Check the Desktop .env workbench URL setting.',
    zh: '请检查 Desktop .env 中的工作台地址设置。',
  },
  'Check API health first. If the API is healthy, inspect the requested route or filters.': {
    en: 'Check API health first. If the API is healthy, inspect the requested route or filters.',
    zh: '先检查 API 健康状态。如果 API 正常，再检查请求的路由或筛选条件。',
  },
  'Formal records remain unchanged unless a successful write already completed.': {
    en: 'Formal records remain unchanged unless a successful write already completed.',
    zh: '除非成功写入已完成，否则正式记录保持不变。',
  },
  'Retry once. If it still fails, check API health and the relevant .env settings.': {
    en: 'Retry once. If it still fails, check API health and the relevant .env settings.',
    zh: '请先重试一次；如果仍失败，再检查 API 健康状态和相关 .env 设置。',
  },
  'This entry-side failure does not imply formal facts are damaged.': {
    en: 'This entry-side failure does not imply formal facts are damaged.',
    zh: '此入口侧失败并不表示正式事实已受损。',
  },
  'System status is unavailable.': { en: 'System status is unavailable.', zh: '系统状态不可用。' },
  'System status has not been loaded yet.': { en: 'System status has not been loaded yet.', zh: '系统状态尚未加载。' },
  'Empty means the shared API responded successfully, but this page does not have records or summary data yet.': {
    en: 'Empty means the shared API responded successfully, but this page does not have records or summary data yet.',
    zh: '空表示共享 API 已成功响应，但当前页面还没有记录或摘要数据。',
  },
  'System status is degraded.': { en: 'System status is degraded.', zh: '系统状态已降级。' },
  'Degraded means /api/system/status reported a shared runtime warning even though reads may still succeed.': {
    en: 'Degraded means /api/system/status reported a shared runtime warning even though reads may still succeed.',
    zh: '降级表示 /api/system/status 报告了共享运行时告警，即使读取仍可能成功。',
  },
  'System status is ready.': { en: 'System status is ready.', zh: '系统状态已就绪。' },
  'The shared API, database, migrations, and task runtime are available for workbench and dashboard reads.': {
    en: 'The shared API, database, migrations, and task runtime are available for workbench and dashboard reads.',
    zh: '共享 API、数据库、迁移和任务运行时均可用于工作台和总览读取。',
  },
  'API: {value}': { en: 'API: {value}', zh: 'API：{value}' },
  'Database: {value}': { en: 'Database: {value}', zh: '数据库：{value}' },
  'Migration: {value}': { en: 'Migration: {value}', zh: '迁移：{value}' },
  'Task runtime: {value}': { en: 'Task runtime: {value}', zh: '任务运行时：{value}' },
  'Last checked: {value}': { en: 'Last checked: {value}', zh: '最近检查：{value}' },
  'Degraded reason: {value}': { en: 'Degraded reason: {value}', zh: '降级原因：{value}' },
  'Recovery note: {value}': { en: 'Recovery note: {value}', zh: '恢复提示：{value}' },
  'Local Continuity': { en: 'Local Continuity', zh: '本地连续性' },
  'Backup, restore, and bounded capture transfer stay explicit and local-first here. SQLite remains the single source of truth, and this section stays support-level rather than becoming an admin console.': {
    en: 'Backup, restore, and bounded capture transfer stay explicit and local-first here. SQLite remains the single source of truth, and this section stays support-level rather than becoming an admin console.',
    zh: '这里的备份、恢复和有界采集传输都保持显式且本地优先。SQLite 仍是唯一真相源，此分区保持支持层级，而不是变成管理控制台。',
  },
  'Local continuity support is unavailable.': { en: 'Local continuity support is unavailable.', zh: '本地连续性支持不可用。' },
  'Local continuity is currently empty.': { en: 'Local continuity is currently empty.', zh: '本地连续性当前为空。' },
  'Local continuity support has not been loaded yet.': { en: 'Local continuity support has not been loaded yet.', zh: '本地连续性支持尚未加载。' },
  'Local SQLite context': { en: 'Local SQLite context', zh: '本地 SQLite 上下文' },
  'Single source of truth': { en: 'Single source of truth', zh: '唯一真相源' },
  'Local backup': { en: 'Local backup', zh: '本地备份' },
  'Full SQLite copy': { en: 'Full SQLite copy', zh: '完整 SQLite 副本' },
  'Backup Path (Optional)': { en: 'Backup Path (Optional)', zh: '备份路径（可选）' },
  'Create local backup': { en: 'Create local backup', zh: '创建本地备份' },
  'Local restore': { en: 'Local restore', zh: '本地恢复' },
  'Bounded replacement': { en: 'Bounded replacement', zh: '有界替换' },
  'Backup File Path': { en: 'Backup File Path', zh: '备份文件路径' },
  'Create a safety backup of the current SQLite file before restore.': {
    en: 'Create a safety backup of the current SQLite file before restore.',
    zh: '恢复前先为当前 SQLite 文件创建一个安全备份。',
  },
  'I understand restore replaces the active local SQLite database file.': {
    en: 'I understand restore replaces the active local SQLite database file.',
    zh: '我理解恢复会替换当前正在使用的本地 SQLite 数据库文件。',
  },
  'Restore local database': { en: 'Restore local database', zh: '恢复本地数据库' },
  'Capture bundle export': { en: 'Capture bundle export', zh: '采集包导出' },
  'Bounded transfer': { en: 'Bounded transfer', zh: '有界传输' },
  'Export File Path (Optional)': { en: 'Export File Path (Optional)', zh: '导出文件路径（可选）' },
  'Export capture bundle': { en: 'Export capture bundle', zh: '导出采集包' },
  'Capture bundle import': { en: 'Capture bundle import', zh: '采集包导入' },
  'Existing intake path': { en: 'Existing intake path', zh: '现有接入路径' },
  'Import File Path': { en: 'Import File Path', zh: '导入文件路径' },
  'Import capture bundle': { en: 'Import capture bundle', zh: '导入采集包' },
  'Open capture records': { en: 'Open capture records', zh: '打开采集记录' },
  'Local daily-use continuity is ready.': { en: 'Local daily-use continuity is ready.', zh: '本地日常使用连续性已就绪。' },
  'Daily-use transition needs attention.': { en: 'Daily-use transition needs attention.', zh: '日常使用过渡需要关注。' },
  'Local backup created.': { en: 'Local backup created.', zh: '本地备份已创建。' },
  'SQLite backup was written to {path}.': { en: 'SQLite backup was written to {path}.', zh: 'SQLite 备份已写入 {path}。' },
  'Active database: {path}': { en: 'Active database: {path}', zh: '当前数据库：{path}' },
  'Backup size: {size}': { en: 'Backup size: {size}', zh: '备份大小：{size}' },
  'Created at: {value}': { en: 'Created at: {value}', zh: '创建时间：{value}' },
  'Local database restored.': { en: 'Local database restored.', zh: '本地数据库已恢复。' },
  'SQLite database was restored from {path}.': { en: 'SQLite database was restored from {path}.', zh: 'SQLite 数据库已从 {path} 恢复。' },
  'Safety backup: {path}': { en: 'Safety backup: {path}', zh: '安全备份：{path}' },
  'Safety backup was skipped.': { en: 'Safety backup was skipped.', zh: '已跳过安全备份。' },
  'Restored at: {value}': { en: 'Restored at: {value}', zh: '恢复时间：{value}' },
  'Capture bundle exported.': { en: 'Capture bundle exported.', zh: '采集包已导出。' },
  'Bounded capture transfer file was written to {path}.': {
    en: 'Bounded capture transfer file was written to {path}.',
    zh: '有界采集传输文件已写入 {path}。',
  },
  'Included capture items: {count}': { en: 'Included capture items: {count}', zh: '包含的采集项：{count}' },
  'Skipped items without raw text: {count}': { en: 'Skipped items without raw text: {count}', zh: '跳过的无原始文本项：{count}' },
  'Capture bundle imported.': { en: 'Capture bundle imported.', zh: '采集包已导入。' },
  'Capture bundle import created {count} new capture records through the existing intake path.': {
    en: 'Capture bundle import created {count} new capture records through the existing intake path.',
    zh: '采集包导入已通过现有接入路径创建 {count} 条新的采集记录。',
  },
  'Pending review routes: {count}': { en: 'Pending review routes: {count}', zh: '进入待审路径：{count}' },
  'Direct committed routes: {count}': { en: 'Direct committed routes: {count}', zh: '直接提交路径：{count}' },
  'Imported at: {value}': { en: 'Imported at: {value}', zh: '导入时间：{value}' },
  'Local continuity action failed.': { en: 'Local continuity action failed.', zh: '本地连续性操作失败。' },
  'Restore could not start.': { en: 'Restore could not start.', zh: '恢复无法启动。' },
  'Restore must be explicitly confirmed because it replaces the active local SQLite database file.': {
    en: 'Restore must be explicitly confirmed because it replaces the active local SQLite database file.',
    zh: '由于恢复会替换当前正在使用的本地 SQLite 数据库文件，因此必须显式确认。',
  },
  'Creating backup...': { en: 'Creating backup...', zh: '正在创建备份...' },
  'Restoring database...': { en: 'Restoring database...', zh: '正在恢复数据库...' },
  'Exporting capture bundle...': { en: 'Exporting capture bundle...', zh: '正在导出采集包...' },
  'Importing capture bundle...': { en: 'Importing capture bundle...', zh: '正在导入采集包...' },
  'Working...': { en: 'Working...', zh: '处理中...' },
  'Confirming...': { en: 'Confirming...', zh: '正在确认...' },
  'Discarding...': { en: 'Discarding...', zh: '正在丢弃...' },
  'Force inserting...': { en: 'Force inserting...', zh: '正在强制写入...' },
  '{label} failed.': { en: '{label} failed.', zh: '{label}失败。' },
  'Fix could not be applied.': { en: 'Fix could not be applied.', zh: '修正无法应用。' },
  'Correction text is required before the corrected payload can be updated.': {
    en: 'Correction text is required before the corrected payload can be updated.',
    zh: '更新修正后的载荷前，必须填写修正文案。',
  },
  'Updating corrected payload...': { en: 'Updating corrected payload...', zh: '正在更新修正后的载荷...' },
  'Corrected payload updated.': { en: 'Corrected payload updated.', zh: '修正后的载荷已更新。' },
  'Fix updates corrected payload only. The pending item remains reviewable until you confirm, discard, or force insert it.': {
    en: 'Fix updates corrected payload only. The pending item remains reviewable until you confirm, discard, or force insert it.',
    zh: '修正只会更新修正后的载荷。在你确认、丢弃或强制写入之前，该待审项仍可继续审核。',
  },
  'Pending item confirmed.': { en: 'Pending item confirmed.', zh: '待审项已确认。' },
  'Confirm wrote the current effective payload to the formal record and resolved the pending item.': {
    en: 'Confirm wrote the current effective payload to the formal record and resolved the pending item.',
    zh: '确认已将当前生效载荷写入正式记录，并解决该待审项。',
  },
  'Pending item discarded.': { en: 'Pending item discarded.', zh: '待审项已丢弃。' },
  'Discard resolved the pending item without writing a formal record.': {
    en: 'Discard resolved the pending item without writing a formal record.',
    zh: '丢弃已在不写入正式记录的情况下解决该待审项。',
  },
  'Pending item force inserted.': { en: 'Pending item force inserted.', zh: '待审项已强制写入。' },
  'Force Insert wrote the current effective payload through the backend force-insert path and resolved the pending item.': {
    en: 'Force Insert wrote the current effective payload through the backend force-insert path and resolved the pending item.',
    zh: '强制写入已通过后端 force-insert 路径写入当前生效载荷，并解决该待审项。',
  },
  '1. Current Mode': { en: '1. Current Mode', zh: '1. 当前模式' },
  '2. Template Work Modes': { en: '2. Template Work Modes', zh: '2. 模板工作模式' },
  '3. Common Entry Paths': { en: '3. Common Entry Paths', zh: '3. 常用入口路径' },
  'Entry layer for current mode, what matters now, and where to go next.': {
    en: 'Entry layer for current mode, what matters now, and where to go next.',
    zh: '用于了解当前模式、当前重点以及下一步去向的入口层。',
  },
  'Current mode only changes entry context. It does not change formal record semantics. It helps explain what to open next.': {
    en: 'Current mode only changes entry context. It does not change formal record semantics. It helps explain what to open next.',
    zh: '当前模式只会改变入口上下文，不会改变正式记录语义。它帮助说明下一步应打开什么。',
  },
  'No active mode': { en: 'No active mode', zh: '当前无激活模式' },
  'Active mode': { en: 'Active mode', zh: '激活模式' },
  'Select a template to set the current work mode.': {
    en: 'Select a template to set the current work mode.',
    zh: '选择一个模板来设置当前工作模式。',
  },
  'Entry Target': { en: 'Entry Target', zh: '入口目标' },
  'Default Entry Mode': { en: 'Default Entry Mode', zh: '默认入口模式' },
  'Scoped Shortcuts': { en: 'Scoped Shortcuts', zh: '限定快捷入口' },
  'Default Filters': { en: 'Default Filters', zh: '默认筛选' },
  'Preferences Updated': { en: 'Preferences Updated', zh: '偏好更新时间' },
  'Open current context': { en: 'Open current context', zh: '打开当前上下文' },
  'What matters now': { en: 'What matters now', zh: '当前重点' },
  'Entry cues': { en: 'Entry cues', zh: '入口提示' },
  'Dashboard support is temporarily unavailable, but mode context and entry paths remain readable.': {
    en: 'Dashboard support is temporarily unavailable, but mode context and entry paths remain readable.',
    zh: '总览支持暂时不可用，但模式上下文和入口路径仍可读取。',
  },
  'These are support cues for choosing the next page. They do not turn Workbench into a dashboard center.': {
    en: 'These are support cues for choosing the next page. They do not turn Workbench into a dashboard center.',
    zh: '这些是帮助选择下一页的支持提示，不会把工作台变成总览中心。',
  },
  'Open Pending': { en: 'Open Pending', zh: '打开的待审项' },
  'Open Alerts': { en: 'Open Alerts', zh: '打开的提醒' },
  'Pinned Entry Paths': { en: 'Pinned Entry Paths', zh: '固定入口路径' },
  'Recent Contexts': { en: 'Recent Contexts', zh: '最近上下文' },
  'Open pending review': { en: 'Open pending review', zh: '打开待审审核' },
  'Continue current mode': { en: 'Continue current mode', zh: '继续当前模式' },
  'Open dashboard summary': { en: 'Open dashboard summary', zh: '打开总览摘要' },
  'Templates are structured work-mode entry points. They set shared entry context and default reads; they do not execute actions or automate workflows.': {
    en: 'Templates are structured work-mode entry points. They set shared entry context and default reads; they do not execute actions or automate workflows.',
    zh: '模板是结构化的工作模式入口。它们设置共享入口上下文和默认读取，但不会执行动作或自动化工作流。',
  },
  'Built-in modes': { en: 'Built-in modes', zh: '内置模式' },
  'User modes': { en: 'User modes', zh: '用户模式' },
  'Stable workbench entry points for common domains and review contexts.': {
    en: 'Stable workbench entry points for common domains and review contexts.',
    zh: '面向常见域和审核上下文的稳定工作台入口。',
  },
  'Lightweight repeated views built from existing read filters and entry defaults.': {
    en: 'Lightweight repeated views built from existing read filters and entry defaults.',
    zh: '基于现有读取筛选和默认入口构建的轻量重复视图。',
  },
  'No user work modes yet.': { en: 'No user work modes yet.', zh: '还没有用户工作模式。' },
  'No description': { en: 'No description', zh: '无描述' },
  Active: { en: 'Active', zh: '激活' },
  'Default entry': { en: 'Default entry', zh: '默认入口' },
  'Current mode': { en: 'Current mode', zh: '当前模式' },
  'Set current mode': { en: 'Set current mode', zh: '设为当前模式' },
  'Set default entry': { en: 'Set default entry', zh: '设为默认入口' },
  'Edit mode': { en: 'Edit mode', zh: '编辑模式' },
  'Disable mode': { en: 'Disable mode', zh: '禁用模式' },
  'Enable mode': { en: 'Enable mode', zh: '启用模式' },
  'Current mode updated.': { en: 'Current mode updated.', zh: '当前工作模式已更新。' },
  'Work mode updated and set as the default entry.': {
    en: 'Work mode updated and set as the default entry.',
    zh: '工作模式已更新并设为默认入口。',
  },
  'Work mode disabled.': { en: 'Work mode disabled.', zh: '工作模式已禁用。' },
  'Work mode enabled.': { en: 'Work mode enabled.', zh: '工作模式已启用。' },
  'Delete this shortcut?': { en: 'Delete this shortcut?', zh: '删除这个快捷入口？' },
  'Shortcut disabled.': { en: 'Shortcut disabled.', zh: '快捷入口已禁用。' },
  'Shortcut enabled.': { en: 'Shortcut enabled.', zh: '快捷入口已启用。' },
  'Shortcut deleted.': { en: 'Shortcut deleted.', zh: '快捷入口已删除。' },
  'Work mode saved.': { en: 'Work mode saved.', zh: '工作模式已保存。' },
  'Work mode created.': { en: 'Work mode created.', zh: '工作模式已创建。' },
  'Shortcut saved.': { en: 'Shortcut saved.', zh: '快捷入口已保存。' },
  'Shortcut created.': { en: 'Shortcut created.', zh: '快捷入口已创建。' },
  Home: { en: 'Home', zh: '主页' },
  'Pending Review': { en: 'Pending Review', zh: '待审审核' },
  'Default workbench landing mode.': { en: 'Default workbench landing mode.', zh: '默认的工作台落地模式。' },
  'Focus on open pending items.': { en: 'Focus on open pending items.', zh: '聚焦仍处于打开状态的待审项。' },
  'Continue expense work from the main workbench.': {
    en: 'Continue expense work from the main workbench.',
    zh: '从主工作台继续处理支出工作。',
  },
  'Not set': { en: 'Not set', zh: '未设置' },
  None: { en: 'None', zh: '无' },
  Yes: { en: 'Yes', zh: '是' },
  No: { en: 'No', zh: '否' },
  'No target payload.': { en: 'No target payload.', zh: '没有目标载荷。' },
  'Route: {value}': { en: 'Route: {value}', zh: '路由：{value}' },
  'view {value}': { en: 'view {value}', zh: '视图 {value}' },
  'JSON input is invalid.': { en: 'JSON input is invalid.', zh: 'JSON 输入无效。' },
} as const

type SharedCopyKey = keyof typeof SHARED_COPY

const STATUS_LABELS: Record<UiLocale, Record<string, string>> = {
  en: {
    open: 'Open',
    acknowledged: 'Acknowledged',
    resolved: 'Resolved',
    pending: 'Pending',
    running: 'Running',
    failed: 'Failed',
    invalidated: 'Invalidated',
    unavailable: 'Unavailable',
    degraded: 'Degraded',
    ready: 'Ready',
    empty: 'Empty',
    confirmed: 'Confirmed',
    discarded: 'Discarded',
    forced: 'Forced',
    force_insert: 'Force Insert',
    builtin: 'Builtin',
    user: 'User',
    received: 'Received',
    parsed: 'Parsed',
    committed: 'Committed',
    formal_record: 'Formal Record',
    daily_use_ready: 'Daily Use Ready',
    dismissed: 'Disabled',
  },
  zh: {
    open: '打开',
    acknowledged: '已确认',
    resolved: '已解决',
    pending: '待处理',
    running: '运行中',
    failed: '失败',
    invalidated: '已失效',
    unavailable: '不可用',
    degraded: '已降级',
    ready: '就绪',
    empty: '空',
    confirmed: '已确认',
    discarded: '已丢弃',
    forced: '已强制',
    force_insert: '强制写入',
    builtin: '内置',
    user: '用户',
    received: '已接收',
    parsed: '已解析',
    committed: '已提交',
    formal_record: '正式记录',
    daily_use_ready: '适合日常使用',
    dismissed: '已禁用',
  },
}

const DOMAIN_LABELS: Record<UiLocale, Record<string, string>> = {
  en: {
    dashboard: 'Dashboard',
    pending: 'Pending',
    expense: 'Expense',
    knowledge: 'Knowledge',
    health: 'Health',
    alerts: 'Alerts',
    capture: 'Capture',
  },
  zh: {
    dashboard: '总览',
    pending: '待审',
    expense: '支出',
    knowledge: '知识',
    health: '健康',
    alerts: '提醒',
    capture: '采集',
  },
}

let renderToken = 0
const captureUiState: CaptureUiState = {
  feedback: null,
  submissionDraft: {
    rawText: '',
    sourceRef: '',
  },
}
const quickCaptureUiState: QuickCaptureUiState = {
  feedback: null,
  draftText: '',
}
const bulkIntakeUiState: BulkIntakeUiState = {
  selectedFileName: '',
  fileText: '',
  preview: null,
  feedback: null,
}
const workbenchUiState: WorkbenchUiState = {
  editingTemplateId: null,
  editingShortcutId: null,
  flash: null,
}
const pendingUiState: PendingUiState = {
  feedbackById: {},
  fixDraftById: {},
}
const localOperabilityUiState: LocalOperabilityUiState = {
  feedback: null,
  backupDestinationPath: '',
  restoreSourcePath: '',
  restoreCreateSafetyBackup: true,
  restoreConfirmed: false,
  exportDestinationPath: '',
  importSourcePath: '',
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

setDocumentLocale(localeState.current)
void renderApp()

function sc(key: SharedCopyKey, values: Record<string, string | number> = {}): string {
  const template = String(SHARED_COPY[key][localeState.current] ?? SHARED_COPY[key].en)
  return Object.entries(values).reduce<string>(
    (message, [name, value]) => message.replaceAll(`{${name}}`, String(value)),
    template,
  )
}

function lc(en: string, zh: string): string {
  return localeState.current === 'zh' ? zh : en
}

function localizeUiLabel(label: string): string {
  switch (label) {
    case 'Filters':
      return lc('Filters', '筛选条件')
    case 'Apply filters':
      return lc('Apply filters', '应用筛选')
    case 'Reset':
      return lc('Reset', '重置')
    case 'Date From':
      return lc('Date From', '开始日期')
    case 'Date To':
      return lc('Date To', '结束日期')
    case 'Status':
      return lc('Status', '状态')
    case 'Source Type':
      return lc('Source Type', '来源类型')
    case 'Sort By':
      return lc('Sort By', '排序字段')
    case 'Sort Order':
      return lc('Sort Order', '排序方向')
    case 'Page Size':
      return lc('Page Size', '每页数量')
    case 'Category':
      return lc('Category', '类别')
    case 'Keyword':
      return lc('Keyword', '关键词')
    case 'Metric Type':
      return lc('Metric Type', '指标类型')
    case 'Has Source Text':
      return lc('Has Source Text', '是否有来源文本')
    case 'All':
      return lc('All', '全部')
    case 'None':
      return lc('None', '无')
    case 'Label':
      return lc('Label', '标签')
    case 'Route':
      return lc('Route', '路由')
    case 'View key':
      return lc('View key', '视图键')
    case 'Query JSON':
      return lc('Query JSON', '查询 JSON')
    case 'Enabled':
      return lc('Enabled', '启用')
    case 'Capture ID':
      return lc('Capture ID', '采集 ID')
    case 'Current Summary':
      return lc('Current Summary', '当前摘要')
    case 'Current Status':
      return lc('Current Status', '当前状态')
    case 'Current Stage':
      return lc('Current Stage', '当前阶段')
    case 'Target Domain':
      return lc('Target Domain', '目标域')
    case 'Source Reference':
      return lc('Source Reference', '来源说明')
    case 'Created At':
      return lc('Created At', '创建时间')
    case 'Updated At':
      return lc('Updated At', '更新时间')
    case 'Finalized At':
      return lc('Finalized At', '最终完成时间')
    case 'Chain Summary':
      return lc('Chain Summary', '链路摘要')
    case 'Filtered Total':
      return lc('Filtered Total', '筛选后总数')
    case 'Current Status Filter':
      return lc('Current Status Filter', '当前状态筛选')
    case 'Current Source Filter':
      return lc('Current Source Filter', '当前来源筛选')
    case 'Status Note':
      return lc('Status Note', '状态说明')
    case 'Accepted Input':
      return lc('Accepted Input', '接受输入')
    case 'Submission Source Type':
      return lc('Submission Source Type', '提交来源类型')
    case 'Scope Note':
      return lc('Scope Note', '范围说明')
    case 'Primary Raw Input':
      return lc('Primary Raw Input', '主要原始输入')
    case 'Read Mode':
      return lc('Read Mode', '读取方式')
    case 'Parse Result ID':
      return lc('Parse Result ID', '解析结果 ID')
    case 'Confidence Level':
      return lc('Confidence Level', '置信级别')
    case 'Confidence Score':
      return lc('Confidence Score', '置信分数')
    case 'Parser':
      return lc('Parser', '解析器')
    case 'Parsed At':
      return lc('Parsed At', '解析时间')
    case 'Pending Status':
      return lc('Pending Status', '待审状态')
    case 'Pending Summary':
      return lc('Pending Summary', '待审摘要')
    case 'Reviewability':
      return lc('Reviewability', '可审性')
    case 'Resolved At':
      return lc('Resolved At', '解决时间')
    case 'Result Summary':
      return lc('Result Summary', '结果摘要')
    case 'Resolution Path':
      return lc('Resolution Path', '解决路径')
    case 'Pending ID':
      return lc('Pending ID', '待审 ID')
    case 'Formal Record ID':
      return lc('Formal Record ID', '正式记录 ID')
    case 'Reason':
      return lc('Reason', '原因')
    case 'Review Basis':
      return lc('Review Basis', '审查依据')
    case 'Actionability':
      return lc('Actionability', '可操作性')
    case 'Payload Origin Note':
      return lc('Payload Origin Note', '载荷来源说明')
    case 'Source Capture ID':
      return lc('Source Capture ID', '来源采集 ID')
    case 'Source Relationship':
      return lc('Source Relationship', '来源关系')
    case 'Intake Reason':
      return lc('Intake Reason', '进入原因')
    case 'Next to Review':
      return lc('Next to Review', '下一个待审项')
    case 'Current Domain Filter':
      return lc('Current Domain Filter', '当前域筛选')
    case 'Queue Note':
      return lc('Queue Note', '队列说明')
    case 'Reason Preview':
      return lc('Reason Preview', '原因预览')
    case 'Has Corrected Payload':
      return lc('Has Corrected Payload', '是否有修正载荷')
    case 'Current Category Filter':
      return lc('Current Category Filter', '当前类别筛选')
    case 'Current Keyword Filter':
      return lc('Current Keyword Filter', '当前关键词筛选')
    case 'Current Sort':
      return lc('Current Sort', '当前排序')
    case 'Expense ID':
      return lc('Expense ID', '支出 ID')
    case 'Recorded At':
      return lc('Recorded At', '记录时间')
    case 'Amount':
      return lc('Amount', '金额')
    case 'Currency':
      return lc('Currency', '币种')
    case 'Record Path':
      return lc('Record Path', '记录路径')
    case 'Formal Note':
      return lc('Formal Note', '正式备注')
    case 'Source Pending ID':
      return lc('Source Pending ID', '来源待审 ID')
    case 'Source Path':
      return lc('Source Path', '来源路径')
    case 'Knowledge ID':
      return lc('Knowledge ID', '知识 ID')
    case 'Title':
      return lc('Title', '标题')
    case 'Content':
      return lc('Content', '内容')
    case 'Source Text':
      return lc('Source Text', '来源文本')
    case 'ID':
      return lc('ID', 'ID')
    case 'Value Text':
      return lc('Value Text', '数值文本')
    case 'Note':
      return lc('Note', '备注')
    case 'Content Preview':
      return lc('Content Preview', '内容预览')
    case 'Has Source Pending':
      return lc('Has Source Pending', '是否有来源待审')
    case 'Value Text Preview':
      return lc('Value Text Preview', '数值文本预览')
    case 'Note Preview':
      return lc('Note Preview', '备注预览')
    case 'Rule Key':
      return lc('Rule Key', '规则键')
    case 'Source Record':
      return lc('Source Record', '来源记录')
    case 'Severity':
      return lc('Severity', '严重级别')
    case 'Triggered At':
      return lc('Triggered At', '触发时间')
    case 'Acknowledged At':
      return lc('Acknowledged At', '确认时间')
    case 'Message':
      return lc('Message', '消息')
    case 'Explanation':
      return lc('Explanation', '说明')
    case 'Resolution Note':
      return lc('Resolution Note', '解决说明')
    case 'Derivation Status':
      return lc('Derivation Status', '派生状态')
    case 'Model Key':
      return lc('Model Key', '模型键')
    case 'Model Version':
      return lc('Model Version', '模型版本')
    case 'Last generated summary':
      return lc('Last generated summary', '上次生成摘要')
    case 'Open Pending':
      return lc('Open Pending', '打开待审数')
    case 'Open Alerts':
      return lc('Open Alerts', '打开提醒数')
    case 'Opened in last 7 days':
      return lc('Opened in last 7 days', '近 7 天打开')
    case 'Resolved in last 7 days':
      return lc('Resolved in last 7 days', '近 7 天已解决')
    case 'Created this month':
      return lc('Created this month', '本月创建')
    case 'Latest expense':
      return lc('Latest expense', '最新支出')
    case 'Amount by currency':
      return lc('Amount by currency', '按币种金额')
    case 'Created in last 7 days':
      return lc('Created in last 7 days', '近 7 天创建')
    case 'Created in last 30 days':
      return lc('Created in last 30 days', '近 30 天创建')
    case 'Latest knowledge entry':
      return lc('Latest knowledge entry', '最新知识记录')
    case 'Latest health record':
      return lc('Latest health record', '最新健康记录')
    case 'Recent metric types':
      return lc('Recent metric types', '最近指标类型')
    case 'Pinned Entry Paths':
      return lc('Pinned Entry Paths', '置顶入口路径')
    case 'Recent Contexts':
      return lc('Recent Contexts', '最近上下文')
    case 'Entry Target':
      return lc('Entry Target', '入口目标')
    case 'Default Entry Mode':
      return lc('Default Entry Mode', '默认入口模式')
    case 'Scoped Shortcuts':
      return lc('Scoped Shortcuts', '限定快捷入口')
    case 'Default Filters':
      return lc('Default Filters', '默认筛选')
    case 'Preferences Updated':
      return lc('Preferences Updated', '偏好更新时间')
    case 'Expense This Month':
      return lc('Expense This Month', '本月支出记录')
    case 'Knowledge Last 7 Days':
      return lc('Knowledge Last 7 Days', '近 7 天知识记录')
    case 'Health Last 7 Days':
      return lc('Health Last 7 Days', '近 7 天健康记录')
    case 'Database Path':
      return lc('Database Path', '数据库路径')
    case 'Database File Present':
      return lc('Database File Present', '数据库文件存在')
    case 'Backup Directory':
      return lc('Backup Directory', '备份目录')
    case 'Transfer Directory':
      return lc('Transfer Directory', '传输目录')
    case 'Daily-use Readiness':
      return lc('Daily-use Readiness', '日常使用准备度')
    case 'Pagination':
      return lc('Pagination', '分页')
    case 'Previous':
      return lc('Previous', '上一页')
    case 'Next':
      return lc('Next', '下一页')
    case 'Open':
      return lc('Open', '打开')
    case 'Resume':
      return lc('Resume', '继续')
    case 'Open record':
      return lc('Open record', '打开记录')
    case 'Open formal record':
      return lc('Open formal record', '打开正式记录')
    case 'Open pending detail':
      return lc('Open pending detail', '打开待审详情')
    case 'Open source capture':
      return lc('Open source capture', '打开来源采集')
    case 'Open source pending':
      return lc('Open source pending', '打开来源待审')
    case 'View alerts':
      return lc('View alerts', '查看提醒')
    case 'View pending':
      return lc('View pending', '查看待审')
    case 'View expenses':
      return lc('View expenses', '查看支出')
    case 'View knowledge':
      return lc('View knowledge', '查看知识')
    case 'View health':
      return lc('View health', '查看健康')
    case 'Open context':
      return lc('Open context', '打开上下文')
    case 'Create Capture Record':
      return lc('Create Capture Record', '创建采集记录')
    case 'Captured Text':
      return lc('Captured Text', '采集文本')
    case 'Backup Path (Optional)':
      return lc('Backup Path (Optional)', '备份路径（可选）')
    case 'Backup File Path':
      return lc('Backup File Path', '备份文件路径')
    case 'Export File Path (Optional)':
      return lc('Export File Path (Optional)', '导出文件路径（可选）')
    case 'Import File Path':
      return lc('Import File Path', '导入文件路径')
    case 'Plain text only':
      return lc('Plain text only', '仅纯文本')
    case 'manual':
      return lc('manual', '手动')
    case 'Direct from Capture':
      return lc('Direct from Capture', '直接来自采集')
    case 'Reviewed from Pending':
      return lc('Reviewed from Pending', '经待审复核')
    default:
      return label
  }
}

function setUiLocale(nextLocale: UiLocale): void {
  localeState.current = nextLocale
  persistLocale(nextLocale)
  setDocumentLocale(nextLocale)
}

function setDocumentLocale(locale: UiLocale): void {
  document.documentElement.lang = locale === 'zh' ? 'zh-CN' : 'en'
}

function renderLocaleToggle(): string {
  return `
    <div class="locale-toggle" role="group" aria-label="${escapeHtml(sc('Language'))}">
      <span class="locale-toggle__label">${escapeHtml(sc('Language'))}</span>
      <button
        class="locale-toggle__button${localeState.current === 'en' ? ' is-active' : ''}"
        type="button"
        data-locale-choice="en"
        aria-pressed="${localeState.current === 'en' ? 'true' : 'false'}"
      >
        ${escapeHtml(sc('English'))}
      </button>
      <button
        class="locale-toggle__button${localeState.current === 'zh' ? ' is-active' : ''}"
        type="button"
        data-locale-choice="zh"
        aria-pressed="${localeState.current === 'zh' ? 'true' : 'false'}"
      >
        ${escapeHtml(sc('Chinese'))}
      </button>
    </div>
  `
}

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
  applyRoutePostRenderEffects(route)
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
          pageTitle: sc('Workbench'),
          documentTitle: sc('Workbench'),
        }
      case 'dashboard':
        return {
          kind: 'dashboard',
          section: 'dashboard',
          pageTitle: sc('Dashboard'),
          documentTitle: sc('Dashboard'),
        }
      case 'quick-capture':
        return {
          kind: 'quick-capture',
          section: 'capture',
          pageTitle: sc('Quick Capture'),
          documentTitle: sc('Quick Capture'),
        }
      case 'bulk-intake':
        return {
          kind: 'bulk-intake',
          section: 'capture',
          pageTitle: sc('Bulk Intake'),
          documentTitle: sc('Bulk Intake'),
        }
      case 'capture':
        return { kind: 'capture-list', section: 'capture', pageTitle: sc('Capture'), documentTitle: sc('Capture') }
      case 'pending':
        return { kind: 'pending-list', section: 'pending', pageTitle: sc('Pending'), documentTitle: sc('Pending') }
      case 'expense':
        return { kind: 'expense-list', section: 'expense', pageTitle: sc('Expenses'), documentTitle: sc('Expenses') }
      case 'knowledge':
        return {
          kind: 'knowledge-list',
          section: 'knowledge',
          pageTitle: sc('Knowledge'),
          documentTitle: sc('Knowledge'),
        }
      case 'health':
        return { kind: 'health-list', section: 'health', pageTitle: sc('Health'), documentTitle: sc('Health') }
      default:
        return { kind: 'redirect', to: '/workbench' }
    }
  }

  if (parts.length === 2) {
    if (parts[0] === 'capture') {
      return {
        kind: 'capture-detail',
        section: 'capture',
        pageTitle: sc('Capture Record'),
        documentTitle: sc('Capture Record'),
        id: parts[1],
      }
    }
    if (parts[0] === 'pending') {
      return {
        kind: 'pending-detail',
        section: 'pending',
        pageTitle: sc('Pending Item'),
        documentTitle: sc('Pending Item'),
        id: parts[1],
      }
    }
    if (parts[0] === 'expense') {
      return {
        kind: 'expense-detail',
        section: 'expense',
        pageTitle: sc('Expense Record'),
        documentTitle: sc('Expense Record'),
        id: parts[1],
      }
    }
    if (parts[0] === 'knowledge') {
      return {
        kind: 'knowledge-detail',
        section: 'knowledge',
        pageTitle: sc('Knowledge Record'),
        documentTitle: sc('Knowledge Record'),
        id: parts[1],
      }
    }
    if (parts[0] === 'health') {
      return {
        kind: 'health-detail',
        section: 'health',
        pageTitle: sc('Health Record'),
        documentTitle: sc('Health Record'),
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
    case 'quick-capture':
      return renderQuickCapturePage()
    case 'bulk-intake':
      return renderBulkIntakePage()
    case 'capture-list':
      return renderCaptureListPage()
    case 'capture-detail':
      return renderCaptureDetailPage(route.id)
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

async function renderQuickCapturePage(): Promise<string> {
  return renderQuickCaptureView()
}

async function renderBulkIntakePage(): Promise<string> {
  return renderBulkIntakeView()
}

async function renderWorkbenchPage(): Promise<string> {
  const [homeResult, dashboardResult, shortcutsResult, preferencesResult, runtimeStatusResult, localOperabilityResult] = await Promise.allSettled([
    fetchWorkbenchHome(),
    fetchDashboard(),
    fetchWorkbenchShortcuts(),
    fetchWorkbenchPreferences(),
    fetchRuntimeStatus(),
    fetchLocalOperability(),
  ])

  if (homeResult.status === 'rejected') {
    return renderPageShell(`
      ${renderPageHeaderBlock({
        title: sc('Workbench'),
        copy: lc('Central entry layer for work modes, shortcuts, recent context, and summary.', '工作模式、快捷入口、最近上下文与摘要支持的中央入口层。'),
      })}
      ${renderRuntimeStatusSection(
        runtimeStatusResult.status === 'fulfilled' ? runtimeStatusResult.value : null,
        runtimeStatusResult.status === 'rejected' ? toErrorMessage(runtimeStatusResult.reason) : null,
      )}
      ${renderLocalOperabilitySection(
        localOperabilityResult.status === 'fulfilled' ? localOperabilityResult.value : null,
        localOperabilityResult.status === 'rejected' ? toErrorMessage(localOperabilityResult.reason) : null,
      )}
      ${renderUnavailableState(lc('Workbench home is unavailable.', '工作台主页不可用。'), toErrorMessage(homeResult.reason))}
    `)
  }

  return renderWorkbenchView(
    homeResult.value,
    dashboardResult.status === 'fulfilled' ? dashboardResult.value : null,
    shortcutsResult.status === 'fulfilled' ? shortcutsResult.value.items : [],
    preferencesResult.status === 'fulfilled' ? preferencesResult.value : null,
    runtimeStatusResult.status === 'fulfilled' ? runtimeStatusResult.value : null,
    localOperabilityResult.status === 'fulfilled' ? localOperabilityResult.value : null,
    {
      dashboardError: dashboardResult.status === 'rejected' ? toErrorMessage(dashboardResult.reason) : null,
      shortcutsError: shortcutsResult.status === 'rejected' ? toErrorMessage(shortcutsResult.reason) : null,
      preferencesError: preferencesResult.status === 'rejected' ? toErrorMessage(preferencesResult.reason) : null,
      runtimeStatusError: runtimeStatusResult.status === 'rejected' ? toErrorMessage(runtimeStatusResult.reason) : null,
      localOperabilityError: localOperabilityResult.status === 'rejected' ? toErrorMessage(localOperabilityResult.reason) : null,
    },
  )
}

function renderWorkbenchView(
  home: WorkbenchHomeData,
  dashboard: DashboardData | null,
  allShortcuts: WorkbenchShortcut[],
  preferences: WorkbenchPreferences | null,
  runtimeStatus: RuntimeStatusData | null,
  localOperability: LocalOperabilityData | null,
  errors: {
    dashboardError: string | null
    shortcutsError: string | null
    preferencesError: string | null
    runtimeStatusError: string | null
    localOperabilityError: string | null
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
      title: sc('Workbench'),
      copy: sc('Entry layer for current mode, what matters now, and where to go next.'),
    })}
    ${renderWorkbenchFlash()}
    ${renderWorkbenchStateBanner(home, dashboard, errors.dashboardError)}
    ${renderWorkbenchCurrentModeSection(home, dashboard, activeTemplate, defaultTemplate, preferences, errors.preferencesError, errors.dashboardError)}
    ${renderWorkbenchTemplatesSection(home, editingTemplate, defaultTemplate?.template_id ?? preferences?.default_template_id ?? null)}
    ${renderWorkbenchShortcutsSection(home, allShortcuts, editingShortcut, errors.shortcutsError)}
    ${renderWorkbenchDashboardSummarySection(dashboard, errors.dashboardError)}
    ${renderWorkbenchRecentSection(home)}
    ${renderRuntimeStatusSection(runtimeStatus, errors.runtimeStatusError)}
    ${renderLocalOperabilitySection(localOperability, errors.localOperabilityError)}
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
  dashboard: DashboardData | null,
  activeTemplate: WorkbenchTemplate | null,
  defaultTemplate: WorkbenchTemplate | null,
  preferences: WorkbenchPreferences | null,
  preferencesError?: string | null,
  dashboardError?: string | null,
): string {
  const currentModeHref = buildWorkbenchModeHref({
    default_module: home.current_mode.default_module,
    default_view_key: home.current_mode.default_view_key,
    default_query_json: home.current_mode.default_query_json,
  })

  return renderSectionShell(
    {
      title: sc('1. Current Mode'),
      copy: sc('Current mode only changes entry context. It does not change formal record semantics. It helps explain what to open next.'),
      className: 'workbench-section section-shell--primary',
    },
    `
      <div class="workbench-mode-grid">
        <article class="record-card">
          <div class="record-card__header">
            <div class="record-card__title-group">
              <h3>${escapeHtml(activeTemplate ? getTemplateDisplayName(activeTemplate) : sc('No active mode'))}</h3>
              <span class="record-meta">${escapeHtml(sc('Active mode'))}</span>
            </div>
            <div class="inline-list">
              ${activeTemplate ? renderBadge(sc('Current mode')) : ''}
              ${activeTemplate ? renderBadge(formatDomainLabel(activeTemplate.default_module)) : ''}
              ${activeTemplate?.template_type ? renderBadge(formatStatusLabel(activeTemplate.template_type), true) : ''}
            </div>
          </div>
          <p class="section-copy">${escapeHtml(activeTemplate ? getTemplateDisplayDescription(activeTemplate) : sc('Select a template to set the current work mode.'))}</p>
          <div class="field-grid">
            ${renderField(sc('Entry Target'), describeWorkbenchModeTarget(activeTemplate?.default_module ?? null, activeTemplate?.default_view_key ?? null))}
            ${renderField(sc('Default Entry Mode'), defaultTemplate ? getTemplateDisplayName(defaultTemplate) : sc('Not set'))}
            ${renderField(sc('Scoped Shortcuts'), String(activeTemplate?.scoped_shortcut_ids.length ?? 0))}
            ${renderField(sc('Default Filters'), summarizeQuery(activeTemplate?.default_query_json))}
            ${
              preferences
                ? renderField(sc('Preferences Updated'), formatDateTime(preferences.updated_at))
                : ''
            }
          </div>
          ${preferencesError ? `<p class="section-copy">${escapeHtml(preferencesError)}</p>` : ''}
          ${
            currentModeHref
              ? `<div class="filter-actions"><a class="record-action" href="${escapeHtml(currentModeHref)}" data-nav="true">${escapeHtml(sc('Open current context'))}</a></div>`
              : ''
          }
        </article>
        <article class="record-card">
          <div class="record-card__header">
            <div class="record-card__title-group">
              <h3>${escapeHtml(sc('What matters now'))}</h3>
              <span class="record-meta">${escapeHtml(sc('Entry cues'))}</span>
            </div>
          </div>
          <p class="section-copy">${
            dashboardError
              ? sc('Dashboard support is temporarily unavailable, but mode context and entry paths remain readable.')
              : sc('These are support cues for choosing the next page. They do not turn Workbench into a dashboard center.')
          }</p>
          <div class="field-grid">
            ${renderField(sc('Open Pending'), dashboard ? String(dashboard.pending_summary.open_count) : sc('Unavailable'))}
            ${renderField(sc('Open Alerts'), dashboard ? String(dashboard.alert_summary.open_count) : sc('Unavailable'))}
            ${renderField(sc('Pinned Entry Paths'), String(home.pinned_shortcuts.length))}
            ${renderField(sc('Recent Contexts'), String(home.recent_contexts.length))}
          </div>
          <div class="filter-actions">
            <a class="record-action" href="/pending" data-nav="true">${escapeHtml(sc('Open pending review'))}</a>
            ${
              currentModeHref
                ? `<a class="record-action" href="${escapeHtml(currentModeHref)}" data-nav="true">${escapeHtml(sc('Continue current mode'))}</a>`
                : `<a class="record-action" href="/dashboard" data-nav="true">${escapeHtml(sc('Open dashboard summary'))}</a>`
            }
          </div>
        </article>
      </div>
    `,
  )
}

function renderWorkbenchTemplatesSection(
  home: WorkbenchHomeData,
  editingTemplate: WorkbenchTemplate | null,
  defaultTemplateId: number | null,
): string {
  const builtinTemplates = home.templates.filter((template) => template.template_type === 'builtin')
  const userTemplates = home.templates.filter((template) => template.template_type === 'user')

  return renderSectionShell(
    {
      title: sc('2. Template Work Modes'),
      copy: sc(
        'Templates are structured work-mode entry points. They set shared entry context and default reads; they do not execute actions or automate workflows.',
      ),
      className: 'workbench-section section-shell--primary',
    },
    `
      <div class="subsection">
        <h3>${escapeHtml(sc('Built-in modes'))}</h3>
        <p class="section-copy">${escapeHtml(sc('Stable workbench entry points for common domains and review contexts.'))}</p>
        <div class="workbench-card-grid">
          ${builtinTemplates.map((template) => renderWorkbenchTemplateCard(template, home.current_mode.template_id, defaultTemplateId)).join('')}
        </div>
      </div>
      <div class="subsection">
        <h3>${escapeHtml(sc('User modes'))}</h3>
        <p class="section-copy">${escapeHtml(sc('Lightweight repeated views built from existing read filters and entry defaults.'))}</p>
        ${
          userTemplates.length > 0
            ? `<div class="workbench-card-grid">
                ${userTemplates.map((template) => renderWorkbenchTemplateCard(template, home.current_mode.template_id, defaultTemplateId)).join('')}
              </div>`
            : renderEmptyState(sc('No user work modes yet.'))
        }
      </div>
      ${renderWorkbenchTemplateForm(home.templates, editingTemplate)}
    `,
  )
}

function renderWorkbenchTemplateCard(
  template: WorkbenchTemplate,
  activeTemplateId: number | null,
  defaultTemplateId: number | null,
): string {
  const isActive = template.template_id === activeTemplateId
  const isDefault = template.template_id === defaultTemplateId
  return `
    <article class="record-card${isActive ? ' record-card--priority' : ''}">
      <div class="record-card__header">
        <div class="record-card__title-group">
          <h3>${escapeHtml(getTemplateDisplayName(template))}</h3>
          <span class="record-meta">${escapeHtml(getTemplateDisplayDescription(template) || sc('No description'))}</span>
        </div>
        <div class="inline-list">
          ${isActive ? renderBadge(sc('Active')) : ''}
          ${isDefault ? renderBadge(sc('Default entry'), true) : ''}
          ${renderBadge(formatStatusLabel(template.template_type), true)}
          ${renderBadge(formatDomainLabel(template.default_module))}
          ${renderStatusBadge(template.is_enabled ? 'open' : 'dismissed')}
        </div>
      </div>
      <div class="field-grid">
        ${renderField(sc('Entry Target'), describeWorkbenchModeTarget(template.default_module, template.default_view_key))}
        ${renderField(sc('Default Filters'), summarizeQuery(template.default_query_json))}
        ${renderField(localeState.current === 'zh' ? '限定入口路径' : 'Scoped Entry Paths', String(template.scoped_shortcut_ids.length))}
        ${renderField(localeState.current === 'zh' ? '更新时间' : 'Updated', formatDateTime(template.updated_at))}
      </div>
      <div class="filter-actions">
        <button
          class="primary-button"
          type="button"
          data-workbench-action="template-apply"
          data-template-id="${template.template_id}"
          ${isActive || !template.is_enabled ? 'disabled' : ''}
        >
          ${isActive ? escapeHtml(sc('Current mode')) : escapeHtml(sc('Set current mode'))}
        </button>
        <button
          class="secondary-button"
          type="button"
          data-workbench-action="template-apply-default"
          data-template-id="${template.template_id}"
          ${isDefault || !template.is_enabled ? 'disabled' : ''}
        >
          ${isDefault ? escapeHtml(sc('Default entry')) : escapeHtml(sc('Set default entry'))}
        </button>
        ${
          template.template_type === 'user'
            ? `
                <button class="secondary-button" type="button" data-workbench-action="template-edit" data-template-id="${template.template_id}">
                  ${escapeHtml(sc('Edit mode'))}
                </button>
                <button
                  class="secondary-button"
                  type="button"
                  data-workbench-action="template-toggle"
                  data-template-id="${template.template_id}"
                  data-enabled="${template.is_enabled ? 'true' : 'false'}"
                >
                  ${escapeHtml(template.is_enabled ? sc('Disable mode') : sc('Enable mode'))}
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
  const title = editingTemplate
    ? (localeState.current === 'zh' ? '编辑用户工作模式' : 'Edit user work mode')
    : (localeState.current === 'zh' ? '创建用户工作模式' : 'Create user work mode')
  return `
    <section class="workbench-form-panel">
      <div class="section-header">
        <h3>${escapeHtml(title)}</h3>
        <p class="section-copy">${escapeHtml(
          localeState.current === 'zh'
            ? '用户工作模式保持为纯入口层。请将它们限制在现有正式读取查询语义内。'
            : 'User work modes stay entry-only. Keep them limited to existing formal read query semantics.',
        )}</p>
      </div>
      <form class="filter-form" data-workbench-form="true" data-workbench-kind="template">
        <input type="hidden" name="template_id" value="${editingTemplate?.template_id ?? ''}" />
        <div class="filter-grid">
          ${renderTextInput('name', localeState.current === 'zh' ? '名称' : 'Name', editingTemplate?.name ?? '')}
          ${renderWorkbenchModuleSelect('default_module', editingTemplate?.default_module ?? 'dashboard')}
          ${renderTextInput('default_view_key', localeState.current === 'zh' ? '默认视图键' : 'Default view key', editingTemplate?.default_view_key ?? '')}
          ${renderTextInput('sort_order', localeState.current === 'zh' ? '排序' : 'Sort order', String(editingTemplate?.sort_order ?? templates.length * 10 + 40))}
          ${renderTextInput('scoped_shortcut_ids', localeState.current === 'zh' ? '限定快捷入口 ID' : 'Scoped shortcut IDs', (editingTemplate?.scoped_shortcut_ids ?? []).join(', '))}
          ${renderTextInput('description', localeState.current === 'zh' ? '描述' : 'Description', editingTemplate?.description ?? '')}
        </div>
        <label class="filter-field">
          <span>${escapeHtml(localeState.current === 'zh' ? '默认查询 JSON' : 'Default query JSON')}</span>
          <textarea class="workbench-textarea" name="default_query_json">${escapeHtml(
            editingTemplate?.default_query_json ? JSON.stringify(editingTemplate.default_query_json, null, 2) : '',
          )}</textarea>
        </label>
        <label class="workbench-checkbox">
          <input type="checkbox" name="is_enabled" ${editingTemplate?.is_enabled ?? true ? 'checked' : ''} />
          <span>${escapeHtml(localeState.current === 'zh' ? '启用' : 'Enabled')}</span>
        </label>
        <div class="filter-actions">
          <button class="primary-button" type="submit">${escapeHtml(editingTemplate ? sc('Save') + (localeState.current === 'zh' ? '工作模式' : ' work mode') : localeState.current === 'zh' ? '创建工作模式' : 'Create work mode')}</button>
          ${
            editingTemplate
              ? `<button class="secondary-button" type="button" data-workbench-action="template-cancel">${escapeHtml(sc('Cancel'))}${localeState.current === 'zh' ? '编辑' : ' edit'}</button>`
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
      title: lc('3. Common Entry Paths', '3. 常用入口路径'),
      copy: lc(
        'After choosing a mode, use shortcuts to open the next formal page or filtered view. They do not run action chains.',
        '选择模式后，可用快捷入口打开下一个正式页面或筛选视图。它们不会执行动作链。',
      ),
      className: 'workbench-section section-shell--secondary',
    },
    `
      <div class="workbench-shortcut-highlight">
        ${renderWorkbenchQuickCaptureCard()}
        ${renderWorkbenchBulkIntakeCard()}
        ${
          home.pinned_shortcuts.length > 0
            ? home.pinned_shortcuts.map((shortcut) => renderWorkbenchShortcutCard(shortcut, true)).join('')
            : renderEmptyState(lc('No pinned shortcuts for the current mode.', '当前模式下没有置顶快捷入口。'))
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

function renderWorkbenchQuickCaptureCard(): string {
  return `
    <article class="record-card record-card--priority">
      <div class="record-card__header">
        <div class="record-card__title-group">
          <h3>${escapeHtml(sc('Quick Capture'))}</h3>
          <span class="record-meta">${escapeHtml(lc('Pure text only', '仅纯文本'))}</span>
        </div>
        <div class="inline-list">
          ${renderBadge(sc('Capture'))}
          ${renderBadge(lc('Web only', '仅限 Web'), true)}
        </div>
      </div>
      <p class="section-copy">${escapeHtml(
        lc(
          'Fastest way to send one text into Capture and stay ready for the next item.',
          '这是把一段文本送入采集链路并保持准备录入下一条的最快方式。',
        ),
      )}</p>
      <div class="filter-actions">
        <a class="record-action" href="/quick-capture" data-nav="true">${escapeHtml(lc('Open quick capture', '打开快速采集'))}</a>
      </div>
    </article>
  `
}

function renderWorkbenchBulkIntakeCard(): string {
  return `
    <article class="record-card">
      <div class="record-card__header">
        <div class="record-card__title-group">
          <h3>${escapeHtml(sc('Bulk Intake'))}</h3>
          <span class="record-meta">${escapeHtml(lc('Text files only', '仅文本文件'))}</span>
        </div>
        <div class="inline-list">
          ${renderBadge(sc('Capture'))}
          ${renderBadge(lc('Preview first', '先预览'), true)}
        </div>
      </div>
      <p class="section-copy">${escapeHtml(
        lc(
          'Import a text file, preview the candidate entries, then send them into Capture without bypassing later review.',
          '导入文本文件、先预览候选项，再把它们送入采集层，而不会绕过后续复核。',
        ),
      )}</p>
      <div class="filter-actions">
        <a class="record-action" href="/bulk-intake" data-nav="true">${escapeHtml(lc('Open bulk intake', '打开批量导入'))}</a>
      </div>
    </article>
  `
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
          ${renderBadge(lc(`Order ${shortcut.sort_order}`, `顺序 ${shortcut.sort_order}`), true)}
        </div>
      </div>
      <p class="section-copy">${escapeHtml(describeShortcutTarget(shortcut))}</p>
      <div class="filter-actions">
        ${
          href
            ? `<a class="record-action" href="${escapeHtml(href)}" data-nav="true">${escapeHtml(localizeUiLabel('Open'))}</a>`
            : `<span class="record-meta">${escapeHtml(lc('No target route', '没有目标路由'))}</span>`
        }
        <button class="secondary-button" type="button" data-workbench-action="shortcut-edit" data-shortcut-id="${shortcut.shortcut_id}">
          ${escapeHtml(lc('Edit', '编辑'))}
        </button>
        <button
          class="secondary-button"
          type="button"
          data-workbench-action="shortcut-toggle"
          data-shortcut-id="${shortcut.shortcut_id}"
          data-enabled="${shortcut.is_enabled ? 'true' : 'false'}"
        >
          ${escapeHtml(shortcut.is_enabled ? lc('Disable', '禁用') : lc('Enable', '启用'))}
        </button>
        <button class="secondary-button" type="button" data-workbench-action="shortcut-delete" data-shortcut-id="${shortcut.shortcut_id}">
          ${escapeHtml(lc('Delete', '删除'))}
        </button>
      </div>
    </article>
  `
}

function renderWorkbenchShortcutForm(editingShortcut: WorkbenchShortcut | null): string {
  const payload = asRecord(editingShortcut?.target_payload_json)
  const title = editingShortcut ? lc('Edit shortcut', '编辑快捷入口') : lc('Create shortcut', '创建快捷入口')
  return `
    <section class="workbench-form-panel">
      <div class="section-header">
        <h3>${escapeHtml(title)}</h3>
        <p class="section-copy">${escapeHtml(
          lc('Shortcut targets stay limited to route or module-view context.', '快捷入口目标仅限于路由或模块视图上下文。'),
        )}</p>
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
          <span>${escapeHtml(localizeUiLabel('Query JSON'))}</span>
          <textarea class="workbench-textarea" name="query_json">${escapeHtml(
            payload?.query ? JSON.stringify(payload.query, null, 2) : '',
          )}</textarea>
        </label>
        <label class="workbench-checkbox">
          <input type="checkbox" name="is_enabled" ${editingShortcut?.is_enabled ?? true ? 'checked' : ''} />
          <span>${escapeHtml(localizeUiLabel('Enabled'))}</span>
        </label>
        <div class="filter-actions">
          <button class="primary-button" type="submit">${escapeHtml(
            editingShortcut ? lc('Save shortcut', '保存快捷入口') : lc('Create shortcut', '创建快捷入口'),
          )}</button>
          ${
            editingShortcut
              ? `<button class="secondary-button" type="button" data-workbench-action="shortcut-cancel">${escapeHtml(lc('Cancel edit', '取消编辑'))}</button>`
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
      title: lc('5. Recent Context', '5. 最近上下文'),
      copy: lc(
        'Recent helps you reopen active detail work after you already know the next mode or page. It is contextual support, not a history log or audit timeline.',
        '最近上下文用于在你已经知道下一步模式或页面后重新打开正在进行的详情工作。它是上下文支持，不是历史日志或审计时间线。',
      ),
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
          : renderEmptyState(lc('No recent work context yet.', '还没有最近工作上下文。'))
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
      <a class="record-action" href="${escapeHtml(recent.route_snapshot)}" data-nav="true">${escapeHtml(localizeUiLabel('Resume'))}</a>
    </article>
  `
}

function renderWorkbenchDashboardSummarySection(dashboard: DashboardData | null, errorMessage?: string | null): string {
  return renderSectionShell(
    {
      title: lc('4. Summary Support', '4. 摘要支持'),
      copy: lc(
        'Summary stays support. Use it after choosing a mode or entry path to confirm where pressure exists or which detail area is worth reopening next.',
        '摘要保持为支持层。选择模式或入口路径后，再用它确认当前压力点，或判断下一个值得重新打开的详情区域。',
      ),
      className: 'workbench-section section-shell--support',
    },
    `
      ${
        errorMessage
          ? renderUnavailableState(
              lc('Dashboard summary is unavailable for the workbench right now.', '工作台的总览摘要当前不可用。'),
              errorMessage,
            )
          : dashboard
            ? isDashboardEmpty(dashboard)
              ? renderPageStatePanel({
                  tone: 'empty',
                  eyebrow: sc('Empty'),
                  title: lc('Dashboard summary is currently empty.', '总览摘要当前为空。'),
                  message: sc('Empty means the shared API responded successfully, but this page does not have records or summary data yet.'),
                })
              : `
                  <div class="summary-grid">
                    <article class="summary-card">
                      <div class="summary-card__header">
                        <h3>${escapeHtml(lc('Pending review', '待审复核'))}</h3>
                        <a class="record-action" href="/pending" data-nav="true">${escapeHtml(localizeUiLabel('Open'))}</a>
                      </div>
                      <p class="summary-value">${dashboard.pending_summary.open_count}</p>
                      <p class="section-copy">${escapeHtml(
                        lc(
                          `${dashboard.pending_summary.resolved_in_last_7_days} resolved in the last 7 days.`,
                          `近 7 天已解决 ${dashboard.pending_summary.resolved_in_last_7_days} 条。`,
                        ),
                      )}</p>
                    </article>
                    <article class="summary-card summary-card--alert">
                      <div class="summary-card__header">
                        <h3>${escapeHtml(lc('Open alerts', '打开提醒'))}</h3>
                        <a class="record-action" href="/dashboard" data-nav="true">${escapeHtml(sc('Dashboard'))}</a>
                      </div>
                      <p class="summary-value">${dashboard.alert_summary.open_count}</p>
                      <p class="section-copy">${escapeHtml(
                        lc('Current high-signal alert count from the shared dashboard summary.', '来自共享总览摘要的当前高信号提醒数量。'),
                      )}</p>
                    </article>
                    <article class="summary-card">
                      <div class="summary-card__header">
                        <h3>${escapeHtml(lc('Formal record pulse', '正式记录脉冲'))}</h3>
                        <a class="record-action" href="/dashboard" data-nav="true">${escapeHtml(sc('Dashboard'))}</a>
                      </div>
                      <p class="section-copy">${escapeHtml(
                        lc(
                          'Formal records stay primary on their own pages. This support block only helps you pick the next domain to open.',
                          '正式记录仍以各自页面为主。本支持块仅帮助你决定下一个要打开的域。',
                        ),
                      )}</p>
                      <div class="field-grid">
                        ${renderField('Expense This Month', String(dashboard.expense_summary.created_in_current_month))}
                        ${renderField('Knowledge Last 7 Days', String(dashboard.knowledge_summary.created_in_last_7_days))}
                        ${renderField('Health Last 7 Days', String(dashboard.health_summary.created_in_last_7_days))}
                      </div>
                      <div class="filter-actions">
                        <a class="record-action" href="/expense" data-nav="true">${escapeHtml(formatDomainLabel('expense'))}</a>
                        <a class="record-action" href="/knowledge" data-nav="true">${escapeHtml(formatDomainLabel('knowledge'))}</a>
                        <a class="record-action" href="/health" data-nav="true">${escapeHtml(formatDomainLabel('health'))}</a>
                      </div>
                    </article>
                  </div>
                `
            : renderPageStatePanel({
                tone: 'empty',
                eyebrow: sc('Empty'),
                title: lc('Dashboard summary is currently empty.', '总览摘要当前为空。'),
                message: sc('Empty means the shared API responded successfully, but this page does not have records or summary data yet.'),
              })
      }
    `,
  )
}

function describeWorkbenchModeTarget(moduleValue: string | null, viewKey: string | null): string {
  if (!moduleValue) {
    return sc('Not set')
  }

  const moduleLabel = formatDomainLabel(moduleValue)
  if (!viewKey) {
    return moduleLabel
  }

  return `${moduleLabel} / ${viewKey}`
}

function getTemplateDisplayName(template: WorkbenchTemplate): string {
  if (template.template_type !== 'builtin') {
    return template.name
  }

  switch (template.default_module) {
    case 'dashboard':
      return sc('Home')
    case 'pending':
      return sc('Pending Review')
    case 'expense':
      return sc('Expenses')
    default:
      return template.name
  }
}

function getTemplateDisplayDescription(template: WorkbenchTemplate): string {
  if (template.template_type !== 'builtin') {
    return template.description ?? ''
  }

  switch (template.default_module) {
    case 'dashboard':
      return sc('Default workbench landing mode.')
    case 'pending':
      return sc('Focus on open pending items.')
    case 'expense':
      return sc('Continue expense work from the main workbench.')
    default:
      return template.description ?? ''
  }
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
        workbenchUiState.flash = { kind: 'success', message: sc('Current mode updated.') }
        await renderApp()
        return
      }
      case 'template-apply-default': {
        const templateId = Number.parseInt(button.dataset.templateId || '', 10)
        await applyWorkbenchTemplate(templateId, { set_as_default: true })
        workbenchUiState.flash = { kind: 'success', message: sc('Work mode updated and set as the default entry.') }
        await renderApp()
        return
      }
      case 'template-toggle': {
        const templateId = Number.parseInt(button.dataset.templateId || '', 10)
        const enabled = button.dataset.enabled === 'true'
        await updateWorkbenchTemplate(templateId, { is_enabled: !enabled })
        workbenchUiState.flash = { kind: 'success', message: enabled ? sc('Work mode disabled.') : sc('Work mode enabled.') }
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
        workbenchUiState.flash = { kind: 'success', message: enabled ? sc('Shortcut disabled.') : sc('Shortcut enabled.') }
        await renderApp()
        return
      }
      case 'shortcut-delete': {
        const shortcutId = Number.parseInt(button.dataset.shortcutId || '', 10)
        if (!window.confirm(sc('Delete this shortcut?'))) {
          return
        }
        await deleteWorkbenchShortcut(shortcutId)
        if (workbenchUiState.editingShortcutId === shortcutId) {
          workbenchUiState.editingShortcutId = null
        }
        workbenchUiState.flash = { kind: 'success', message: sc('Shortcut deleted.') }
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
        workbenchUiState.flash = { kind: 'success', message: sc('Work mode saved.') }
      } else {
        await createWorkbenchTemplate(payload)
        workbenchUiState.flash = { kind: 'success', message: sc('Work mode created.') }
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
        workbenchUiState.flash = { kind: 'success', message: sc('Shortcut saved.') }
      } else {
        await createWorkbenchShortcut(payload)
        workbenchUiState.flash = { kind: 'success', message: sc('Shortcut created.') }
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

async function renderCaptureListPage(): Promise<string> {
  const query = parseCaptureListQuery(new URLSearchParams(window.location.search))

  try {
    const response = await fetchCaptureList(buildCaptureApiParams(query))
    return renderCaptureListView(query, response)
  } catch (error) {
    return renderCaptureListView(query, null, toErrorMessage(error))
  }
}

async function renderCaptureDetailPage(id: string): Promise<string> {
  try {
    const detail = await fetchCaptureDetail(id)
    return renderCaptureDetailView(detail)
  } catch (error) {
    return renderDetailErrorView(sc('Capture Record'), '/capture', sc('Capture'), toErrorMessage(error))
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
    return renderDetailErrorView(sc('Pending Item'), '/pending', sc('Pending'), toErrorMessage(error))
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
    return renderDetailErrorView(sc('Expense Record'), '/expense', sc('Expenses'), toErrorMessage(error))
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
    return renderDetailErrorView(sc('Knowledge Record'), '/knowledge', sc('Knowledge'), toErrorMessage(detailResult.reason))
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
    return renderDetailErrorView(sc('Health Record'), '/health', sc('Health'), toErrorMessage(detailResult.reason))
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

  const localeButton = target.closest<HTMLButtonElement>('[data-locale-choice]')
  if (localeButton) {
    const nextLocale = localeButton.dataset.localeChoice
    if (nextLocale === 'en' || nextLocale === 'zh') {
      event.preventDefault()
      setUiLocale(nextLocale)
      void renderApp()
    }
    return
  }

  const pendingActionButton = target.closest<HTMLButtonElement>('[data-pending-action]')
  if (pendingActionButton) {
    const action = pendingActionButton.dataset.pendingAction
    const pendingId = Number.parseInt(pendingActionButton.dataset.pendingId || '', 10)
    if ((action === 'confirm' || action === 'discard' || action === 'force_insert') && Number.isFinite(pendingId)) {
      event.preventDefault()
      void handlePendingReviewAction(pendingActionButton, pendingId, action)
    }
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

  if (form.dataset.captureForm === 'submit') {
    event.preventDefault()
    void handleCaptureSubmit(form)
    return
  }

  if (form.dataset.captureForm === 'quick-submit') {
    event.preventDefault()
    void handleQuickCaptureSubmit(form)
    return
  }

  if (form.dataset.bulkIntakeForm === 'preview') {
    event.preventDefault()
    void handleBulkIntakePreviewSubmit(form)
    return
  }

  if (form.dataset.bulkIntakeForm === 'import') {
    event.preventDefault()
    void handleBulkIntakeImportSubmit(form)
    return
  }

  if (form.dataset.pendingForm === 'fix') {
    event.preventDefault()
    void handlePendingFixSubmit(form)
    return
  }

  if (form.dataset.workbenchForm === 'true') {
    event.preventDefault()
    void handleWorkbenchSubmit(form)
    return
  }

  if (form.dataset.systemForm) {
    event.preventDefault()
    void handleSystemFormSubmit(form)
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

async function handleQuickCaptureSubmit(form: HTMLFormElement): Promise<void> {
  const textarea = form.querySelector<HTMLTextAreaElement>('textarea[name="raw_text"]')
  const submitButton = form.querySelector<HTMLButtonElement>('button[type="submit"]')

  const rawText = textarea?.value.trim() || ''

  quickCaptureUiState.draftText = rawText
  quickCaptureUiState.feedback = null

  if (!rawText) {
    quickCaptureUiState.feedback = {
      kind: 'error',
      title: lc('Quick capture could not be submitted.', '无法提交快速采集。'),
      message: lc('Enter text first so the system can create a capture record.', '请先输入文本，系统才能创建采集记录。'),
    }
    await renderApp()
    return
  }

  if (submitButton) {
    submitButton.disabled = true
    submitButton.textContent = lc('Saving quick capture...', '正在保存快速采集...')
  }
  if (textarea) {
    textarea.disabled = true
  }

  try {
    const result = await submitCapture({
      raw_text: rawText,
      source_type: 'manual',
      source_ref: null,
    })
    const baseFeedback = buildCaptureSubmissionSuccessFeedback(result)
    quickCaptureUiState.draftText = ''
    quickCaptureUiState.feedback = {
      kind: 'success',
      title: lc('Quick capture saved.', '快速采集已保存。'),
      message: `${baseFeedback.message} ${lc('You can enter the next text now.', '现在可以继续输入下一条。')}`,
      captureId: result.capture_id,
    }
  } catch (error) {
    quickCaptureUiState.feedback = {
      kind: 'error',
      title: lc('Quick capture could not be submitted.', '无法提交快速采集。'),
      message: toErrorMessage(error),
    }
  }

  await renderApp()
}

async function handleBulkIntakePreviewSubmit(form: HTMLFormElement): Promise<void> {
  const fileInput = form.querySelector<HTMLInputElement>('input[name="bulk_file"]')
  const submitButton = form.querySelector<HTMLButtonElement>('button[type="submit"]')
  const file = fileInput?.files?.[0] ?? null

  bulkIntakeUiState.feedback = null

  if (!file) {
    bulkIntakeUiState.feedback = {
      kind: 'error',
      title: lc('Preview could not be generated.', '无法生成预览。'),
      message: lc('Select a .txt or .md file first.', '请先选择 .txt 或 .md 文件。'),
    }
    await renderApp()
    return
  }

  if (!isSupportedBulkTextFile(file.name)) {
    bulkIntakeUiState.feedback = {
      kind: 'error',
      title: lc('Preview could not be generated.', '无法生成预览。'),
      message: lc('Only .txt and .md files are supported in this first version.', '这一版仅支持 .txt 和 .md 文件。'),
    }
    await renderApp()
    return
  }

  if (submitButton) {
    submitButton.disabled = true
    submitButton.textContent = lc('Generating preview...', '正在生成预览...')
  }
  if (fileInput) {
    fileInput.disabled = true
  }

  try {
    const textContent = await file.text()
    const preview = await previewBulkCapture({
      file_name: file.name,
      text_content: textContent,
    })
    bulkIntakeUiState.selectedFileName = file.name
    bulkIntakeUiState.fileText = textContent
    bulkIntakeUiState.preview = preview
    bulkIntakeUiState.feedback = null
  } catch (error) {
    bulkIntakeUiState.feedback = {
      kind: 'error',
      title: lc('Preview could not be generated.', '无法生成预览。'),
      message: toErrorMessage(error),
    }
  } finally {
    if (submitButton) {
      submitButton.disabled = false
      submitButton.textContent = lc('Generate preview', '生成预览')
    }
    if (fileInput) {
      fileInput.disabled = false
    }
  }

  await renderApp()
}


async function handleBulkIntakeImportSubmit(form: HTMLFormElement): Promise<void> {
  const submitButton = form.querySelector<HTMLButtonElement>('button[type="submit"]')
  const preview = bulkIntakeUiState.preview

  if (!preview) {
    bulkIntakeUiState.feedback = {
      kind: 'error',
      title: lc('Import could not start.', '无法开始导入。'),
      message: lc('Generate a preview first so the capture candidates are visible before import.', '请先生成预览，以便在导入前查看采集候选项。'),
    }
    await renderApp()
    return
  }

  const validEntries = preview.candidates.filter((candidate) => candidate.is_valid).map((candidate) => candidate.raw_text)
  if (validEntries.length === 0) {
    bulkIntakeUiState.feedback = {
      kind: 'error',
      title: lc('Import could not start.', '无法开始导入。'),
      message: lc('No valid candidate entries are available to import into Capture.', '当前没有可导入到采集层的有效候选项。'),
    }
    await renderApp()
    return
  }

  if (submitButton) {
    submitButton.disabled = true
    submitButton.textContent = lc('Importing into Capture...', '正在导入到采集层...')
  }

  try {
    const result = await importBulkCapture({
      file_name: preview.file_name,
      entries: validEntries,
    })
    bulkIntakeUiState.feedback = buildBulkIntakeSuccessFeedback(preview, result)
    bulkIntakeUiState.preview = null
    bulkIntakeUiState.selectedFileName = ''
    bulkIntakeUiState.fileText = ''
  } catch (error) {
    bulkIntakeUiState.feedback = {
      kind: 'error',
      title: lc('Import could not complete.', '导入无法完成。'),
      message: toErrorMessage(error),
    }
  } finally {
    if (submitButton) {
      submitButton.disabled = false
      submitButton.textContent = lc('Import valid entries', '导入有效候选项')
    }
  }

  await renderApp()
}

async function handleCaptureSubmit(form: HTMLFormElement): Promise<void> {
  const textarea = form.querySelector<HTMLTextAreaElement>('textarea[name="raw_text"]')
  const sourceRefInput = form.querySelector<HTMLInputElement>('input[name="source_ref"]')
  const submitButton = form.querySelector<HTMLButtonElement>('button[type="submit"]')

  const rawText = textarea?.value.trim() || ''
  const sourceRef = sourceRefInput?.value.trim() || ''

  captureUiState.submissionDraft = {
    rawText,
    sourceRef,
  }
  captureUiState.feedback = null

  if (!rawText) {
    captureUiState.feedback = {
      kind: 'error',
      title: lc('Capture could not be submitted.', '无法提交采集。'),
      message: lc('Plain text input is required before a capture record can be created.', '创建采集记录前必须提供纯文本输入。'),
    }
    await renderApp()
    return
  }

  if (submitButton) {
    submitButton.disabled = true
    submitButton.textContent = lc('Submitting capture...', '正在提交采集...')
  }
  if (textarea) {
    textarea.disabled = true
  }
  if (sourceRefInput) {
    sourceRefInput.disabled = true
  }

  try {
    const result = await submitCapture({
      raw_text: rawText,
      source_type: 'manual',
      source_ref: sourceRef || null,
    })
    captureUiState.submissionDraft = {
      rawText: '',
      sourceRef: '',
    }
    captureUiState.feedback = buildCaptureSubmissionSuccessFeedback(result)
    navigate(`/capture/${result.capture_id}`)
  } catch (error) {
    captureUiState.feedback = {
      kind: 'error',
      title: lc('Capture could not be submitted.', '无法提交采集。'),
      message: toErrorMessage(error),
    }
    await renderApp()
  } finally {
    if (submitButton) {
      submitButton.disabled = false
      submitButton.textContent = localizeUiLabel('Create Capture Record')
    }
    if (textarea) {
      textarea.disabled = false
    }
    if (sourceRefInput) {
      sourceRefInput.disabled = false
    }
  }
}

async function handleSystemFormSubmit(form: HTMLFormElement): Promise<void> {
  const kind = form.dataset.systemForm
  if (!kind) {
    return
  }

  const formData = new FormData(form)
  localOperabilityUiState.feedback = null
  localOperabilityUiState.backupDestinationPath = optionalFormValue(formData, 'destination_path') || ''
  localOperabilityUiState.restoreSourcePath = optionalFormValue(formData, 'source_path') || ''
  localOperabilityUiState.restoreCreateSafetyBackup = formData.get('create_safety_backup') === 'on'
  localOperabilityUiState.restoreConfirmed = formData.get('confirm_restore') === 'on'
  localOperabilityUiState.exportDestinationPath = optionalFormValue(formData, 'export_destination_path') || ''
  localOperabilityUiState.importSourcePath = optionalFormValue(formData, 'import_source_path') || ''

  if (kind === 'restore' && !localOperabilityUiState.restoreConfirmed) {
    localOperabilityUiState.feedback = {
      kind: 'error',
      title: sc('Restore could not start.'),
      message: sc('Restore must be explicitly confirmed because it replaces the active local SQLite database file.'),
    }
    await renderApp()
    return
  }

  const submitButton = form.querySelector<HTMLButtonElement>('button[type="submit"]')
  const originalLabel = submitButton?.textContent || ''
  if (submitButton) {
    submitButton.textContent = describeSystemFormRunningLabel(kind)
  }
  setFormControlsDisabled(form, true)

  try {
    if (kind === 'backup') {
      const result = await createLocalBackup(localOperabilityUiState.backupDestinationPath || undefined)
      localOperabilityUiState.feedback = {
        kind: 'success',
        title: sc('Local backup created.'),
        message: sc('SQLite backup was written to {path}.', { path: result.backup_path }),
        details: [
          sc('Active database: {path}', { path: result.database_path }),
          sc('Backup size: {size}', { size: formatByteSize(result.file_size_bytes) }),
          sc('Created at: {value}', { value: formatDateTime(result.created_at) }),
        ],
      }
      localOperabilityUiState.backupDestinationPath = ''
    } else if (kind === 'restore') {
      const result = await restoreLocalBackup(
        localOperabilityUiState.restoreSourcePath,
        localOperabilityUiState.restoreCreateSafetyBackup,
      )
      localOperabilityUiState.feedback = {
        kind: 'success',
        title: sc('Local database restored.'),
        message: sc('SQLite database was restored from {path}.', { path: result.source_path }),
        details: [
          sc('Active database: {path}', { path: result.database_path }),
          result.safety_backup_path ? sc('Safety backup: {path}', { path: result.safety_backup_path }) : sc('Safety backup was skipped.'),
          sc('Restored at: {value}', { value: formatDateTime(result.restored_at) }),
        ],
      }
      localOperabilityUiState.restoreConfirmed = false
    } else if (kind === 'export') {
      const result = await exportCaptureBundle(localOperabilityUiState.exportDestinationPath || undefined)
      localOperabilityUiState.feedback = {
        kind: 'success',
        title: sc('Capture bundle exported.'),
        message: sc('Bounded capture transfer file was written to {path}.', { path: result.export_path }),
        details: [
          sc('Included capture items: {count}', { count: String(result.item_count) }),
          sc('Skipped items without raw text: {count}', { count: String(result.skipped_count) }),
          sc('Created at: {value}', { value: formatDateTime(result.created_at) }),
        ],
      }
      localOperabilityUiState.exportDestinationPath = ''
    } else if (kind === 'import') {
      const result = await importCaptureBundle(localOperabilityUiState.importSourcePath)
      localOperabilityUiState.feedback = {
        kind: 'success',
        title: sc('Capture bundle imported.'),
        message: sc('Capture bundle import created {count} new capture records through the existing intake path.', {
          count: result.imported_count,
        }),
        details: [
          sc('Pending review routes: {count}', { count: String(result.pending_count) }),
          sc('Direct committed routes: {count}', { count: String(result.committed_count) }),
          sc('Imported at: {value}', { value: formatDateTime(result.imported_at) }),
        ],
      }
    }
    await renderApp()
  } catch (error) {
    localOperabilityUiState.feedback = {
      kind: 'error',
      title: sc('Local continuity action failed.'),
      message: toErrorMessage(error),
    }
    await renderApp()
  } finally {
    setFormControlsDisabled(form, false)
    if (submitButton) {
      submitButton.textContent = originalLabel
    }
  }
}

function setFormControlsDisabled(form: HTMLFormElement, disabled: boolean): void {
  form.querySelectorAll<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement | HTMLButtonElement>(
    'input, textarea, select, button',
  ).forEach((element) => {
    element.disabled = disabled
  })
}

function describeSystemFormRunningLabel(kind: string): string {
  switch (kind) {
    case 'backup':
      return sc('Creating backup...')
    case 'restore':
      return sc('Restoring database...')
    case 'export':
      return sc('Exporting capture bundle...')
    case 'import':
      return sc('Importing capture bundle...')
    default:
      return sc('Working...')
  }
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
          <span class="brand-caption">${escapeHtml(sc('Local-first workspace'))}</span>
        </div>
        <nav class="workspace-nav" aria-label="${escapeHtml(sc('Primary'))}">
          ${renderNavLink(sc('Workbench'), '/workbench', activeSection === 'workbench')}
          ${renderNavLink(sc('Dashboard'), '/dashboard', activeSection === 'dashboard')}
          ${renderNavLink(sc('Capture'), '/capture', activeSection === 'capture')}
          ${renderNavLink(sc('Pending'), '/pending', activeSection === 'pending')}
          ${renderNavLink(sc('Expenses'), '/expense', activeSection === 'expense')}
          ${renderNavLink(sc('Knowledge'), '/knowledge', activeSection === 'knowledge')}
          ${renderNavLink(sc('Health'), '/health', activeSection === 'health')}
        </nav>
        ${renderLocaleToggle()}
      </header>
      <main class="workspace-main">
        ${content}
      </main>
    </div>
  `
}

function applyRoutePostRenderEffects(route: Exclude<Route, { kind: 'redirect' }>): void {
  if (route.kind === 'bulk-intake') {
    const fileInput = app.querySelector<HTMLInputElement>('input[name="bulk_file"]')
    fileInput?.focus()
    return
  }

  if (route.kind !== 'quick-capture') {
    return
  }

  const input = app.querySelector<HTMLTextAreaElement>('[data-quick-capture-input="true"]')
  if (!input) {
    return
  }

  input.focus()
  const end = input.value.length
  try {
    input.setSelectionRange(end, end)
  } catch {
    // Ignore selection support issues and keep focus only.
  }
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
          ? `<a class="back-link" href="${escapeHtml(options.backHref)}" data-nav="true">${escapeHtml(sc('Back to {label}', { label: options.backLabel }))}</a>`
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

async function handleAiDerivationAction(
  button: HTMLButtonElement,
  recordId: number,
): Promise<void> {
  const originalLabel = button.textContent || ''
  button.disabled = true
  button.textContent = lc('Requesting recompute...', '正在请求重新计算...')

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
  button.textContent = action === 'acknowledge' ? lc('Acknowledging...', '正在确认...') : lc('Resolving...', '正在解决...')

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

async function handlePendingReviewAction(
  button: HTMLButtonElement,
  pendingId: number,
  action: PendingReviewActionType,
): Promise<void> {
  const originalLabel = button.textContent || ''
  button.disabled = true
  button.textContent = describePendingActionRunningLabel(action)
  pendingUiState.feedbackById[pendingId] = undefined

  try {
    const result =
      action === 'confirm'
        ? await confirmPending(pendingId)
        : action === 'discard'
          ? await discardPending(pendingId)
          : await forceInsertPending(pendingId)

    pendingUiState.feedbackById[pendingId] = buildPendingActionSuccessFeedback(result)
    await renderApp()
  } catch (error) {
    pendingUiState.feedbackById[pendingId] = {
      kind: 'error',
      title: sc('{label} failed.', { label: formatPendingActionLabel(action) }),
      message: toErrorMessage(error),
    }
    await renderApp()
  } finally {
    button.disabled = false
    button.textContent = originalLabel
  }
}

async function handlePendingFixSubmit(form: HTMLFormElement): Promise<void> {
  const pendingId = Number.parseInt(form.dataset.pendingId || '', 10)
  if (!Number.isFinite(pendingId)) {
    return
  }

  const textarea = form.querySelector<HTMLTextAreaElement>('textarea[name="correction_text"]')
  const submitButton = form.querySelector<HTMLButtonElement>('button[type="submit"]')
  const correctionText = textarea?.value.trim() || ''

  pendingUiState.fixDraftById[pendingId] = correctionText

  if (!correctionText) {
    pendingUiState.feedbackById[pendingId] = {
      kind: 'error',
      title: sc('Fix could not be applied.'),
      message: sc('Correction text is required before the corrected payload can be updated.'),
    }
    await renderApp()
    return
  }

  if (submitButton) {
    submitButton.disabled = true
    submitButton.textContent = sc('Updating corrected payload...')
  }
  if (textarea) {
    textarea.disabled = true
  }
  pendingUiState.feedbackById[pendingId] = undefined

  try {
    await fixPending(pendingId, correctionText)
    delete pendingUiState.fixDraftById[pendingId]
    pendingUiState.feedbackById[pendingId] = {
      kind: 'success',
      title: sc('Corrected payload updated.'),
      message: sc('Fix updates corrected payload only. The pending item remains reviewable until you confirm, discard, or force insert it.'),
    }
    await renderApp()
  } catch (error) {
    pendingUiState.feedbackById[pendingId] = {
      kind: 'error',
      title: sc('Fix could not be applied.'),
      message: toErrorMessage(error),
    }
    await renderApp()
  } finally {
    if (submitButton) {
      submitButton.disabled = false
      submitButton.textContent = sc('Apply Fix')
    }
    if (textarea) {
      textarea.disabled = false
    }
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
      title: sc('Dashboard'),
      copy: lc('Summary layer for pending review, formal records, and recent context.', '待审复核、正式记录与最近上下文的摘要层。'),
    })}
    ${renderRuntimeStatusSection(runtimeStatus ?? null, runtimeStatusError ?? null)}
    ${
      !errorMessage && dashboard && isDashboardEmpty(dashboard)
        ? renderPageStatePanel({
            tone: 'empty',
            eyebrow: sc('Empty'),
            title: lc('Dashboard is currently empty.', '总览当前为空。'),
            message: sc('Empty means the shared API responded successfully, but this page does not have records or summary data yet.'),
          })
        : ''
    }
    ${
      errorMessage
        ? renderUnavailableState(lc('Dashboard is unavailable.', '总览不可用。'), errorMessage)
        : dashboard
          ? `
              ${renderPendingSummarySection(dashboard)}
              ${renderFormalSummariesSection(dashboard)}
              ${renderAlertSummarySection(dashboard)}
              ${renderRecentActivitySection(dashboard)}
              ${renderQuickLinksSection(dashboard)}
            `
          : renderEmptyState(lc('Dashboard data is not available.', '总览数据暂不可用。'))
    }
  `)
}

function renderAlertSummarySection(dashboard: DashboardData): string {
  return `
    <section class="panel page-section">
      <div class="section-header">
        <h2>${escapeHtml(lc('Rule Alerts', '规则提醒'))}</h2>
      </div>
      <div class="summary-grid">
        <article class="summary-card summary-card--alert">
          <div class="summary-card__header">
            <h3>${escapeHtml(lc('Open health alerts', '打开健康提醒'))}</h3>
            <a class="record-action" href="${escapeHtml(dashboard.alert_summary.href)}" data-nav="true">${escapeHtml(localizeUiLabel('View alerts'))}</a>
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
                                <a class="record-action" href="${escapeHtml(item.href)}" data-nav="true">${escapeHtml(localizeUiLabel('Open record'))}</a>
                              </div>
                            </article>
                          `,
                        )
                        .join('')}
                    </div>
                  `
                : `<p class="section-copy">${escapeHtml(lc('No open rule alerts.', '没有打开的规则提醒。'))}</p>`
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
        <h2>${escapeHtml(lc('Pending Summary', '待审摘要'))}</h2>
      </div>
      <div class="summary-grid">
        <article class="summary-card">
          <div class="summary-card__header">
            <h3>${escapeHtml(lc('Open pending items', '打开待审项'))}</h3>
            <a class="record-action" href="${escapeHtml(dashboard.pending_summary.href)}" data-nav="true">${escapeHtml(localizeUiLabel('View pending'))}</a>
          </div>
          <div class="summary-stack">
            <p class="summary-value">${dashboard.pending_summary.open_count}</p>
            <div class="field-grid">
              ${renderField('Opened in last 7 days', String(dashboard.pending_summary.opened_in_last_7_days))}
              ${renderField('Resolved in last 7 days', String(dashboard.pending_summary.resolved_in_last_7_days))}
            </div>
            <div>
              <span class="field__label">${escapeHtml(lc('Open by target domain', '按目标域打开数'))}</span>
              ${renderDataList(
                Object.entries(dashboard.pending_summary.open_count_by_target_domain).map(([domain, count]) => ({
                  label: formatDomainLabel(domain),
                  value: String(count),
                })),
                lc('No open pending items by domain.', '各目标域下都没有打开的待审项。'),
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
        <h2>${escapeHtml(lc('Quick Links', '快捷入口'))}</h2>
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
                        <span class="quick-link-card__meta">${escapeHtml(localizeUiLabel('Open context'))}</span>
                      </a>
                    `,
                  )
                  .join('')}
              </div>
            `
          : renderEmptyState(lc('No quick links available.', '没有可用的快捷入口。'))
      }
    </section>
  `
}

function renderFormalSummariesSection(dashboard: DashboardData): string {
  return `
    <section class="panel page-section">
      <div class="section-header">
        <h2>${escapeHtml(lc('Expense / Knowledge / Health Summaries', '支出 / 知识 / 健康摘要'))}</h2>
      </div>
      <div class="summary-grid">
        <article class="summary-card">
          <div class="summary-card__header">
            <h3>${escapeHtml(formatDomainLabel('expense'))}</h3>
            <a class="record-action" href="${escapeHtml(dashboard.expense_summary.href)}" data-nav="true">${escapeHtml(localizeUiLabel('View expenses'))}</a>
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
              <span class="field__label">${escapeHtml(lc('Amount by currency', '按币种金额'))}</span>
              ${renderDataList(
                Object.entries(dashboard.expense_summary.amount_by_currency_current_month).map(([currency, amount]) => ({
                  label: currency,
                  value: amount,
                })),
                lc('No expense amounts for the current month.', '当前月份没有支出金额。'),
              )}
            </div>
          </div>
        </article>
        <article class="summary-card">
          <div class="summary-card__header">
            <h3>${escapeHtml(formatDomainLabel('knowledge'))}</h3>
            <a class="record-action" href="${escapeHtml(dashboard.knowledge_summary.href)}" data-nav="true">${escapeHtml(localizeUiLabel('View knowledge'))}</a>
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
            <h3>${escapeHtml(formatDomainLabel('health'))}</h3>
            <a class="record-action" href="${escapeHtml(dashboard.health_summary.href)}" data-nav="true">${escapeHtml(localizeUiLabel('View health'))}</a>
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
              <span class="field__label">${escapeHtml(lc('Recent metric types', '最近指标类型'))}</span>
              ${renderInlineList(
                dashboard.health_summary.recent_metric_types.map((metricType) => formatDomainLabel(metricType)),
                lc('No recent metric types.', '没有最近指标类型。'),
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
        <h2>${escapeHtml(lc('Recent Activity', '最近活动'))}</h2>
      </div>
      ${
        dashboard.recent_activity.length > 0
          ? renderRecentActivityRecords(dashboard.recent_activity)
          : renderEmptyState(lc('No recent activity available.', '没有最近活动。'))
      }
    </section>
  `
}

function renderCaptureListView(
  query: CaptureListQuery,
  response: CaptureListResponse | null,
  errorMessage?: string,
): string {
  const items = response?.items ?? []
  const followUpCount = items.filter((item) => captureNeedsFollowUp(item)).length
  const pendingLinkedCount = items.filter((item) => captureHasPendingLink(item)).length
  const formalizedCount = items.filter((item) => captureHasFormalResult(item)).length
  const suggestedNext = pickSuggestedCaptureInboxItem(items)
  const statusNote =
    response && response.total > 0
      ? lc(
          `Current filtered inbox contains ${response.total} capture records. Prioritize items that still need downstream follow-up or already point into Pending or formal records.`,
          `当前筛选收件箱包含 ${response.total} 条采集记录。优先查看仍需下游跟进，或已经指向待审与正式记录的项目。`,
        )
      : lc(
          'Capture inbox makes upstream input records visible without turning the web app into a new intake platform.',
          '采集收件箱让上游输入记录保持可见，而不会把 Web 应用变成新的录入平台。',
        )

  return renderPageShell(`
    ${renderPageHeaderBlock({
      title: sc('Capture'),
      copy: lc(
        'Capture is the visible upstream intake inbox. Use it to inspect what entered the system, what still needs follow-up, and where it flowed next.',
        '采集是可见的上游收件箱。用它查看进入系统的内容、哪些还需要跟进，以及之后流向何处。',
      ),
    })}
    ${renderSectionShell(
      {
        title: lc('Inbox and Triage Status', '收件箱与分流状态'),
        copy: lc(
          'Capture stays upstream. Use these cues to understand new intake, downstream linkage, and the next sensible item to inspect.',
          '采集保持在上游。用这些提示理解新的录入、下游链接，以及下一条适合查看的项目。',
        ),
        className: 'section-shell--contextual',
      },
      `
        <div class="field-grid">
          ${renderField('Filtered Total', String(response?.total ?? 0))}
          ${renderField(lc('Needs Follow-up', '需要跟进'), String(followUpCount))}
          ${renderField(lc('Pending Linked', '已关联待审'), String(pendingLinkedCount))}
          ${renderField(lc('Formalized', '已进入正式记录'), String(formalizedCount))}
          ${renderField(
            lc('Suggested Next', '建议下一项'),
            suggestedNext
              ? `${suggestedNext.summary || `Capture #${suggestedNext.id}`} · ${describeCaptureInboxNextStep(suggestedNext)}`
              : lc('No triage suggestion on the current page.', '当前页没有建议的分流项。'),
            true,
          )}
          ${renderField('Status Note', statusNote, true)}
        </div>
      `,
    )}
    ${renderCaptureSubmissionSection()}
    ${renderCaptureFilters(query)}
    ${renderSectionShell(
      {
        title: lc('Capture Inbox', '采集收件箱'),
        copy: lc(
          'Use the inbox to scan new intake, downstream linkage, and the next sensible destination before opening capture detail.',
          '在打开采集详情前，可先用收件箱查看新录入、下游链接以及更适合前往的下一站。',
        ),
        className: 'section-shell--primary',
      },
      errorMessage
        ? renderUnavailableState(lc('Capture list is unavailable.', '采集列表不可用。'), errorMessage)
        : response && response.items.length > 0
          ? renderCaptureRecords(response.items)
          : renderEmptyState(lc('No capture records found for the current filters.', '当前筛选条件下没有采集记录。')),
    )}
    ${renderPagination('/capture', response?.page ?? query.page, response?.page_size ?? query.pageSize, response?.total ?? 0)}
  `)
}

function renderQuickCaptureView(): string {
  const feedback = quickCaptureUiState.feedback ? renderCaptureSubmissionFeedback(quickCaptureUiState.feedback) : ''

  return renderPageShell(`
    ${renderPageHeaderBlock({
      title: sc('Quick Capture'),
      copy: lc(
        'Quick Capture keeps text-first intake light. It creates a capture record first so parsing, pending review, or formal commit can happen later through the existing chain.',
        '快速采集让以文本为先的录入保持轻量。它会先创建采集记录，后续再通过现有链路进入解析、待审复核或正式提交。',
      ),
    })}
    ${feedback}
    ${renderSectionShell(
      {
        title: lc('Quick Text Entry', '快速文本录入'),
        copy: lc(
          'Use one text box and one submit action here. After success, the page stays in place and resets for the next entry.',
          '这里使用一个文本框和一个提交动作。成功后页面保持不跳转，并会重置以便继续录入下一条。',
        ),
        className: 'section-shell--primary',
      },
      `
        <form class="filter-form" data-capture-form="quick-submit">
          <label class="filter-field">
            <span>${escapeHtml(lc('Quick Text', '快速文本'))}</span>
            <textarea class="workbench-textarea" name="raw_text" data-quick-capture-input="true" placeholder="${escapeHtml(
              lc('Type the text you want to capture, then submit and keep going.', '输入你想采集的文本，然后提交并继续录入。'),
            )}">${escapeHtml(quickCaptureUiState.draftText)}</textarea>
          </label>
          <div class="filter-actions">
            <button class="primary-button" type="submit">${escapeHtml(lc('Save quick capture', '保存快速采集'))}</button>
            <a class="record-action" href="/capture" data-nav="true">${escapeHtml(sc('Open capture records'))}</a>
          </div>
        </form>
      `,
    )}
    ${renderSectionShell(
      {
        title: lc('Chain Note', '链路说明'),
        copy: lc(
          'This creates a capture record first. Parsing, pending review, or formal commit may happen later through the existing chain.',
          '这里会先创建采集记录。解析、待审复核或正式提交会稍后通过现有链路发生。',
        ),
        className: 'section-shell--contextual',
      },
      `
        <div class="field-grid">
          ${renderField(lc('Input Type', '输入类型'), lc('Pure text only', '仅纯文本'))}
          ${renderField(lc('Creates', '创建结果'), lc('Capture record', '采集记录'))}
          ${renderField(lc('After Success', '成功后'), lc('Stay here ready for the next entry', '留在这里准备下一条录入'))}
          ${renderField(lc('Formal Write', '正式写入'), lc('Later through parse and review', '稍后经由解析与复核'))}
        </div>
      `,
    )}
  `)
}

function renderBulkIntakeView(): string {
  const feedback = bulkIntakeUiState.feedback ? renderBulkIntakeFeedback(bulkIntakeUiState.feedback) : ''
  const previewSection = bulkIntakeUiState.preview ? renderBulkIntakePreviewSection(bulkIntakeUiState.preview) : ''

  return renderPageShell(`
    ${renderPageHeaderBlock({
      title: sc('Bulk Intake'),
      copy: lc(
        'Bulk Intake imports text files into Capture first. Preview the split candidates, then confirm import into the existing chain.',
        '批量导入会先把文本文件导入到采集层。先预览拆分后的候选项，再确认导入到现有链路中。',
      ),
    })}
    ${feedback}
    ${renderSectionShell(
      {
        title: lc('Text File Intake', '文本文件导入'),
        copy: lc(
          'This first version accepts .txt and .md files only. The preview uses blank lines to split candidate capture entries.',
          '这一版仅接受 .txt 和 .md 文件。预览会按空行拆分采集候选项。',
        ),
        className: 'section-shell--primary',
      },
      `
        <div class="field-grid">
          ${renderField(lc('Import Destination', '导入目标'), lc('Capture layer only', '仅导入采集层'))}
          ${renderField(lc('Preview Mode', '预览模式'), lc('Confirmation-oriented', '以确认为导向'))}
          ${renderField(lc('Split Strategy', '拆分策略'), lc('Blank-line blocks', '按空行分块'))}
          ${renderField(lc('Formal Write', '正式写入'), lc('Later through parse and review', '稍后经由解析与复核'))}
        </div>
        <form class="filter-form" data-bulk-intake-form="preview">
          <label class="filter-field">
            <span>${escapeHtml(lc('Choose text file', '选择文本文件'))}</span>
            <input type="file" name="bulk_file" accept=".txt,.md,text/plain,text/markdown" />
          </label>
          <div class="filter-actions">
            <button class="primary-button" type="submit">${escapeHtml(lc('Generate preview', '生成预览'))}</button>
            <a class="record-action" href="/capture" data-nav="true">${escapeHtml(sc('Open capture records'))}</a>
          </div>
        </form>
      `,
    )}
    ${previewSection}
  `)
}

function renderBulkIntakePreviewSection(preview: BulkCapturePreviewResult): string {
  const validCandidates = preview.candidates.filter((candidate) => candidate.is_valid)
  const invalidCandidates = preview.candidates.filter((candidate) => !candidate.is_valid)

  return renderSectionShell(
    {
      title: lc('Preview Before Import', '导入前预览'),
      copy: lc(
        'Review the candidate count and short previews here. This step confirms what will become capture records before import runs.',
        '在这里查看候选项数量和简短预览。该步骤用于在导入执行前确认哪些内容会变成采集记录。',
      ),
      className: 'section-shell--secondary',
    },
    `
      <div class="field-grid">
        ${renderField(lc('File Name', '文件名'), preview.file_name)}
        ${renderField(lc('Candidate Count', '候选项数量'), String(preview.candidate_count))}
        ${renderField(lc('Valid Candidates', '有效候选项'), String(preview.valid_count))}
        ${renderField(lc('Skipped at Preview', '预览时跳过'), String(preview.invalid_count))}
      </div>
      <div class="workbench-card-grid">
        ${validCandidates
          .map(
            (candidate) => `
              <article class="record-card">
                <div class="record-card__header">
                  <div class="record-card__title-group">
                    <h3>${escapeHtml(lc(`Entry ${candidate.index}`, `候选项 ${candidate.index}`))}</h3>
                    <span class="record-meta">${escapeHtml(lc(`${candidate.char_count} chars`, `${candidate.char_count} 个字符`))}</span>
                  </div>
                  <div class="inline-list">
                    ${renderStatusBadge('open')}
                  </div>
                </div>
                <p class="section-copy">${escapeHtml(candidate.preview ?? '')}</p>
              </article>
            `,
          )
          .join('')}
        ${
          invalidCandidates.length > 0
            ? invalidCandidates
                .map(
                  (candidate) => `
                    <article class="record-card">
                      <div class="record-card__header">
                        <div class="record-card__title-group">
                          <h3>${escapeHtml(lc(`Entry ${candidate.index}`, `候选项 ${candidate.index}`))}</h3>
                          <span class="record-meta">${escapeHtml(lc('Skipped candidate', '已跳过候选项'))}</span>
                        </div>
                        <div class="inline-list">
                          ${renderStatusBadge('discarded')}
                        </div>
                      </div>
                      <p class="section-copy">${escapeHtml(lc('Blank or invalid text block.', '空白或无效的文本块。'))}</p>
                    </article>
                  `,
                )
                .join('')
            : ''
        }
      </div>
      <form class="filter-form" data-bulk-intake-form="import">
        <div class="filter-actions">
          <button class="primary-button" type="submit">${escapeHtml(lc('Import valid entries', '导入有效候选项'))}</button>
          <span class="record-meta">${escapeHtml(
            lc('Import creates capture records first. Parsing and review happen later through the existing chain.', '导入会先创建采集记录。解析和复核会稍后经由现有链路进行。'),
          )}</span>
        </div>
      </form>
    `,
  )
}

function renderBulkIntakeFeedback(feedback: BulkIntakeFeedback): string {
  return renderPageStatePanel({
    tone: feedback.kind === 'success' ? 'ready' : 'unavailable',
    eyebrow: feedback.kind === 'success' ? lc('Bulk Intake Result', '批量导入结果') : lc('Bulk Intake Failed', '批量导入失败'),
    title: feedback.title,
    message: feedback.message,
    details: feedback.details,
    actions:
      feedback.kind === 'success'
        ? `<a class="record-action" href="/capture" data-nav="true">${escapeHtml(sc('Open capture records'))}</a>`
        : undefined,
  })
}

function renderCaptureDetailView(detail: CaptureDetail): string {
  const feedback =
    captureUiState.feedback && captureUiState.feedback.captureId === detail.id
      ? renderCaptureSubmissionFeedback(captureUiState.feedback)
      : ''

  return renderPageShell(`
    ${renderPageHeaderBlock({
      title: sc('Capture Record'),
      backHref: '/capture',
      backLabel: sc('Capture'),
      copy: lc(
        'Capture detail keeps the upstream input readable first, then shows triage context and downstream linkage as support.',
        '采集详情先让上游输入保持可读，再把分流上下文和下游链接作为支持层展示。',
      ),
    })}
    ${feedback}
    ${renderSectionShell(
      {
        title: lc('Current Capture Item', '当前采集项'),
        copy: lc('Keep the intake frame visible before moving into raw content, triage context, or downstream linkage.', '在进入原始内容、分流上下文或下游链接之前，先保持接入框架清晰可见。'),
        className: 'section-shell--primary',
      },
      `
        <div class="record-badges">
          ${renderStatusBadge(detail.status)}
          ${detail.target_domain ? renderBadge(formatDomainLabel(detail.target_domain), true) : renderBadge(lc('Target domain pending', '目标域待定'), true)}
          ${renderBadge(lc(`Stage: ${formatCaptureStageLabel(detail.current_stage)}`, `阶段：${formatCaptureStageLabel(detail.current_stage)}`))}
          ${renderBadge(lc(`Source: ${detail.source_type}`, `来源：${detail.source_type}`), true)}
        </div>
        <div class="field-grid">
          ${renderField('Capture ID', String(detail.id))}
          ${renderField('Current Status', formatStatusLabel(detail.status))}
          ${renderField('Current Stage', formatCaptureStageLabel(detail.current_stage))}
          ${renderField('Target Domain', detail.target_domain ? formatDomainLabel(detail.target_domain) : lc('Not available yet', '暂不可用'))}
          ${renderField('Source Type', detail.source_type)}
          ${renderField('Created At', formatDateTime(detail.created_at))}
          ${renderField('Updated At', formatDateTime(detail.updated_at))}
          ${renderField('Finalized At', detail.finalized_at ? formatDateTime(detail.finalized_at) : null)}
        </div>
        ${renderOptionalTextSubsection(lc('Current Summary', '当前摘要'), detail.summary)}
        ${renderOptionalTextSubsection(lc('Source Reference', '来源说明'), detail.source_ref)}
        ${renderOptionalTextSubsection(lc('Chain Summary', '链路摘要'), detail.chain_summary)}
      `,
    )}
    ${renderCaptureRawContentSection(detail)}
    ${renderCaptureTriageContextSection(detail)}
    ${renderCaptureParseContextSection(detail)}
    ${renderCapturePendingLinkSection(detail)}
    ${renderCaptureFormalResultSection(detail)}
  `)
}

function renderCaptureSubmissionSection(): string {
  const draft = captureUiState.submissionDraft
  const feedback =
    captureUiState.feedback && captureUiState.feedback.captureId === undefined
      ? renderCaptureSubmissionFeedback(captureUiState.feedback)
      : ''

  return renderSectionShell(
    {
      title: lc('Minimal Capture Entry', '最小采集入口'),
      copy: lc(
        'This restrained entry accepts plain text only and reuses the existing backend capture submission semantics.',
        '这个受限入口仅接受纯文本，并复用现有后端采集提交语义。',
      ),
      className: 'section-shell--secondary',
    },
    `
      ${feedback}
      <div class="field-grid">
        ${renderField('Accepted Input', 'Plain text only')}
        ${renderField('Submission Source Type', 'manual')}
        ${renderField('Scope Note', lc('Creating a capture record here does not introduce a new input platform.', '在这里创建采集记录并不会引入新的输入平台。'), true)}
      </div>
      <form class="filter-form" data-capture-form="submit">
        <label class="filter-field">
          <span>${escapeHtml(localizeUiLabel('Captured Text'))}</span>
          <textarea class="workbench-textarea" name="raw_text" placeholder="${escapeHtml(
            lc('Enter the source text that should become a capture record.', '输入应成为采集记录的来源文本。'),
          )}">${escapeHtml(draft.rawText)}</textarea>
        </label>
        <label class="filter-field">
          <span>${escapeHtml(localizeUiLabel('Source Reference'))}</span>
          <input type="text" name="source_ref" value="${escapeHtml(draft.sourceRef)}" placeholder="${escapeHtml(
            lc('Optional note or source reference', '可选备注或来源说明'),
          )}" />
        </label>
        <div class="filter-actions">
          <button class="primary-button" type="submit">${escapeHtml(localizeUiLabel('Create Capture Record'))}</button>
        </div>
      </form>
    `,
  )
}

function renderCaptureSubmissionFeedback(feedback: CaptureSubmissionFeedback): string {
  const actions =
    feedback.captureId !== undefined
      ? `<a class="record-action" href="/capture/${feedback.captureId}" data-nav="true">${escapeHtml(lc('Open capture record', '打开采集记录'))}</a>`
      : undefined

  return renderPageStatePanel({
    tone: feedback.kind === 'success' ? 'ready' : 'unavailable',
    eyebrow: feedback.kind === 'success' ? lc('Capture Submitted', '采集已提交') : lc('Submission Failed', '提交失败'),
    title: feedback.title,
    message: feedback.message,
    actions,
  })
}

function renderCaptureRawContentSection(detail: CaptureDetail): string {
  const rawPayloadMarkup =
    detail.raw_payload_json !== null
      ? `
          <section class="subsection">
            <h3>${escapeHtml(lc('Raw Payload Snapshot', '原始载荷快照'))}</h3>
            ${renderTextBlock(formatJson(detail.raw_payload_json))}
          </section>
        `
      : ''

  return renderSectionShell(
    {
      title: lc('Raw Input and Captured Content', '原始输入与采集内容'),
      copy: lc(
        'The captured material itself stays primary. This first version keeps the content readable without adding editor behavior.',
        '采集材料本身保持为主。这一版只保证内容可读，不加入编辑器行为。',
      ),
      className: 'section-shell--primary',
    },
    detail.raw_text || detail.raw_payload_json !== null
      ? `
          <div class="field-grid">
            ${renderField('Primary Raw Input', detail.raw_text ? lc('Plain text capture', '纯文本采集') : lc('Structured payload snapshot', '结构化载荷快照'))}
            ${renderField('Read Mode', lc('Read-only', '只读'))}
          </div>
          ${detail.raw_text ? renderTextBlock(detail.raw_text) : ''}
          ${rawPayloadMarkup}
        `
      : renderEmptyState(lc('No captured content is stored for this record.', '此记录没有存储采集内容。')),
  )
}

function renderCaptureTriageContextSection(detail: CaptureDetail): string {
  const nextHref = buildCaptureDetailNextHref(detail)
  const nextActionLabel = buildCaptureDetailNextActionLabel(detail)
  const actionLinks = [
    nextHref && nextActionLabel
      ? `<a class="record-action" href="${nextHref}" data-nav="true">${escapeHtml(nextActionLabel)}</a>`
      : '',
    `<a class="record-action" href="/capture" data-nav="true">${escapeHtml(lc('Return to capture inbox', '返回采集收件箱'))}</a>`,
  ]
    .filter((value) => value.length > 0)
    .join('')

  return renderSectionShell(
    {
      title: lc('Triage Context and Next Step', '分流上下文与下一步'),
      copy: lc(
        'Capture stays navigational here: understand the current chain position, then move to Pending or the resulting record when needed.',
        '这里的采集保持导航角色：先理解当前链路位置，再在需要时进入待审或结果记录。',
      ),
      className: 'section-shell--secondary',
    },
    `
      <div class="field-grid">
        ${renderField(lc('Inbox Cue', '收件箱提示'), describeCaptureDetailInboxCue(detail), true)}
        ${renderField(lc('Downstream Linkage', '下游链接'), describeCaptureDetailLinkage(detail), true)}
        ${renderField(lc('Likely Next Page', '可能的下一页'), describeCaptureDetailNextStep(detail), true)}
        ${renderField(lc('Follow-up Status', '跟进状态'), describeCaptureDetailFollowUpStatus(detail), true)}
      </div>
      ${renderSectionActionRow(actionLinks)}
    `,
  )
}

function renderCaptureParseContextSection(detail: CaptureDetail): string {
  if (!detail.parse_result) {
    return renderSectionShell(
      {
        title: lc('Parse and Processing Context', '解析与处理上下文'),
        copy: lc('Parse context is secondary support for understanding how this capture moved downstream.', '解析上下文是帮助理解该采集如何流向下游的次级支持。'),
        className: 'section-shell--contextual',
      },
      renderPageStatePanel({
        tone: 'empty',
        eyebrow: lc('Parse', '解析'),
        title: lc('No parse result is available yet.', '还没有可用的解析结果。'),
        message: lc('This capture does not currently show a parse result in the visible chain.', '这条采集在当前可见链路中还没有显示解析结果。'),
      }),
    )
  }

  return renderSectionShell(
    {
      title: lc('Parse and Processing Context', '解析与处理上下文'),
      copy: lc(
        'Parse context stays concise and supports the user’s understanding of the chain without becoming an AI explanation surface.',
        '解析上下文保持简洁，用于帮助理解链路，而不会变成 AI 解释界面。',
      ),
      className: 'section-shell--contextual',
    },
    `
      <div class="field-grid">
        ${renderField('Parse Result ID', String(detail.parse_result.id))}
        ${renderField('Target Domain', formatDomainLabel(detail.parse_result.target_domain))}
        ${renderField('Confidence Level', formatStatusLabel(detail.parse_result.confidence_level))}
        ${renderField('Confidence Score', String(detail.parse_result.confidence_score))}
        ${renderField('Parser', `${detail.parse_result.parser_name} ${detail.parse_result.parser_version}`)}
        ${renderField('Parsed At', formatDateTime(detail.parse_result.created_at))}
      </div>
      <section class="subsection">
        <h3>${escapeHtml(lc('Parsed Payload Snapshot', '解析载荷快照'))}</h3>
        ${renderTextBlock(formatJson(detail.parse_result.parsed_payload_json))}
      </section>
    `,
  )
}

function renderCapturePendingLinkSection(detail: CaptureDetail): string {
  if (!detail.pending_item) {
    return renderSectionShell(
      {
        title: lc('Pending Review Linkage', '待审复核链接'),
        copy: lc(
          'Pending linkage stays simple: it shows whether this capture moved into the review workbench and where to go next.',
          '待审链接保持简单：只展示这条采集是否进入了复核工作台，以及下一步去哪里。',
        ),
        className: 'section-shell--secondary',
      },
      renderPageStatePanel({
        tone: 'empty',
        eyebrow: sc('Pending'),
        title: lc('No linked pending item is available.', '没有关联的待审项。'),
        message: lc('This capture did not create a visible pending review item, or it moved directly into a formal result.', '这条采集没有生成可见的待审复核项，或者已直接进入正式结果。'),
      }),
    )
  }

  return renderSectionShell(
    {
      title: lc('Pending Review Linkage', '待审复核链接'),
      copy: lc('Pending is the downstream review workbench for captures that need a formal decision.', '待审是需要正式决策的采集所进入的下游复核工作台。'),
      className: 'section-shell--secondary',
    },
    `
      <article class="record-card${detail.pending_item.actionable ? ' record-card--priority' : ''}">
        <div class="record-card__header">
          <div class="record-card__title-group">
            <h3>${escapeHtml(detail.pending_item.summary || `Pending #${detail.pending_item.id}`)}</h3>
            <span class="record-meta">Pending #${detail.pending_item.id}</span>
          </div>
          <a class="record-action" href="/pending/${detail.pending_item.id}" data-nav="true">${escapeHtml(localizeUiLabel('Open pending detail'))}</a>
        </div>
        <div class="record-badges">
          ${renderStatusBadge(detail.pending_item.status)}
          ${renderBadge(formatDomainLabel(detail.pending_item.target_domain), true)}
          ${detail.pending_item.actionable ? renderBadge(lc('Actionable', '可操作')) : renderBadge(lc('Resolved', '已解决'), true)}
        </div>
        <div class="field-grid">
          ${renderField('Pending Status', formatStatusLabel(detail.pending_item.status))}
          ${renderField('Target Domain', formatDomainLabel(detail.pending_item.target_domain))}
          ${renderField('Pending Summary', detail.pending_item.summary, true)}
          ${renderField('Reviewability', detail.pending_item.actionable ? lc('Still actionable in Pending', '在待审中仍可操作') : lc('No longer actionable', '已不可操作'))}
          ${renderField('Resolved At', detail.pending_item.resolved_at ? formatDateTime(detail.pending_item.resolved_at) : null)}
        </div>
      </article>
    `,
  )
}

function renderCaptureFormalResultSection(detail: CaptureDetail): string {
  if (!detail.formal_result) {
    return renderSectionShell(
      {
        title: lc('Formal Result and Resolution Context', '正式结果与解决上下文'),
        copy: lc(
          'Formal-result linkage stays restrained and only shows whether this capture already produced a formal record.',
          '正式结果链接保持克制，只展示这条采集是否已经生成正式记录。',
        ),
        className: 'section-shell--contextual',
      },
      renderPageStatePanel({
        tone: 'empty',
        eyebrow: lc('Formal Result', '正式结果'),
        title: lc('No formal result is linked yet.', '还没有关联的正式结果。'),
        message: lc('This capture has not yet produced a visible formal record, or it resolved without one.', '这条采集还没有生成可见的正式记录，或者是在没有正式记录的情况下结束。'),
      }),
    )
  }

  const href = buildFormalRecordHref(detail.formal_result.target_domain, detail.formal_result.record_id)

  return renderSectionShell(
    {
      title: lc('Formal Result and Resolution Context', '正式结果与解决上下文'),
      copy: lc(
        'Formal-result linkage shows the downstream fact record without turning Capture into a workflow console.',
        '正式结果链接展示下游事实记录，而不会把采集变成工作流控制台。',
      ),
      className: 'section-shell--contextual',
    },
    `
      <article class="record-card">
        <div class="record-card__header">
          <div class="record-card__title-group">
            <h3>${escapeHtml(detail.formal_result.summary || `${formatDomainLabel(detail.formal_result.target_domain)} record`)}</h3>
            <span class="record-meta">${escapeHtml(formatDateTime(detail.formal_result.created_at))}</span>
          </div>
          ${href ? `<a class="record-action" href="${escapeHtml(href)}" data-nav="true">${escapeHtml(localizeUiLabel('Open formal record'))}</a>` : ''}
        </div>
        <div class="record-badges">
          ${renderBadge(formatDomainLabel(detail.formal_result.target_domain), true)}
          ${detail.formal_result.source_pending_id ? renderBadge(lc(`Via Pending #${detail.formal_result.source_pending_id}`, `经由待审 #${detail.formal_result.source_pending_id}`)) : renderBadge(lc('Direct from capture', '直接来自采集'))}
        </div>
        <div class="field-grid">
          ${renderField('Target Domain', formatDomainLabel(detail.formal_result.target_domain))}
          ${renderField('Formal Record ID', String(detail.formal_result.record_id))}
          ${renderField('Result Summary', detail.formal_result.summary, true)}
          ${renderField('Resolution Path', detail.formal_result.source_pending_id ? lc(`Pending #${detail.formal_result.source_pending_id}`, `待审 #${detail.formal_result.source_pending_id}`) : lc('Direct commit from capture', '直接从采集提交'))}
        </div>
      </article>
    `,
  )
}

function renderPendingListView(
  query: PendingListQuery,
  response: PendingListResponse | null,
  errorMessage?: string,
): string {
  const queueSummary =
    response?.next_pending_item_id
      ? lc(
          `Next to review uses the earliest open pending item rule. Current hint: #${response.next_pending_item_id}.`,
          `下一个待审项按最早打开的待审规则确定。当前提示：#${response.next_pending_item_id}。`,
        )
      : lc(
          'Pending list helps you scan what still needs review, what is already resolved, and what to open next.',
          '待审列表帮助你查看哪些仍需复核、哪些已解决，以及下一步应打开什么。',
        )

  return renderPageShell(`
    ${renderPageHeaderBlock({
      title: sc('Pending'),
      copy: lc(
        'Pending is the review queue for scanning, prioritizing, and entering the single-item review workbench.',
        '待审是用于浏览、排序优先级并进入单项复核工作台的复核队列。',
      ),
    })}
    ${renderSectionShell(
      {
        title: lc('Queue Status', '队列状态'),
        copy: lc(
          'Open items remain actionable. Confirmed, discarded, and forced items stay readable as resolution context only.',
          '打开状态的项目仍可操作。已确认、已丢弃和已强制写入的项目仅作为解决上下文保留可读。',
        ),
        className: 'section-shell--contextual',
      },
      `
        <div class="field-grid">
          ${renderField('Filtered Total', String(response?.total ?? 0))}
          ${renderField('Next to Review', response?.next_pending_item_id === null || response?.next_pending_item_id === undefined ? localizeUiLabel('None') : `#${response.next_pending_item_id}`)}
          ${renderField('Current Status Filter', formatStatusLabel(query.status))}
          ${renderField('Current Domain Filter', query.targetDomain ? formatDomainLabel(query.targetDomain) : localizeUiLabel('All'))}
          ${renderField('Queue Note', queueSummary, true)}
        </div>
      `,
    )}
    ${renderPendingFilters(query)}
    ${renderSectionShell(
      {
        title: lc('Review Queue', '复核队列'),
        copy: lc(
          'Use the list to scan status, domain, current summary, and timestamps before opening the detail workbench.',
          '在打开详情工作台之前，可先用列表查看状态、域、当前摘要和时间戳。',
        ),
        className: 'section-shell--primary',
      },
      errorMessage
        ? renderUnavailableState(lc('Pending review queue is unavailable.', '待审复核队列不可用。'), errorMessage)
        : response && response.items.length > 0
          ? renderPendingRecords(response.items)
          : renderEmptyState(lc('No pending items found for the current queue filters.', '当前队列筛选条件下没有待审项。')),
    )}
    ${renderPagination('/pending', response?.page ?? query.page, response?.page_size ?? query.pageSize, response?.total ?? 0)}
  `)
}

function renderPendingDetailView(detail: PendingDetail): string {
  return renderPageShell(`
    ${renderPageHeaderBlock({
      title: sc('Pending Item'),
      backHref: '/pending',
      backLabel: sc('Pending'),
      copy: lc(
        'Pending detail is a single-item review workbench. Keep the current payload primary, then use source context, actions, and history as support.',
        '待审详情是单项复核工作台。先以当前载荷为主，再把来源上下文、动作和历史作为支持层使用。',
      ),
    })}
    ${renderSectionShell(
      {
        title: lc('Current Pending Item', '当前待审项'),
        copy: lc(
          'Keep the review frame visible before moving into payload, downstream context, or action choices.',
          '在进入载荷、下游上下文或动作选择之前，先保持复核框架清晰可见。',
        ),
        className: 'section-shell--primary',
      },
      `
        <div class="record-badges">
          ${renderStatusBadge(detail.status)}
          ${renderBadge(formatDomainLabel(detail.target_domain), true)}
          ${detail.actionable ? renderBadge(lc('Actionable', '可操作')) : renderBadge(lc('Resolved', '已解决'), true)}
          ${detail.summary ? renderBadge(lc('Current item', '当前项')) : ''}
        </div>
        <div class="field-grid">
          ${renderField('Pending ID', String(detail.id))}
          ${renderField('Current Status', formatStatusLabel(detail.status))}
          ${renderField('Target Domain', formatDomainLabel(detail.target_domain))}
          ${renderField('Created At', formatDateTime(detail.created_at))}
          ${renderField('Updated At', formatDateTime(detail.updated_at))}
          ${renderField('Resolved At', detail.resolved_at ? formatDateTime(detail.resolved_at) : null)}
        </div>
        ${renderOptionalTextSubsection(lc('Current Summary', '当前摘要'), detail.summary)}
        ${renderOptionalTextSubsection(lc('Review Reason', '复核原因'), detail.reason)}
      `,
    )}
    ${renderPendingCurrentPayloadSection(detail)}
    ${renderPendingSourceContextSection(detail)}
    ${renderPendingActionSection(detail)}
    ${renderPendingHistorySection(detail)}
  `)
}

function renderPendingCurrentPayloadSection(detail: PendingDetail): string {
  const effectiveSource = formatPendingEffectivePayloadSource(detail.effective_payload_source)
  const originalPayload =
    detail.effective_payload_source === 'corrected' ? detail.proposed_payload_json : detail.corrected_payload_json
  const originalTitle =
    detail.effective_payload_source === 'corrected'
      ? lc('Original Proposed Payload Snapshot', '原始建议载荷快照')
      : lc('Corrected Payload Snapshot', '修正载荷快照')

  return renderSectionShell(
    {
      title: lc('Current Review Payload', '当前复核载荷'),
      copy: lc(
        'Current review uses corrected payload when it exists. Otherwise it uses the proposed payload.',
        '当前复核在有修正载荷时使用修正载荷，否则使用建议载荷。',
      ),
      className: 'section-shell--primary',
    },
    `
      <div class="record-badges">
        ${renderBadge(lc(`Effective Source: ${effectiveSource}`, `生效来源：${effectiveSource}`))}
        ${detail.corrected_payload_json !== null ? renderBadge(lc('Corrected payload available', '已有修正载荷'), true) : renderBadge(lc('Using proposed payload', '正在使用建议载荷'), true)}
      </div>
      <div class="field-grid">
        ${renderField('Review Basis', effectiveSource)}
        ${renderField('Actionability', detail.actionable ? lc('Still reviewable', '仍可复核') : lc('Read-only resolved state', '只读已解决状态'))}
        ${renderField('Payload Origin Note', describePendingEffectivePayloadSource(detail.effective_payload_source), true)}
      </div>
      ${renderTextBlock(formatJson(detail.effective_payload_json))}
      ${
        originalPayload !== null
          ? `
              <section class="subsection">
                <h3>${escapeHtml(originalTitle)}</h3>
                ${renderTextBlock(formatJson(originalPayload))}
              </section>
            `
          : ''
      }
    `,
  )
}

function renderPendingSourceContextSection(detail: PendingDetail): string {
  return renderSectionShell(
    {
      title: lc('Source and Upstream Context', '来源与上游上下文'),
      copy: lc(
        'Source capture, parse reference, and intake reason stay contextual support for the current review decision.',
        '来源采集、解析引用和进入原因是当前复核决策的上下文支持。',
      ),
      className: 'section-shell--contextual source-reference-block',
    },
    `
      <div class="field-grid">
        ${renderField('Source Capture ID', String(detail.source_capture_id))}
        ${renderField('Parse Result ID', String(detail.parse_result_id))}
        ${renderField('Target Domain', formatDomainLabel(detail.target_domain))}
      </div>
      ${renderOptionalTextSubsection(
        lc('Source Relationship', '来源关系'),
        lc(`Capture #${detail.source_capture_id} -> Parse #${detail.parse_result_id} -> Pending #${detail.id}`, `采集 #${detail.source_capture_id} -> 解析 #${detail.parse_result_id} -> 待审 #${detail.id}`),
      )}
    `,
  )
}

function renderPendingActionSection(detail: PendingDetail): string {
  const feedback = renderPendingReviewFeedback(detail.id)
  const fixDraft = pendingUiState.fixDraftById[detail.id] ?? ''

  if (!detail.actionable) {
    return renderSectionShell(
      {
        title: lc('Review Actions', '复核动作'),
        copy: lc('Review actions stay close to the main payload, but resolved items are no longer actionable.', '复核动作保持贴近主载荷，但已解决项目不再可操作。'),
        className: 'section-shell--secondary',
      },
      `
        ${feedback}
        ${renderPageStatePanel({
          tone: 'ready',
          eyebrow: sc('Resolved'),
          title: localeState.current === 'zh' ? '待审项已不可操作。' : 'Pending item is no longer actionable.',
          message: lc(
            `This pending item is resolved as ${formatStatusLabel(detail.status)}. It remains readable for traceability, but review actions are disabled.`,
            `该待审项已按${formatStatusLabel(detail.status)}解决。它仍保留可读以便追溯，但复核动作已禁用。`,
          ),
        })}
      `,
    )
  }

  return renderSectionShell(
    {
      title: lc('Review Actions', '复核动作'),
      copy: lc(
        'Actions stay subordinate to understanding. Fix updates corrected payload only; confirm, discard, and force insert resolve the pending item under existing backend rules.',
        '动作必须从属于理解。Fix 只更新修正载荷；Confirm、Discard 和 Force Insert 按现有后端规则解决该待审项。',
      ),
      className: 'section-shell--secondary',
    },
    `
      ${feedback}
      <div class="workbench-card-grid">
        <article class="record-card">
          <div class="record-card__header">
            <div class="record-card__title-group">
              <h3>${escapeHtml(sc('Fix'))}</h3>
              <span class="record-meta">${escapeHtml(localeState.current === 'zh' ? '仅更新修正后的载荷' : 'Updates corrected payload only')}</span>
            </div>
          </div>
          <p class="section-copy">${escapeHtml(lc('Fix updates corrected payload, does not directly write a formal record, and keeps the item reviewable afterward.', 'Fix 会更新修正载荷，不会直接写入正式记录，并且之后该项仍可继续复核。'))}</p>
          <form class="filter-form" data-pending-form="fix" data-pending-id="${detail.id}">
            <label class="filter-field">
              <span>${escapeHtml(localeState.current === 'zh' ? '修正文案' : 'Correction Text')}</span>
              <textarea class="workbench-textarea" name="correction_text" placeholder="${escapeHtml(
                localeState.current === 'zh'
                  ? '输入 TraceFold 应重新解析为修正后载荷的修正文案。'
                  : 'Enter the corrected text TraceFold should parse into the corrected payload.',
              )}">${escapeHtml(fixDraft)}</textarea>
            </label>
            <div class="filter-actions">
              <button class="secondary-button" type="submit">${escapeHtml(sc('Apply Fix'))}</button>
            </div>
          </form>
        </article>
        <article class="record-card record-card--priority">
          <div class="record-card__header">
            <div class="record-card__title-group">
              <h3>${escapeHtml(sc('Confirm'))}</h3>
              <span class="record-meta">${escapeHtml(localeState.current === 'zh' ? '标准批准并提交路径' : 'Standard approve and commit path')}</span>
            </div>
          </div>
          <p class="section-copy">${escapeHtml(lc('Confirm writes the current effective payload to the formal record and resolves the pending item.', 'Confirm 会把当前生效载荷写入正式记录，并解决该待审项。'))}</p>
          <div class="filter-actions">
            <button class="primary-button" type="button" data-pending-action="confirm" data-pending-id="${detail.id}">${escapeHtml(sc('Confirm'))}</button>
          </div>
        </article>
        <article class="record-card">
          <div class="record-card__header">
            <div class="record-card__title-group">
              <h3>${escapeHtml(sc('Discard'))}</h3>
              <span class="record-meta">${escapeHtml(localeState.current === 'zh' ? '在不写入正式记录的情况下解决' : 'Resolve without writing a formal record')}</span>
            </div>
          </div>
          <p class="section-copy">${escapeHtml(lc('Discard resolves the pending item without writing a formal record. After discard, the item is no longer actionable.', 'Discard 会在不写入正式记录的情况下解决该待审项。丢弃后，该项不再可操作。'))}</p>
          <div class="filter-actions">
            <button class="secondary-button" type="button" data-pending-action="discard" data-pending-id="${detail.id}">${escapeHtml(sc('Discard'))}</button>
          </div>
        </article>
        <article class="record-card">
          <div class="record-card__header">
            <div class="record-card__title-group">
              <h3>${escapeHtml(sc('Force Insert'))}</h3>
              <span class="record-meta">${escapeHtml(localeState.current === 'zh' ? '显式强制写入路径' : 'Explicit force-insert path')}</span>
            </div>
          </div>
          <p class="section-copy">${escapeHtml(lc('Force Insert writes the current effective payload through the backend force-insert path and resolves the pending item. Use it only when that explicit path is intended.', 'Force Insert 会通过后端强制写入路径写入当前生效载荷，并解决该待审项。仅在明确需要该路径时使用。'))}</p>
          <div class="filter-actions">
            <button class="secondary-button" type="button" data-pending-action="force_insert" data-pending-id="${detail.id}">${escapeHtml(sc('Force Insert'))}</button>
          </div>
        </article>
      </div>
    `,
  )
}

function renderPendingReviewFeedback(pendingId: number): string {
  const feedback = pendingUiState.feedbackById[pendingId]
  if (!feedback) {
    return ''
  }

  return renderPageStatePanel({
    tone: feedback.kind === 'success' ? 'ready' : 'unavailable',
    eyebrow: feedback.kind === 'success' ? sc('Action Complete') : sc('Action Failed'),
    title: feedback.title,
    message: feedback.message,
  })
}

function renderPendingHistorySection(detail: PendingDetail): string {
  const formalResultMarkup = renderPendingFormalResult(detail.formal_result)
  const historyMarkup =
    detail.review_actions.length > 0
      ? renderPendingReviewActions(detail.review_actions)
      : renderEmptyState(lc('No review actions have been recorded for this pending item yet.', '该待审项还没有记录复核动作。'))

  return renderSectionShell(
    {
      title: lc('Review History and Resolution Context', '复核历史与解决上下文'),
      copy: lc(
        'Review history, resolution state, and any linked formal result stay secondary to the current payload and current decision.',
        '复核历史、解决状态以及关联的正式结果，都应次于当前载荷和当前决策。',
      ),
      className: 'section-shell--contextual',
    },
    `
      <section class="subsection">
        <h3>${escapeHtml(lc('Linked Formal Result', '关联的正式结果'))}</h3>
        ${formalResultMarkup}
      </section>
      <section class="subsection">
        <h3>${escapeHtml(lc('Recorded Review Actions', '已记录的复核动作'))}</h3>
        ${historyMarkup}
      </section>
    `,
  )
}

function renderPendingFormalResult(formalResult: PendingFormalResult | null): string {
  if (!formalResult) {
    return renderPageStatePanel({
      tone: 'empty',
      eyebrow: lc('Result', '结果'),
      title: lc('No formal result is linked yet.', '还没有关联的正式结果。'),
      message: lc('A formal result becomes available after confirm or force insert when a formal record is created for this pending item.', '当 confirm 或 force insert 为该待审项创建正式记录后，正式结果才会出现。'),
    })
  }

  const href = buildFormalRecordHref(formalResult.target_domain, formalResult.record_id)
  return `
    <article class="record-card">
      <div class="record-card__header">
        <div class="record-card__title-group">
          <h3>${escapeHtml(lc('Formal Result', '正式结果'))}</h3>
          <span class="record-meta">${escapeHtml(formatDomainLabel(formalResult.target_domain))}</span>
        </div>
        ${href ? `<a class="record-action" href="${escapeHtml(href)}" data-nav="true">${escapeHtml(localizeUiLabel('Open formal record'))}</a>` : ''}
      </div>
      <div class="field-grid">
        ${renderField('Target Domain', formatDomainLabel(formalResult.target_domain))}
        ${renderField('Formal Record ID', String(formalResult.record_id))}
      </div>
    </article>
  `
}

function renderPendingReviewActions(actions: PendingReviewAction[]): string {
  return actions
    .map(
      (action) => `
        <article class="record-card">
          <div class="record-card__header">
            <div class="record-card__title-group">
              <h3>${escapeHtml(formatPendingActionLabel(action.action_type))}</h3>
              <span class="record-meta">${escapeHtml(formatDateTime(action.created_at))}</span>
            </div>
            <div class="record-badges">
              ${renderBadge(formatStatusLabel(action.action_type), true)}
            </div>
          </div>
          <div class="field-grid">
            ${renderField('Action Type', formatStatusLabel(action.action_type))}
            ${renderField('Action Note', action.note, true)}
          </div>
          ${
            action.before_payload_json !== null
              ? `
                  <section class="subsection">
                    <h3>${escapeHtml(lc('Before Payload Snapshot', '操作前载荷快照'))}</h3>
                    ${renderTextBlock(formatJson(action.before_payload_json))}
                  </section>
                `
              : ''
          }
          ${
            action.after_payload_json !== null
              ? `
                  <section class="subsection">
                    <h3>${escapeHtml(lc('After Payload Snapshot', '操作后载荷快照'))}</h3>
                    ${renderTextBlock(formatJson(action.after_payload_json))}
                  </section>
                `
              : ''
          }
        </article>
      `,
    )
    .join('')
}

function renderExpenseListView(
  query: ExpenseListQuery,
  response: PaginatedResponse<ExpenseListItem> | null,
  errorMessage?: string,
): string {
  const headerMarkup = renderPageHeaderBlock({
    title: sc('Expenses'),
    copy: lc(
      'Expense is a formal record consumption surface. Use it to scan recorded facts first, then open a record for contextual support.',
      '支出是正式记录消费页面。先查看已记录事实，再打开记录查看上下文支持。',
    ),
  })
  const contextMarkup = renderSectionShell(
    {
      title: lc('Record Scope', '记录范围'),
      copy: lc(
        'Expense list stays formal-record-first. It is for reading recorded facts, not for turning the page into a chart or analytics center.',
        '支出列表保持正式记录优先。它用于读取已记录事实，而不是把页面变成图表或分析中心。',
      ),
      className: 'section-shell--contextual',
    },
    `
      <div class="field-grid">
        ${renderField('Filtered Total', String(response?.total ?? 0))}
        ${renderField('Current Category Filter', query.category || localizeUiLabel('All'))}
        ${renderField('Current Keyword Filter', query.keyword || localizeUiLabel('None'))}
        ${renderField('Current Sort', `${formatStatusLabel(query.sortBy)} · ${query.sortOrder}`)}
      </div>
    `,
  )

  if (errorMessage) {
    return renderPageShell(`
      ${headerMarkup}
      ${contextMarkup}
      ${renderExpenseFilters(query)}
      ${renderUnavailableState(lc('Expense records are unavailable.', '支出记录不可用。'), errorMessage)}
    `)
  }

  return renderPageShell(`
    ${headerMarkup}
    ${contextMarkup}
    ${renderExpenseFilters(query)}
    ${renderSectionShell(
      {
        title: lc('Formal Records', '正式记录'),
        copy: lc(
          'Use the list to scan amount, category, recorded time, and source path before opening expense detail.',
          '在打开支出详情前，可先用列表查看金额、类别、记录时间和来源路径。',
        ),
        className: 'section-shell--primary',
      },
      response && response.items.length > 0
        ? renderExpenseRecords(response.items)
        : renderEmptyState(lc('No expense records found for the current filters.', '当前筛选条件下没有支出记录。')),
    )}
    ${renderPagination('/expense', response?.page ?? query.page, response?.page_size ?? query.pageSize, response?.total ?? 0)}
  `)
}

function renderKnowledgeListView(
  query: KnowledgeListQuery,
  response: PaginatedResponse<KnowledgeListItem> | null,
  errorMessage?: string,
): string {
  return renderPageShell(`
    ${renderPageHeaderBlock({
      title: sc('Knowledge'),
      copy: lc(
        'Knowledge is a formal record consumption surface. Read formal content first, then use source context and AI-derived summary as support on detail pages.',
        '知识是正式记录消费页面。先阅读正式内容，再在详情页把来源上下文和 AI 派生摘要作为支持层使用。',
      ),
    })}
    ${renderKnowledgeFilters(query)}
    ${renderSectionShell(
      {
        title: lc('Formal Records', '正式记录'),
        copy: lc(
          'Use the list to scan titles, content previews, and provenance support before opening knowledge detail.',
          '在打开知识详情前，可先用列表查看标题、内容预览和来源支持。',
        ),
        className: 'section-shell--primary',
      },
      errorMessage
        ? renderUnavailableState(lc('Knowledge records are unavailable.', '知识记录不可用。'), errorMessage)
        : response && response.items.length > 0
          ? renderKnowledgeRecords(response.items)
          : renderEmptyState(lc('No knowledge entries found.', '没有找到知识记录。')),
    )}
    ${renderPagination(
      '/knowledge',
      response?.page ?? query.page,
      response?.page_size ?? query.pageSize,
      response?.total ?? 0,
    )}
  `)
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
        title: sc('Health'),
        copy: lc('Formal health records with separate rule-based reminders.', '正式健康记录，配有独立的规则提醒支持。'),
      })}
      ${renderHealthFilters(query)}
      ${renderUnavailableState(lc('Health records are unavailable.', '健康记录不可用。'), errorMessage)}
    `)
  }

  const factsSection = renderSectionShell(
    {
      title: lc('Formal Records', '正式记录'),
      copy: lc('Formal health records remain the primary read layer for this page.', '正式健康记录仍是本页的主要读取层。'),
      className: 'section-shell--primary',
    },
    response && response.items.length > 0
      ? renderHealthRecords(response.items)
      : renderEmptyState(lc('No health records found.', '没有找到健康记录。')),
  )
  const alertsSection = renderHealthAlertSection(
    alerts,
    alertsErrorMessage,
    {
      heading: lc('Rule Alerts', '规则提醒'),
      emptyMessage: lc('No rule alerts for health records.', '健康记录没有规则提醒。'),
      emphasize: query.focusAlerts,
    },
  )

  return renderPageShell(`
    ${renderPageHeaderBlock({
      title: sc('Health'),
      copy: lc('Formal health records with separate rule-based reminders.', '正式健康记录，配有独立的规则提醒支持。'),
    })}
    ${renderHealthFilters(query)}
    ${factsSection}
    ${alertsSection}
    ${renderPagination('/health', response?.page ?? query.page, response?.page_size ?? query.pageSize, response?.total ?? 0)}
  `)
}

function renderExpenseDetailView(detail: ExpenseDetail): string {
  return renderPageShell(`
    ${renderPageHeaderBlock({
      title: sc('Expense Record'),
      backHref: '/expense',
      backLabel: sc('Expenses'),
      copy: lc(
        'Read the formal expense record first, then use source provenance and record path as contextual support.',
        '先阅读正式支出记录，再把来源说明和记录路径作为上下文支持使用。',
      ),
    })}
    ${renderSectionShell(
      {
        title: lc('Formal Expense Record', '正式支出记录'),
        copy: lc('Formal expense fields remain the truth-bearing content for this page. Keep the amount, category, and path readable before tracing provenance.', '正式支出字段仍是本页承载事实的主要内容。先保持金额、类别和路径清晰可读，再追踪来源。'),
        className: 'section-shell--primary',
      },
      `
        <div class="record-badges">
          ${renderBadge(`${detail.amount} ${detail.currency}`)}
          ${detail.category ? renderBadge(formatDomainLabel(detail.category), true) : renderBadge(lc('Uncategorized', '未分类'), true)}
          ${detail.source_pending_id === null ? renderBadge(lc('Direct from Capture', '直接来自采集')) : renderBadge(lc('Reviewed from Pending', '经待审复核'))}
        </div>
        <div class="field-grid">
          ${renderField('Expense ID', String(detail.id))}
          ${renderField('Recorded At', formatDateTime(detail.created_at))}
          ${renderField('Amount', detail.amount)}
          ${renderField('Currency', detail.currency)}
          ${renderField('Category', detail.category)}
          ${renderField('Record Path', describeExpenseSourcePath(detail.source_pending_id))}
        </div>
        ${renderOptionalTextSubsection(lc('Formal Note', '正式备注'), detail.note)}
      `,
    )}
    ${renderExpenseSourceContextSection(detail)}
  `)
}

function renderExpenseSourceContextSection(detail: ExpenseDetail): string {
  const sourceLinks = [
    `<a class="record-action" href="/capture/${detail.source_capture_id}" data-nav="true">${escapeHtml(localizeUiLabel('Open source capture'))}</a>`,
    detail.source_pending_id === null
      ? ''
      : `<a class="record-action" href="/pending/${detail.source_pending_id}" data-nav="true">${escapeHtml(localizeUiLabel('Open source pending'))}</a>`,
  ]
    .filter((value) => value.length > 0)
    .join('')

  return renderSectionShell(
    {
      title: lc('Source and Record Context', '来源与记录上下文'),
      copy: lc(
        'Source reference stays contextual support. It helps trace formal provenance without turning Expense into a workflow or analytics center.',
        '来源说明保持为上下文支持。它帮助追踪正式来源，而不会把支出页面变成工作流或分析中心。',
      ),
      className: 'section-shell--contextual source-reference-block',
    },
    `
      ${
        sourceLinks
          ? renderSectionActionRow(sourceLinks)
          : ''
      }
      <div class="field-grid">
        ${renderField('Source Capture ID', String(detail.source_capture_id))}
        ${renderField('Source Pending ID', detail.source_pending_id === null ? null : String(detail.source_pending_id))}
        ${renderField('Source Path', describeExpenseSourcePath(detail.source_pending_id), true)}
      </div>
    `,
  )
}

function renderKnowledgeDetailView(
  detail: KnowledgeDetail,
  aiSummaryState: KnowledgeAiSummaryState,
): string {
  const sourceLinks = [
    `<a class="record-action" href="/capture/${detail.source_capture_id}" data-nav="true">${escapeHtml(localizeUiLabel('Open source capture'))}</a>`,
    detail.source_pending_id === null
      ? ''
      : `<a class="record-action" href="/pending/${detail.source_pending_id}" data-nav="true">${escapeHtml(localizeUiLabel('Open source pending'))}</a>`,
  ]
    .filter((value) => value.length > 0)
    .join('')

  return renderPageShell(`
    ${renderPageHeaderBlock({
      title: sc('Knowledge Record'),
      backHref: '/knowledge',
      backLabel: sc('Knowledge'),
      copy: lc('Read the formal content first, then use source reference and AI-derived summary as support.', '先阅读正式内容，再把来源说明和 AI 派生摘要作为支持层使用。'),
    })}
    ${renderSectionShell(
      {
        title: lc('Formal Content', '正式内容'),
        copy: lc('Formal content remains the record of truth for this knowledge entry. Keep the title and body easy to read before moving into support layers.', '正式内容仍是这条知识记录的事实来源。先让标题和正文易于阅读，再进入支持层。'),
        className: 'section-shell--primary',
      },
      `
        <div class="record-badges">
          ${renderBadge(lc('Formal record', '正式记录'))}
          ${detail.source_pending_id === null ? renderBadge(lc('Direct from Capture', '直接来自采集'), true) : renderBadge(lc('Reviewed from Pending', '经待审复核'), true)}
        </div>
        <div class="field-grid">
          ${renderField('Knowledge ID', String(detail.id))}
          ${renderField('Created At', formatDateTime(detail.created_at))}
          ${renderField('Title', detail.title, true)}
        </div>
        <section class="subsection">
          <h3>${escapeHtml(lc('Formal Body', '正式正文'))}</h3>
          ${renderTextBlock(detail.content)}
        </section>
      `,
    )}
    ${renderSectionShell(
      {
        title: lc('Source Reference', '来源说明'),
        copy: lc('Source reference stays contextual. It helps trace origin without replacing the formal record.', '来源说明保持为上下文层。它帮助追踪来源，但不会取代正式记录。'),
        className: 'section-shell--contextual source-reference-block',
      },
      `
        ${sourceLinks ? renderSectionActionRow(sourceLinks) : ''}
        <div class="field-grid">
          ${renderField('Source Capture ID', String(detail.source_capture_id))}
          ${renderField('Source Pending ID', detail.source_pending_id === null ? null : String(detail.source_pending_id))}
        </div>
        ${renderOptionalTextSubsection(lc('Source Text Snapshot', '来源文本快照'), detail.source_text)}
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
      title: sc('Health Record'),
      backHref: '/health',
      backLabel: sc('Health'),
      copy: lc('Read the formal record first, then use source context and rule alerts as support.', '先阅读正式记录，再把来源上下文和规则提醒作为支持层使用。'),
    })}
    ${renderSectionShell(
      {
        title: lc('Formal Record', '正式记录'),
        copy: lc('Formal health record values remain the record of truth for this page. Keep the metric and recorded value readable before moving into alerts.', '正式健康记录数值仍是本页的事实来源。先让指标和记录值易于阅读，再进入提醒层。'),
        className: 'section-shell--primary',
      },
      `
        <div class="record-badges">
          ${renderBadge(detail.metric_type)}
          ${detail.source_pending_id === null ? renderBadge(lc('Direct from Capture', '直接来自采集'), true) : renderBadge(lc('Reviewed from Pending', '经待审复核'), true)}
        </div>
        <div class="field-grid">
          ${renderField('ID', String(detail.id))}
          ${renderField('Created At', formatDateTime(detail.created_at))}
          ${renderField('Metric Type', detail.metric_type)}
        </div>
        <section class="subsection">
          <h3>${escapeHtml(lc('Recorded Value', '记录值'))}</h3>
          ${renderTextBlock(detail.value_text)}
        </section>
        ${renderOptionalTextSubsection(lc('Supporting Note', '补充说明'), detail.note)}
      `,
    )}
    ${renderSourceSection(detail.source_capture_id, detail.source_pending_id)}
    ${renderHealthAlertSection(alerts, alertsErrorMessage, {
      heading: lc('Rule Alerts', '规则提醒'),
      emptyMessage: lc('No rule alerts for this health record.', '这条健康记录没有规则提醒。'),
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
    ${renderUnavailableState(localeState.current === 'zh' ? `${title}不可用。` : `${title} is unavailable.`, message)}
  `)
}

function renderCaptureFilters(query: CaptureListQuery): string {
  return renderFilterSection(
    '/capture',
    `
      ${renderDateInput('date_from', 'Date From', query.dateFrom)}
      ${renderDateInput('date_to', 'Date To', query.dateTo)}
      ${renderSelectInput('status', 'Status', query.status, [
        ['', 'All'],
        ['received', 'received'],
        ['parsed', 'parsed'],
        ['pending', 'pending'],
        ['committed', 'committed'],
        ['discarded', 'discarded'],
        ['failed', 'failed'],
      ])}
      ${renderTextInput('source_type', 'Source Type', query.sourceType)}
      ${renderSelectInput('sort_by', 'Sort By', query.sortBy, [
        ['created_at', 'created_at'],
        ['status', 'status'],
        ['source_type', 'source_type'],
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
        <h2>${escapeHtml(localizeUiLabel('Filters'))}</h2>
      </div>
      <form class="filter-form" data-list-form="true" data-path="${path}">
        <div class="filter-grid">
          ${controls}
        </div>
        <div class="filter-actions">
          <button class="primary-button" type="submit">${escapeHtml(localizeUiLabel('Apply filters'))}</button>
          <button class="secondary-button" type="button" data-reset-path="${path}">${escapeHtml(localizeUiLabel('Reset'))}</button>
        </div>
      </form>
    </section>
  `
}

function renderCaptureRecords(items: CaptureListItem[]): string {
  return items
    .map(
      (item) => {
        const nextHref = buildCaptureInboxNextHref(item)
        const nextLabel = buildCaptureInboxNextActionLabel(item)
        const detailHref = `/capture/${item.id}`
        return `
        <article class="record-card${captureNeedsFollowUp(item) ? ' record-card--priority' : ''}">
          <div class="record-card__header">
            <div class="record-card__title-group">
              <h3>${escapeHtml(item.summary || `Capture #${item.id}`)}</h3>
              <span class="record-meta">Capture #${item.id}</span>
            </div>
            <a class="record-action" href="${detailHref}" data-nav="true">${escapeHtml(lc('Open capture detail', '打开采集详情'))}</a>
          </div>
          <div class="record-badges">
            ${renderStatusBadge(item.status)}
            ${renderBadge(lc(`Stage: ${formatCaptureStageLabel(item.current_stage)}`, `阶段：${formatCaptureStageLabel(item.current_stage)}`))}
            ${renderBadge(lc(`Source: ${item.source_type}`, `来源：${item.source_type}`), true)}
            ${item.target_domain ? renderBadge(formatDomainLabel(item.target_domain), true) : ''}
            ${captureNeedsFollowUp(item) ? renderBadge(lc('Needs follow-up', '需要跟进')) : renderBadge(lc('Resolved downstream', '下游已解决'), true)}
            ${captureHasPendingLink(item) ? renderBadge(lc('Pending linked', '已关联待审'), true) : ''}
            ${captureHasFormalResult(item) ? renderBadge(lc('Formalized', '已进入正式记录'), true) : ''}
          </div>
          <div class="field-grid">
            ${renderField('Current Summary', item.summary, true)}
            ${renderField('Current Stage', formatCaptureStageLabel(item.current_stage))}
            ${renderField('Target Domain', item.target_domain ? formatDomainLabel(item.target_domain) : lc('Not available yet', '暂不可用'))}
            ${renderField(lc('Downstream Linkage', '下游链接'), describeCaptureInboxLinkage(item), true)}
            ${renderField(lc('Next Step', '下一步'), describeCaptureInboxNextStep(item), true)}
            ${renderField('Source Reference', item.source_ref, true)}
            ${renderField('Created At', formatDateTime(item.created_at))}
            ${renderField('Updated At', formatDateTime(item.updated_at))}
          </div>
          <div class="filter-actions">
            ${
              nextHref && nextHref !== detailHref
                ? `<a class="record-action" href="${nextHref}" data-nav="true">${escapeHtml(nextLabel)}</a>`
                : ''
            }
          </div>
        </article>
      `
      },
    )
    .join('')
}

function renderPendingRecords(items: PendingListItem[]): string {
  return items
    .map(
      (item) => `
        <article class="record-card${item.is_next_to_review ? ' record-card--priority' : ''}">
          <div class="record-card__header">
            <div class="record-card__title-group">
              <h3>${escapeHtml(item.summary || `Pending #${item.id}`)}</h3>
              <span class="record-meta">Pending #${item.id}</span>
            </div>
            <a class="record-action" href="/pending/${item.id}" data-nav="true">${escapeHtml(localizeUiLabel('Open'))}</a>
          </div>
          <div class="record-badges">
            ${renderStatusBadge(item.status)}
            ${renderBadge(formatDomainLabel(item.target_domain), true)}
            ${item.has_corrected_payload ? renderBadge(lc('Has corrected payload', '有修正载荷')) : ''}
            ${item.is_next_to_review ? renderBadge(lc('Next to review', '下一个待审')) : ''}
            ${item.status === 'open' ? renderBadge(lc('Actionable', '可操作')) : renderBadge(lc('Resolved', '已解决'), true)}
          </div>
          <div class="field-grid">
            ${renderField('Current Summary', item.summary, true)}
            ${renderField('Reason Preview', item.reason_preview, true)}
            ${renderField('Created At', formatDateTime(item.created_at))}
            ${renderField('Updated At', formatDateTime(item.updated_at))}
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
      (item) => {
        const sourcePath = item.has_source_pending ? describeExpenseSourcePath(1) : describeExpenseSourcePath(null)
        return `
        <article class="record-card">
          <div class="record-card__header">
            <div class="record-card__title-group">
              <h3>${escapeHtml(`${item.amount} ${item.currency}`)}</h3>
              <span class="record-meta">${escapeHtml(formatDateTime(item.created_at))}</span>
            </div>
            <a class="record-action" href="/expense/${item.id}" data-nav="true">${escapeHtml(localizeUiLabel('Open'))}</a>
          </div>
          <div class="record-badges">
            ${renderBadge(`${item.amount} ${item.currency}`)}
            ${item.category ? renderBadge(formatDomainLabel(item.category), true) : renderBadge(lc('Uncategorized', '未分类'), true)}
            ${item.has_source_pending ? renderBadge(lc('Reviewed from Pending', '经待审复核')) : renderBadge(lc('Direct from Capture', '直接来自采集'), true)}
          </div>
          <div class="field-grid">
            ${renderField('Recorded At', formatDateTime(item.created_at))}
            ${renderField('Amount', `${item.amount} ${item.currency}`)}
            ${renderField('Category', item.category)}
            ${renderField('Source Path', sourcePath, true)}
            ${renderField('Note Preview', item.note_preview, true)}
          </div>
        </article>
      `
      },
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
            <a class="record-action" href="/knowledge/${item.id}" data-nav="true">${escapeHtml(localizeUiLabel('Open'))}</a>
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
            <a class="record-action" href="/health/${item.id}" data-nav="true">${escapeHtml(localizeUiLabel('Open'))}</a>
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
      copy: lc(
        'Rule-based alerts are reminders derived from formal health records. They do not replace or rewrite the formal record itself.',
        '基于规则的提醒来自正式健康记录。它们不会取代或重写正式记录本身。',
      ),
      className: `${options.emphasize ? 'page-section--alert-focus ' : ''}section-shell--secondary`.trim(),
      id: 'health-alerts',
    },
    `
      ${
        errorMessage
          ? renderUnavailableState(lc('Health alerts are unavailable right now.', '健康提醒当前不可用。'), errorMessage)
          : filteredAlerts.length > 0
            ? `
                ${renderAlertStatusGroup(lc('Open Alerts', '打开提醒'), openAlerts, lc('Open alerts still need attention. This does not mean the formal record was changed.', '打开提醒仍需处理。这并不表示正式记录已被更改。'))}
                ${renderAlertStatusGroup(lc('Acknowledged Alerts', '已确认提醒'), acknowledgedAlerts, lc('Acknowledged means the alert was seen. It does not mean the formal health record was modified.', '已确认表示提醒已被查看。这并不表示正式健康记录已被修改。'))}
                ${renderAlertStatusGroup(lc('Resolved Alerts', '已解决提醒'), resolvedAlerts, lc('Resolved means the reminder was handled. It does not mean the health fact was automatically corrected.', '已解决表示提醒已被处理。这并不表示健康事实被自动更正。'))}
                ${
                  otherAlerts.length > 0
                    ? renderAlertStatusGroup(lc('Other Alert Lifecycle States', '其他提醒生命周期状态'), otherAlerts, lc('Additional alert lifecycle states remain reminders rather than formal fact changes.', '其他提醒生命周期状态仍然只是提醒，而不是正式事实变化。'))
                    : ''
                }
              `
            : renderEmptyState(options.emptyMessage, lc('Health alerts are currently empty.', '健康提醒当前为空。'))
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
      title: lc('AI-derived Summary', 'AI 派生摘要'),
      copy: lc(
        'AI-derived summary is generated from the formal record. It does not replace the formal content.',
        'AI 派生摘要由正式记录生成。它不会取代正式内容。',
      ),
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
        lc('AI-derived summary is unavailable right now.', 'AI 派生摘要当前不可用。'),
        aiSummaryState.errorMessage || 'TraceFold AI derivation request failed.',
      )
    case 'not-generated':
      return renderPageStatePanel({
        tone: 'empty',
        eyebrow: lc('Not Generated', '未生成'),
        title: lc('AI-derived summary is not available yet.', 'AI 派生摘要尚不可用。'),
        message: lc('The formal content remains available.', '正式内容仍然可用。'),
        actions: `<button class="secondary-button" type="button" data-ai-action="recompute-knowledge-summary" data-record-id="${knowledgeId}">${escapeHtml(lc('Generate AI-derived Summary', '生成 AI 派生摘要'))}</button>`,
      })
    default:
      return renderKnowledgeAiSummaryContent(aiSummaryState.derivation, knowledgeId)
  }
}

function renderKnowledgeAiSummaryContent(aiSummary: AiDerivationResultItem | null, knowledgeId: number): string {
  if (!aiSummary) {
    return renderPageStatePanel({
      tone: 'empty',
      eyebrow: lc('Not Generated', '未生成'),
      title: lc('AI-derived summary is not available yet.', 'AI 派生摘要尚不可用。'),
      message: lc('The formal content remains available.', '正式内容仍然可用。'),
      actions: `<button class="secondary-button" type="button" data-ai-action="recompute-knowledge-summary" data-record-id="${knowledgeId}">${escapeHtml(lc('Generate AI-derived Summary', '生成 AI 派生摘要'))}</button>`,
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
          <h3>${escapeHtml(lc('Generated summary', '已生成摘要'))}</h3>
          <span class="record-meta">${escapeHtml(formatDateTime(generationTime))}</span>
        </div>
      <div class="record-badges">
        ${renderAiStatusBadge(aiSummary.status)}
      </div>
      </div>
      ${
        aiSummary.status === 'failed'
          ? `
              <p class="section-copy">${escapeHtml(lc('AI-derived summary generation failed. The formal content remains available.', 'AI 派生摘要生成失败。正式内容仍然可用。'))}</p>
              <p class="section-copy">${escapeHtml(aiSummary.error_message || 'AI derivation failed. The formal record remains available.')}</p>
            `
          : aiSummary.status === 'invalidated'
            ? `
                <p class="section-copy">${escapeHtml(lc('AI-derived summary is invalidated and should be recomputed before relying on it.', 'AI 派生摘要已失效，在依赖它之前应重新计算。'))}</p>
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
              ? `<p class="section-copy">${escapeHtml(lc('AI-derived summary recompute has been requested. Refresh the page if the status remains pending.', '已请求重新计算 AI 派生摘要。如果状态仍为待处理，请刷新页面。'))}</p>`
              : `
                <section class="subsection">
                  <h3>${escapeHtml(lc('Summary', '摘要'))}</h3>
                  ${renderTextBlock(summaryText)}
                </section>
                <section class="subsection">
                  <h3>${escapeHtml(lc('Key Points', '要点'))}</h3>
                  ${renderDataList(
                    keyPoints.map((item, index) => ({ label: `Point ${index + 1}`, value: item })),
                    lc('No key points available.', '没有可用要点。'),
                  )}
                </section>
                <section class="subsection">
                  <h3>${escapeHtml(lc('Keywords', '关键词'))}</h3>
                  ${renderInlineList(keywords, lc('No keywords available.', '没有可用关键词。'))}
                </section>
              `
      }
      <section class="subsection">
        <h3>${escapeHtml(lc('Derivation Context', '派生上下文'))}</h3>
        <div class="field-grid">
          ${renderField('Derivation Status', formatStatusLabel(aiSummary.status))}
          ${renderField('Model Key', aiSummary.model_key || aiSummary.model_name)}
          ${renderField('Model Version', aiSummary.model_version)}
        </div>
      </section>
      <div class="section-action-row alert-actions">
        <button class="secondary-button" type="button" data-ai-action="recompute-knowledge-summary" data-record-id="${knowledgeId}">
          ${escapeHtml(lc('Recompute AI-derived Summary', '重新计算 AI 派生摘要'))}
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
              <h3>${escapeHtml(item.title || lc('Rule alert', '规则提醒'))}</h3>
              <span class="record-meta">${escapeHtml(formatDateTime(item.triggered_at))}</span>
            </div>
            <a class="record-action" href="/health/${item.source_record_id}" data-nav="true">${escapeHtml(localizeUiLabel('Open record'))}</a>
          </div>
          <div class="record-badges">
            ${renderSeverityBadge(item.severity)}
            ${renderStatusBadge(item.status)}
          </div>
          <div class="field-grid">
            ${renderField('Rule Key', item.rule_key || item.rule_code || null)}
            ${renderField('Source Record', lc(`Health #${item.source_record_id}`, `健康 #${item.source_record_id}`))}
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
                    <button class="secondary-button" type="button" data-alert-action="acknowledge" data-alert-id="${item.id}">${escapeHtml(lc('Acknowledge Alert', '确认提醒'))}</button>
                    <button class="secondary-button" type="button" data-alert-action="resolve" data-alert-id="${item.id}">${escapeHtml(lc('Resolve Alert', '解决提醒'))}</button>
                  </div>
                `
              : item.status === 'acknowledged'
                ? `
                    <div class="section-action-row alert-actions">
                      <button class="secondary-button" type="button" data-alert-action="resolve" data-alert-id="${item.id}">${escapeHtml(lc('Resolve Alert', '解决提醒'))}</button>
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
                <p class="activity-card__target">${escapeHtml(item.title_or_preview || lc('No preview available.', '没有可用预览。'))}</p>
                <p class="section-copy">${escapeHtml(lc(`Target #${item.target_id}`, `目标 #${item.target_id}`))}</p>
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
        <h2>${escapeHtml(localizeUiLabel('Pagination'))}</h2>
      </div>
      <div class="pagination-row">
        <button
          class="secondary-button"
          type="button"
          data-path="${path}"
          data-page-target="${page - 1}"
          ${hasPrevious ? '' : 'disabled'}
        >
          ${escapeHtml(localizeUiLabel('Previous'))}
        </button>
        <div class="pagination-summary">
          <span>${escapeHtml(lc(`Page ${page} of ${totalPages}`, `第 ${page} / ${totalPages} 页`))}</span>
          <span>${escapeHtml(lc(`${total} total`, `共 ${total} 条`))}</span>
        </div>
        <button
          class="secondary-button"
          type="button"
          data-path="${path}"
          data-page-target="${page + 1}"
          ${hasNext ? '' : 'disabled'}
        >
          ${escapeHtml(localizeUiLabel('Next'))}
        </button>
      </div>
    </section>
  `
}

function renderSourceSection(sourceCaptureId: number, sourcePendingId: number | null): string {
  const sourceLinks = [
    `<a class="record-action" href="/capture/${sourceCaptureId}" data-nav="true">${escapeHtml(localizeUiLabel('Open source capture'))}</a>`,
    sourcePendingId === null
      ? ''
      : `<a class="record-action" href="/pending/${sourcePendingId}" data-nav="true">${escapeHtml(localizeUiLabel('Open source pending'))}</a>`,
  ]
    .filter((value) => value.length > 0)
    .join('')

  return renderSectionShell(
    {
      title: lc('Source Reference', '来源说明'),
      copy: lc('Source reference stays contextual. It supports traceability without replacing the formal record.', '来源说明保持为上下文层。它支持追溯，但不会取代正式记录。'),
      className: 'section-shell--contextual source-reference-block',
    },
    `
      ${sourceLinks ? renderSectionActionRow(sourceLinks) : ''}
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
    eyebrow: sc('Loading'),
    title: sc('Loading shared page inputs.'),
    message: sc('Loading state is shown while the current route is still fetching its shared API inputs.'),
  })
}

function renderEmptyState(message: string, title = sc('This section is currently empty.')): string {
  return renderPageStatePanel({
    tone: 'empty',
    eyebrow: sc('Empty'),
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
      <button class="secondary-button" type="button" data-retry="true">${escapeHtml(sc('Retry'))}</button>
    </section>
  `
}

function renderUnavailableState(title: string, message: string): string {
  const signal = classifyFailureSignal(message)
  return renderPageStatePanel({
    tone: 'unavailable',
    eyebrow: sc('Unavailable'),
    title,
    message: sc('Unavailable means the shared API route could not be reached or returned an unusable response.'),
    details: [signal.message, signal.recoveryHint, signal.formalFactsNote].filter((value): value is string => Boolean(value)),
    retry: true,
  })
}

function renderDegradedState(title: string, message: string, details: string[]): string {
  return renderPageStatePanel({
    tone: 'degraded',
    eyebrow: sc('Degraded'),
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

  const actionMarkup = [options.actions, options.retry ? `<button class="secondary-button" type="button" data-retry="true">${escapeHtml(sc('Retry'))}</button>` : '']
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
    return renderUnavailableState(sc('System status is unavailable.'), runtimeStatusError)
  }

  if (!runtimeStatus) {
    return renderPageStatePanel({
      tone: 'empty',
      eyebrow: sc('Empty'),
      title: sc('System status has not been loaded yet.'),
      message: sc('Empty means the shared API responded successfully, but this page does not have records or summary data yet.'),
    })
  }

  const statusDetails = [
    sc('API: {value}', { value: formatStatusLabel(runtimeStatus.api_status) }),
    sc('Database: {value}', { value: formatStatusLabel(runtimeStatus.db_status) }),
    sc('Migration: {value}', { value: formatStatusLabel(runtimeStatus.migration_status) }),
    sc('Task runtime: {value}', { value: formatStatusLabel(runtimeStatus.task_runtime_status) }),
    sc('Last checked: {value}', { value: formatDateTime(runtimeStatus.last_checked_at) }),
  ]

  if (isRuntimeStatusDegraded(runtimeStatus)) {
    return renderDegradedState(
      sc('System status is degraded.'),
      sc('Degraded means /api/system/status reported a shared runtime warning even though reads may still succeed.'),
      [
        ...statusDetails,
        ...runtimeStatus.degraded_reasons.flatMap((reason) => [
          sc('Degraded reason: {value}', { value: formatStatusLabel(reason) }),
          sc('Recovery note: {value}', { value: describeRuntimeDegradedReason(reason) }),
        ]),
      ],
    )
  }

  return renderPageStatePanel({
    tone: 'ready',
    eyebrow: sc('Ready'),
    title: sc('System status is ready.'),
    message: sc('The shared API, database, migrations, and task runtime are available for workbench and dashboard reads.'),
    details: statusDetails,
  })
}

function renderLocalOperabilitySection(
  localOperability: LocalOperabilityData | null,
  localOperabilityError?: string | null,
): string {
  return renderSectionShell(
    {
      title: sc('Local Continuity'),
      copy: sc(
        'Backup, restore, and bounded capture transfer stay explicit and local-first here. SQLite remains the single source of truth, and this section stays support-level rather than becoming an admin console.',
      ),
      className: 'workbench-section section-shell--support',
    },
    localOperabilityError
      ? renderUnavailableState(sc('Local continuity support is unavailable.'), localOperabilityError)
      : !localOperability
        ? renderEmptyState(sc('Local continuity support has not been loaded yet.'), sc('Local continuity is currently empty.'))
        : `
            ${renderLocalOperabilityFeedback()}
            ${renderLocalOperabilityReadinessPanel(localOperability)}
            <div class="workbench-card-grid">
              <article class="record-card">
                <div class="record-card__header">
                  <div class="record-card__title-group">
                    <h3>${escapeHtml(sc('Local SQLite context'))}</h3>
                    <span class="record-meta">${escapeHtml(sc('Single source of truth'))}</span>
                  </div>
                </div>
                <p class="section-copy">${escapeHtml(localOperability.guidance[0] || 'SQLite remains the single source of truth for local TraceFold data.')}</p>
                <div class="field-grid">
                  ${renderField(localeState.current === 'zh' ? '数据库路径' : 'Database Path', localOperability.database_path, true)}
                  ${renderField(localeState.current === 'zh' ? '数据库文件是否存在' : 'Database File Present', formatBoolean(localOperability.database_exists))}
                  ${renderField(localeState.current === 'zh' ? '备份目录' : 'Backup Directory', localOperability.backup_directory)}
                  ${renderField(localeState.current === 'zh' ? '传输目录' : 'Transfer Directory', localOperability.transfer_directory)}
                  ${renderField(localeState.current === 'zh' ? '日常使用准备度' : 'Daily-use Readiness', formatStatusLabel(localOperability.daily_use_readiness))}
                </div>
              </article>
              <article class="record-card">
                <div class="record-card__header">
                  <div class="record-card__title-group">
                    <h3>${escapeHtml(sc('Local backup'))}</h3>
                    <span class="record-meta">${escapeHtml(sc('Full SQLite copy'))}</span>
                  </div>
                </div>
                <p class="section-copy">${escapeHtml(localOperability.backup_scope)}</p>
                <form class="filter-form" data-system-form="backup">
                  <div class="filter-grid">
                    ${renderTextInput('destination_path', sc('Backup Path (Optional)'), localOperabilityUiState.backupDestinationPath)}
                  </div>
                  <div class="filter-actions">
                    <button class="primary-button" type="submit">${escapeHtml(sc('Create local backup'))}</button>
                  </div>
                </form>
              </article>
              <article class="record-card">
                <div class="record-card__header">
                  <div class="record-card__title-group">
                    <h3>${escapeHtml(sc('Local restore'))}</h3>
                    <span class="record-meta">${escapeHtml(sc('Bounded replacement'))}</span>
                  </div>
                </div>
                <p class="section-copy">${escapeHtml(localOperability.restore_scope)}</p>
                <form class="filter-form" data-system-form="restore">
                  <div class="filter-grid">
                    ${renderTextInput('source_path', sc('Backup File Path'), localOperabilityUiState.restoreSourcePath)}
                  </div>
                  <label class="workbench-checkbox">
                    <input type="checkbox" name="create_safety_backup" ${localOperabilityUiState.restoreCreateSafetyBackup ? 'checked' : ''} />
                    <span>${escapeHtml(sc('Create a safety backup of the current SQLite file before restore.'))}</span>
                  </label>
                  <label class="workbench-checkbox">
                    <input type="checkbox" name="confirm_restore" ${localOperabilityUiState.restoreConfirmed ? 'checked' : ''} />
                    <span>${escapeHtml(sc('I understand restore replaces the active local SQLite database file.'))}</span>
                  </label>
                  <div class="filter-actions">
                    <button class="secondary-button" type="submit">${escapeHtml(sc('Restore local database'))}</button>
                  </div>
                </form>
              </article>
              <article class="record-card">
                <div class="record-card__header">
                  <div class="record-card__title-group">
                    <h3>${escapeHtml(sc('Capture bundle export'))}</h3>
                    <span class="record-meta">${escapeHtml(sc('Bounded transfer'))}</span>
                  </div>
                </div>
                <p class="section-copy">${escapeHtml(localOperability.export_scope)}</p>
                <form class="filter-form" data-system-form="export">
                  <div class="filter-grid">
                    ${renderTextInput('export_destination_path', sc('Export File Path (Optional)'), localOperabilityUiState.exportDestinationPath)}
                  </div>
                  <div class="filter-actions">
                    <button class="secondary-button" type="submit">${escapeHtml(sc('Export capture bundle'))}</button>
                  </div>
                </form>
              </article>
              <article class="record-card">
                <div class="record-card__header">
                  <div class="record-card__title-group">
                    <h3>${escapeHtml(sc('Capture bundle import'))}</h3>
                    <span class="record-meta">${escapeHtml(sc('Existing intake path'))}</span>
                  </div>
                </div>
                <p class="section-copy">${escapeHtml(localOperability.import_scope)}</p>
                <form class="filter-form" data-system-form="import">
                  <div class="filter-grid">
                    ${renderTextInput('import_source_path', sc('Import File Path'), localOperabilityUiState.importSourcePath)}
                  </div>
                  <div class="filter-actions">
                    <button class="secondary-button" type="submit">${escapeHtml(sc('Import capture bundle'))}</button>
                    <a class="record-action" href="/capture" data-nav="true">${escapeHtml(sc('Open capture records'))}</a>
                  </div>
                </form>
              </article>
            </div>
          `,
  )
}

function renderLocalOperabilityFeedback(): string {
  if (!localOperabilityUiState.feedback) {
    return ''
  }

  return renderPageStatePanel({
    tone: localOperabilityUiState.feedback.kind === 'success' ? 'ready' : 'unavailable',
    eyebrow: localOperabilityUiState.feedback.kind === 'success' ? sc('Complete') : sc('Unavailable'),
    title: localOperabilityUiState.feedback.title,
    message: localOperabilityUiState.feedback.message,
    details: localOperabilityUiState.feedback.details,
  })
}

function renderLocalOperabilityReadinessPanel(localOperability: LocalOperabilityData): string {
  if (localOperability.daily_use_readiness === 'daily_use_ready' && localOperability.warnings.length === 0) {
    return renderPageStatePanel({
      tone: 'ready',
      eyebrow: sc('Ready'),
      title: sc('Local daily-use continuity is ready.'),
      message: localOperability.readiness_message,
      details: localOperability.guidance.slice(1),
    })
  }

  return renderDegradedState(
    sc('Daily-use transition needs attention.'),
    localOperability.readiness_message,
    [...localOperability.warnings, ...localOperability.guidance.slice(1)],
  )
}

function renderWorkbenchStateBanner(
  home: WorkbenchHomeData,
  dashboard: DashboardData | null,
  dashboardError?: string | null,
): string {
  if (dashboardError) {
    return renderUnavailableState(lc('Workbench summary inputs are partially unavailable.', '工作台摘要输入部分不可用。'), dashboardError)
  }

  if (!isWorkbenchEmpty(home, dashboard)) {
    return ''
  }

  return renderPageStatePanel({
    tone: 'empty',
    eyebrow: sc('Empty'),
    title: lc('Workbench is currently empty.', '工作台当前为空。'),
    message: sc('Empty means the shared API responded successfully, but this page does not have records or summary data yet.'),
  })
}

function renderField(label: string, value: string | null, wide = false): string {
  return `
    <div class="field${wide ? ' field--wide' : ''}">
      <span class="field__label">${escapeHtml(localizeUiLabel(label))}</span>
      <span class="field__value">${escapeHtml(value || '—')}</span>
    </div>
  `
}

function renderTextBlock(value: string | null): string {
  return `
    <pre class="text-block">${escapeHtml(value || '—')}</pre>
  `
}

function renderOptionalTextSubsection(title: string, value: string | null): string {
  if (!value) {
    return ''
  }

  return `
    <section class="subsection">
      <h3>${escapeHtml(title)}</h3>
      ${renderTextBlock(value)}
    </section>
  `
}

function renderDateInput(name: string, label: string, value: string): string {
  return `
    <label class="filter-field">
      <span>${escapeHtml(localizeUiLabel(label))}</span>
      <input type="date" name="${name}" value="${escapeHtml(value)}" />
    </label>
  `
}

function renderTextInput(name: string, label: string, value: string): string {
  return `
    <label class="filter-field">
      <span>${escapeHtml(localizeUiLabel(label))}</span>
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
      <span>${escapeHtml(localizeUiLabel(label))}</span>
      <select name="${name}">
        ${options
          .map(
            ([value, text]) => `
              <option value="${escapeHtml(value)}" ${value === selectedValue ? 'selected' : ''}>
                ${escapeHtml(localizeUiLabel(text))}
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

function parseCaptureListQuery(params: URLSearchParams): CaptureListQuery {
  return {
    page: parsePositiveInt(params.get('page'), 1),
    pageSize: parsePageSize(params.get('page_size')),
    sortBy: parseSortBy(params.get('sort_by'), ['created_at', 'status', 'source_type'], 'created_at'),
    sortOrder: parseSortOrder(params.get('sort_order')),
    dateFrom: params.get('date_from') ?? '',
    dateTo: params.get('date_to') ?? '',
    status: parseOption(params.get('status'), ['received', 'parsed', 'pending', 'committed', 'discarded', 'failed'], ''),
    sourceType: params.get('source_type') ?? '',
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

function buildCaptureApiParams(query: CaptureListQuery): Record<string, string> {
  return {
    page: String(query.page),
    page_size: String(query.pageSize),
    sort_by: query.sortBy,
    sort_order: query.sortOrder,
    status: query.status,
    source_type: query.sourceType,
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
    ['dashboard', formatDomainLabel('dashboard')],
    ['pending', formatDomainLabel('pending')],
    ['expense', formatDomainLabel('expense')],
    ['knowledge', formatDomainLabel('knowledge')],
    ['health', formatDomainLabel('health')],
    ['alerts', formatDomainLabel('alerts')],
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
      <span>${escapeHtml(localeState.current === 'zh' ? '目标类型' : 'Target type')}</span>
      <select name="target_type">
        <option value="module_view" ${value === 'module_view' ? 'selected' : ''}>${escapeHtml(localeState.current === 'zh' ? '模块视图' : 'Module view')}</option>
        <option value="route" ${value === 'route' ? 'selected' : ''}>${escapeHtml(localeState.current === 'zh' ? '路由' : 'Route')}</option>
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
    return sc('No target payload.')
  }
  if (shortcut.target_type === 'route') {
    return sc('Route: {value}', { value: asString(payload.route) || sc('Not set') })
  }
  const parts = [formatDomainLabel(asString(payload.module) || 'dashboard')]
  const viewKey = asString(payload.view_key)
  if (viewKey) {
    parts.push(sc('view {value}', { value: viewKey }))
  }
  const querySummary = summarizeQuery(payload.query)
  if (querySummary !== sc('None')) {
    parts.push(querySummary)
  }
  return parts.join(' · ')
}

function summarizeQuery(value: unknown): string {
  const record = asRecord(value)
  if (!record || Object.keys(record).length === 0) {
    return sc('None')
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
    throw new Error(sc('JSON input is invalid.'))
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
  return date.toLocaleString(localeState.current === 'zh' ? 'zh-CN' : 'en-GB')
}

function formatByteSize(value: number): string {
  if (!Number.isFinite(value) || value < 1024) {
    return `${Math.max(0, Math.round(value))} B`
  }
  if (value < 1024 * 1024) {
    return `${(value / 1024).toFixed(1)} KB`
  }
  if (value < 1024 * 1024 * 1024) {
    return `${(value / (1024 * 1024)).toFixed(1)} MB`
  }
  return `${(value / (1024 * 1024 * 1024)).toFixed(1)} GB`
}

function formatBoolean(value: boolean): string {
  return value ? sc('Yes') : sc('No')
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

function describeRuntimeDegradedReason(reason: string): string {
  switch (reason) {
    case 'database_unavailable':
      return localeState.current === 'zh'
        ? 'SQLite 数据库当前不可访问。请检查配置的本地数据库路径和文件权限。'
        : 'SQLite database is not reachable. Check the configured local database path and file permissions.'
    case 'migration_head_unavailable':
      return localeState.current === 'zh'
        ? '无法读取迁移 head。请先检查迁移文件，再信任当前 schema 状态。'
        : 'Migration head could not be read. Check migration files before trusting schema state.'
    case 'migration_state_error':
      return localeState.current === 'zh'
        ? '无法干净地读取当前 schema 版本。请针对当前 SQLite 文件重新运行迁移。'
        : 'Current schema revision could not be read cleanly. Re-run migrations against the active SQLite file.'
    case 'schema_not_initialized':
      return localeState.current === 'zh'
        ? 'SQLite 可访问，但 schema 表尚未初始化。请在日常使用前先运行迁移。'
        : 'SQLite is reachable but schema tables are not initialized yet. Run migrations before daily use.'
    case 'migration_not_at_head':
      return localeState.current === 'zh'
        ? 'SQLite schema 落后于当前迁移 head。依赖正式写入前请先应用迁移。'
        : 'SQLite schema is behind the current migration head. Apply migrations before relying on formal writes.'
    case 'task_runtime_unavailable':
      return localeState.current === 'zh'
        ? '无法读取后台任务运行时状态。即使支持信号不完整，正式页面仍可能可用。'
        : 'Background task runtime status could not be read. Formal pages may still be usable while support signals are incomplete.'
    case 'ai_derivation_runtime_unavailable':
      return localeState.current === 'zh'
        ? '无法读取 AI 派生运行时状态。正式页面仍是主要内容，并且可能仍可用。'
        : 'AI derivation runtime status could not be read. Formal pages remain primary and may still be available.'
    default:
      return localeState.current === 'zh'
        ? '继续日常使用前，请检查 API 健康状态和当前本地 SQLite 运行情况。'
        : 'Check API health and the active local SQLite runtime before continuing daily use.'
  }
}

function formatStatusLabel(value: string): string {
  const localized = STATUS_LABELS[localeState.current][value]
  if (localized) {
    return localized
  }
  return value
    .split('_')
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ')
}

function formatDomainLabel(value: string): string {
  const localized = DOMAIN_LABELS[localeState.current][value]
  if (localized) {
    return localized
  }
  return value
    .split('_')
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ')
}

function formatPendingActionLabel(value: string): string {
  return value === 'force_insert' ? sc('Force Insert') : formatStatusLabel(value)
}

function describePendingActionRunningLabel(action: PendingReviewActionType): string {
  switch (action) {
    case 'confirm':
      return sc('Confirming...')
    case 'discard':
      return sc('Discarding...')
    case 'force_insert':
      return sc('Force inserting...')
  }
}

function buildPendingActionSuccessFeedback(result: PendingActionResult): PendingReviewFeedback {
  if (result.action_type === 'confirm') {
    return {
      kind: 'success',
      title: sc('Pending item confirmed.'),
      message: sc('Confirm wrote the current effective payload to the formal record and resolved the pending item.'),
    }
  }

  if (result.action_type === 'discard') {
    return {
      kind: 'success',
      title: sc('Pending item discarded.'),
      message: sc('Discard resolved the pending item without writing a formal record.'),
    }
  }

  return {
    kind: 'success',
    title: sc('Pending item force inserted.'),
    message: sc('Force Insert wrote the current effective payload through the backend force-insert path and resolved the pending item.'),
  }
}

function buildCaptureSubmissionSuccessFeedback(result: CaptureSubmitResult): CaptureSubmissionFeedback {
  if (result.route === 'pending') {
    return {
      kind: 'success',
      title: lc('Capture record created.', '采集记录已创建。'),
      message: lc(
        `Capture #${result.capture_id} was created and routed to Pending for formal review.`,
        `采集 #${result.capture_id} 已创建，并已路由到待审进行正式复核。`,
      ),
      captureId: result.capture_id,
    }
  }

  return {
    kind: 'success',
    title: lc('Capture record created.', '采集记录已创建。'),
    message: lc(
      `Capture #${result.capture_id} was created and committed to the formal ${formatDomainLabel(result.target_domain)} line under existing backend rules.`,
      `采集 #${result.capture_id} 已创建，并按现有后端规则提交到正式${formatDomainLabel(result.target_domain)}线。`,
    ),
    captureId: result.capture_id,
  }
}

function buildBulkIntakeSuccessFeedback(
  preview: BulkCapturePreviewResult,
  result: BulkCaptureImportResult,
): BulkIntakeFeedback {
  return {
    kind: 'success',
    title: lc('Bulk intake completed.', '批量导入已完成。'),
    message: lc(
      `Imported ${result.imported_count} capture records from ${result.file_name}.`,
      `已从 ${result.file_name} 导入 ${result.imported_count} 条采集记录。`,
    ),
    details: [
      lc(`Previewed candidates: ${preview.candidate_count}`, `预览候选项：${preview.candidate_count}`),
      lc(`Imported into Pending: ${result.pending_count}`, `进入待审：${result.pending_count}`),
      lc(`Committed directly under existing backend rules: ${result.committed_count}`, `按现有后端规则直接提交：${result.committed_count}`),
      lc(`Skipped before or during import: ${preview.invalid_count + result.skipped_count}`, `导入前或导入时跳过：${preview.invalid_count + result.skipped_count}`),
    ],
  }
}

function captureNeedsFollowUp(item: CaptureListItem): boolean {
  return !captureHasFormalResult(item) && item.current_stage !== 'discarded'
}

function captureHasPendingLink(item: CaptureListItem): boolean {
  return item.pending_item_id !== null
}

function captureHasFormalResult(item: CaptureListItem): boolean {
  return item.formal_record_id !== null || item.current_stage === 'formal_record'
}

function pickSuggestedCaptureInboxItem(items: CaptureListItem[]): CaptureListItem | null {
  const prioritized = [...items].sort((left, right) => captureInboxPriority(left) - captureInboxPriority(right))
  return prioritized.find((item) => captureInboxPriority(item) < 90) ?? null
}

function captureInboxPriority(item: CaptureListItem): number {
  if (item.current_stage === 'pending_review') {
    return 0
  }
  if (item.current_stage === 'received') {
    return 1
  }
  if (item.current_stage === 'parsed') {
    return 2
  }
  if (item.current_stage === 'failed') {
    return 3
  }
  if (item.current_stage === 'discarded') {
    return 90
  }
  if (captureHasFormalResult(item)) {
    return 99
  }
  return 10
}

function describeCaptureInboxLinkage(item: CaptureListItem): string {
  if (item.formal_record_id !== null && item.target_domain) {
    if (item.formal_source_pending_id !== null) {
      return lc(
        `Pending #${item.formal_source_pending_id} resolved into ${formatDomainLabel(item.target_domain)} #${item.formal_record_id}.`,
        `待审 #${item.formal_source_pending_id} 已解析为 ${formatDomainLabel(item.target_domain)} #${item.formal_record_id}。`,
      )
    }
    return lc(
      `${formatDomainLabel(item.target_domain)} #${item.formal_record_id} is already available as a formal record.`,
      `${formatDomainLabel(item.target_domain)} #${item.formal_record_id} 已作为正式记录可用。`,
    )
  }

  if (item.pending_item_id !== null) {
    return lc(
      `Pending #${item.pending_item_id} is linked as the downstream review item.`,
      `待审 #${item.pending_item_id} 已作为下游复核项关联。`,
    )
  }

  if (item.target_domain) {
    return lc(
      `Target domain is currently ${formatDomainLabel(item.target_domain)}, but no visible pending or formal linkage is available yet.`,
      `当前目标域是 ${formatDomainLabel(item.target_domain)}，但还没有可见的待审或正式记录链接。`,
    )
  }

  return lc(
    'This capture is still visible as upstream intake with no downstream linkage yet.',
    '这条采集目前仍是上游录入状态，尚无下游链接。',
  )
}

function describeCaptureInboxNextStep(item: CaptureListItem): string {
  if (item.pending_item_id !== null && item.current_stage === 'pending_review') {
    return lc(
      `Continue in Pending #${item.pending_item_id} for formal review.`,
      `前往待审 #${item.pending_item_id} 继续正式复核。`,
    )
  }

  if (item.formal_record_id !== null && item.target_domain) {
    return lc(
      `View the resulting ${formatDomainLabel(item.target_domain)} record #${item.formal_record_id}.`,
      `查看生成的 ${formatDomainLabel(item.target_domain)} 记录 #${item.formal_record_id}。`,
    )
  }

  if (item.pending_item_id !== null) {
    return lc(
      `Open Pending #${item.pending_item_id} to inspect the downstream outcome.`,
      `打开待审 #${item.pending_item_id} 查看下游结果。`,
    )
  }

  if (item.current_stage === 'received') {
    return lc('Open capture detail and wait for downstream processing to appear.', '打开采集详情，并等待下游处理结果出现。')
  }

  if (item.current_stage === 'parsed') {
    return lc('Open capture detail to inspect parse context before following downstream.', '打开采集详情，在进入下游前先查看解析上下文。')
  }

  if (item.current_stage === 'failed') {
    return lc('Open capture detail to inspect the failed chain context.', '打开采集详情，查看失败的链路上下文。')
  }

  return lc('Open capture detail for the latest visible chain context.', '打开采集详情查看最新可见链路上下文。')
}

function buildCaptureInboxNextHref(item: CaptureListItem): string | null {
  if (item.pending_item_id !== null && item.current_stage === 'pending_review') {
    return `/pending/${item.pending_item_id}`
  }

  if (item.formal_record_id !== null && item.target_domain) {
    return buildFormalRecordHref(item.target_domain, item.formal_record_id)
  }

  if (item.pending_item_id !== null) {
    return `/pending/${item.pending_item_id}`
  }

  return `/capture/${item.id}`
}

function buildCaptureInboxNextActionLabel(item: CaptureListItem): string {
  if (item.pending_item_id !== null && item.current_stage === 'pending_review') {
    return lc('Continue in Pending', '前往待审')
  }

  if (item.formal_record_id !== null && item.target_domain) {
    return lc('View resulting record', '查看结果记录')
  }

  if (item.pending_item_id !== null) {
    return lc('Open pending outcome', '打开待审结果')
  }

  return lc('Open triage detail', '打开分流详情')
}

function formatPendingEffectivePayloadSource(value: string): string {
  return value === 'corrected' ? lc('Corrected Payload', '修正载荷') : lc('Proposed Payload', '建议载荷')
}

function describePendingEffectivePayloadSource(value: string): string {
  return value === 'corrected'
    ? lc(
        'Review is currently based on corrected payload because a fix has already updated the pending item.',
        '当前复核基于修正载荷，因为 fix 已经更新了该待审项。',
      )
    : lc(
        'Review is currently based on proposed payload because no corrected payload has been saved yet.',
        '当前复核基于建议载荷，因为尚未保存修正载荷。',
      )
}

function buildFormalRecordHref(targetDomain: string, recordId: number): string | null {
  switch (targetDomain) {
    case 'expense':
      return `/expense/${recordId}`
    case 'knowledge':
      return `/knowledge/${recordId}`
    case 'health':
      return `/health/${recordId}`
    default:
      return null
  }
}

function describeExpenseSourcePath(sourcePendingId: number | null): string {
  return sourcePendingId === null
    ? lc('Capture -> Expense', '采集 -> 支出')
    : lc('Capture -> Pending -> Expense', '采集 -> 待审 -> 支出')
}

function formatCaptureStageLabel(value: string): string {
  return value === 'formal_record' ? lc('Formal Record', '正式记录') : formatStatusLabel(value)
}

function describeCaptureDetailInboxCue(detail: CaptureDetail): string {
  if (detail.formal_result) {
    return detail.formal_result.source_pending_id !== null
      ? lc('This capture has already moved through Pending and produced a formal record.', '这条采集已经经过待审并生成了正式记录。')
      : lc('This capture has already produced a formal record directly under the existing backend rules.', '这条采集已经按现有后端规则直接生成了正式记录。')
  }

  if (detail.pending_item?.actionable) {
    return lc('This capture now needs downstream review in Pending rather than more intake-side action here.', '这条采集现在需要在待审中继续复核，而不是在这里做更多录入侧操作。')
  }

  if (detail.pending_item) {
    return lc('This capture already has downstream review context, but that review item is no longer actionable.', '这条采集已经有下游复核上下文，但该复核项已不再可操作。')
  }

  if (detail.parse_result) {
    return lc('This capture has parse context, but no visible Pending or formal linkage yet.', '这条采集已经有解析上下文，但还没有可见的待审或正式记录链接。')
  }

  if (detail.current_stage === 'failed') {
    return lc('This capture is still visible in the inbox because downstream processing did not complete.', '这条采集仍留在收件箱中，因为下游处理没有完成。')
  }

  return lc('This capture is still acting as a new upstream intake item.', '这条采集当前仍是新的上游录入项。')
}

function describeCaptureDetailLinkage(detail: CaptureDetail): string {
  if (detail.formal_result && detail.target_domain) {
    if (detail.formal_result.source_pending_id !== null) {
      return lc(
        `Pending #${detail.formal_result.source_pending_id} resolved into ${formatDomainLabel(detail.target_domain)} #${detail.formal_result.record_id}.`,
        `待审 #${detail.formal_result.source_pending_id} 已解析为 ${formatDomainLabel(detail.target_domain)} #${detail.formal_result.record_id}。`,
      )
    }
    return lc(
      `${formatDomainLabel(detail.target_domain)} #${detail.formal_result.record_id} is already available as the resulting formal record.`,
      `${formatDomainLabel(detail.target_domain)} #${detail.formal_result.record_id} 已作为结果正式记录可用。`,
    )
  }

  if (detail.pending_item) {
    return lc(
      `Pending #${detail.pending_item.id} is the visible downstream review item for this capture.`,
      `待审 #${detail.pending_item.id} 是这条采集当前可见的下游复核项。`,
    )
  }

  if (detail.parse_result) {
    return lc(
      `Parse #${detail.parse_result.id} is visible, but no pending or formal record linkage is available yet.`,
      `解析 #${detail.parse_result.id} 已可见，但还没有待审或正式记录链接。`,
    )
  }

  return lc(
    'No downstream linkage is visible yet. This capture is still acting as an upstream record.',
    '尚无可见的下游链接。这条采集仍作为上游记录存在。',
  )
}

function describeCaptureDetailNextStep(detail: CaptureDetail): string {
  if (detail.pending_item?.actionable) {
    return lc(
      `Continue in Pending #${detail.pending_item.id} for the formal review decision.`,
      `前往待审 #${detail.pending_item.id} 继续完成正式复核决策。`,
    )
  }

  if (detail.formal_result && detail.target_domain) {
    return lc(
      `View the resulting ${formatDomainLabel(detail.target_domain)} record #${detail.formal_result.record_id}.`,
      `查看生成的 ${formatDomainLabel(detail.target_domain)} 记录 #${detail.formal_result.record_id}。`,
    )
  }

  if (detail.pending_item) {
    return lc(
      `Open Pending #${detail.pending_item.id} to inspect the resolved downstream review context.`,
      `打开待审 #${detail.pending_item.id} 查看已解决的下游复核上下文。`,
    )
  }

  if (detail.parse_result) {
    return lc('Stay on capture detail for parse context, then return to the inbox if you need another intake item.', '继续留在采集详情查看解析上下文，如需查看其他录入项再返回收件箱。')
  }

  return lc('Return to the capture inbox after checking this upstream record.', '查看完这条上游记录后返回采集收件箱。')
}

function describeCaptureDetailFollowUpStatus(detail: CaptureDetail): string {
  if (detail.pending_item?.actionable) {
    return lc('Still needs follow-up in Pending.', '仍需在待审中继续跟进。')
  }

  if (detail.formal_result) {
    return lc('No more intake-side follow-up is needed here.', '这里不再需要录入侧跟进。')
  }

  if (detail.pending_item) {
    return lc('Downstream review is already resolved; this page remains for intake visibility only.', '下游复核已经解决；此页仅保留录入可见性。')
  }

  if (detail.parse_result) {
    return lc('Waiting for downstream linkage to become visible.', '正在等待下游链接变得可见。')
  }

  if (detail.current_stage === 'failed') {
    return lc('Downstream processing did not finish cleanly yet.', '下游处理尚未顺利完成。')
  }

  return lc('Still new on the intake side.', '在录入侧仍属于新项。')
}

function buildCaptureDetailNextHref(detail: CaptureDetail): string | null {
  if (detail.pending_item?.actionable) {
    return `/pending/${detail.pending_item.id}`
  }

  if (detail.formal_result && detail.target_domain) {
    return buildFormalRecordHref(detail.target_domain, detail.formal_result.record_id)
  }

  if (detail.pending_item) {
    return `/pending/${detail.pending_item.id}`
  }

  return null
}

function buildCaptureDetailNextActionLabel(detail: CaptureDetail): string | null {
  if (detail.pending_item?.actionable) {
    return lc('Continue in Pending', '前往待审')
  }

  if (detail.formal_result) {
    return lc('View resulting record', '查看结果记录')
  }

  if (detail.pending_item) {
    return lc('Open pending outcome', '打开待审结果')
  }

  return null
}

function isSupportedBulkTextFile(fileName: string): boolean {
  const normalized = fileName.trim().toLowerCase()
  return normalized.endsWith('.txt') || normalized.endsWith('.md')
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
  return sc('TraceFold request failed. Check the shared API and try again.')
}

function classifyFailureSignal(message: string): FailureSignal {
  const normalized = message.toLowerCase()

  if (normalized.includes('api is unavailable') || normalized.includes('service is unavailable')) {
    return {
      message,
      recoveryHint: sc('Check /api/healthz first. If the API is healthy, confirm the API base URL in .env.'),
      formalFactsNote: sc('This entry-side failure does not change existing formal records.'),
    }
  }

  if (normalized.includes('invalid response')) {
    return {
      message,
      recoveryHint: sc('Check API health first, then inspect the API process or logs.'),
      formalFactsNote: sc('This response failure does not change existing formal records.'),
    }
  }

  if (normalized.includes('workbench url could not be opened')) {
    return {
      message,
      recoveryHint: sc('Check the Desktop workbench URL and confirm the Web dev server is running.'),
      formalFactsNote: sc('This shell-side failure does not change existing formal records.'),
    }
  }

  if (normalized.includes('workbench url is invalid') || normalized.includes('workbench url is not configured')) {
    return {
      message,
      recoveryHint: sc('Check the Desktop .env workbench URL setting.'),
      formalFactsNote: sc('This shell-side failure does not change existing formal records.'),
    }
  }

  if (normalized.includes('request failed with status')) {
    return {
      message,
      recoveryHint: sc('Check API health first. If the API is healthy, inspect the requested route or filters.'),
      formalFactsNote: sc('Formal records remain unchanged unless a successful write already completed.'),
    }
  }

  return {
    message,
    recoveryHint: sc('Retry once. If it still fails, check API health and the relevant .env settings.'),
    formalFactsNote: sc('This entry-side failure does not imply formal facts are damaged.'),
  }
}
