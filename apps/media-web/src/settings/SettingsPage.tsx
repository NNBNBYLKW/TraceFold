import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";

import { ApiError, apiClient } from "../shared/api/client";
import type {
  ProcessorSettingRead,
  RuntimeSettingsPatch,
  RuntimeSettingsRead,
} from "../shared/api/types";
import { settingsKeys, useRuntimeSettings, useSystemStatus } from "./queries";


function formatDate(value: string | null): string {
  if (!value) return "尚未扫描";
  const date = new Date(value);
  if (Number.isNaN(date.valueOf())) return value;
  return new Intl.DateTimeFormat("zh-CN", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(date);
}

export function SettingsPage() {
  const queryClient = useQueryClient();
  const settings = useRuntimeSettings();
  const status = useSystemStatus();
  const [form, setForm] = useState<RuntimeSettingsRead | null>(null);

  useEffect(() => {
    if (settings.data) setForm(settings.data);
  }, [settings.data]);

  const save = useMutation({
    mutationFn: (payload: RuntimeSettingsPatch) =>
      apiClient.patch<RuntimeSettingsRead>("/settings", payload),
    onSuccess: async (updated) => {
      setForm(updated);
      queryClient.setQueryData(settingsKeys.all, updated);
      await queryClient.invalidateQueries({ queryKey: settingsKeys.status });
    },
  });

  function updateProcessor(name: string, patch: Partial<ProcessorSettingRead>) {
    setForm((current) => current ? {
      ...current,
      processors: current.processors.map((processor) =>
        processor.processor_name === name ? { ...processor, ...patch } : processor,
      ),
    } : current);
  }

  const busyMessage = save.error instanceof ApiError && save.error.code === "SETTINGS_BUSY"
    ? "存在正在处理或待恢复的文件，暂时不能修改运行目录。"
    : save.error instanceof ApiError
      ? save.error.message
      : save.error
        ? "设置未能保存。"
        : null;

  return (
    <section className="settings-page">
      <header className="page-heading">
        <div>
          <p className="eyebrow">03 / SETTINGS</p>
          <h1>设置</h1>
          <p>管理运行目录、自动扫描与可扩展前置处理器。</p>
        </div>
      </header>

      {status.data && (status.data.recovery_in_progress || !status.data.ingest_enabled) && (
        <div className="recovery-banner" role="status">
          <strong>正在恢复未完成操作</strong>
          <span>文件库仍可浏览；恢复完成前，正式入库暂时不可用。</span>
        </div>
      )}

      <section className="diagnostics-panel" aria-labelledby="diagnostics-title">
        <div className="section-heading">
          <div>
            <p className="eyebrow">SYSTEM / DIAGNOSTICS</p>
            <h2 id="diagnostics-title">运行诊断</h2>
          </div>
          <span className={`connection-chip ${status.error ? "is-error" : "is-online"}`}>
            {status.error ? "状态不可用" : "本地服务"}
          </span>
        </div>
        {status.data && (
          <div className="diagnostic-grid">
            <div className={status.data.ffmpeg_available ? "is-good" : "is-bad"}>
              <span>FFMPEG</span>
              <strong>{status.data.ffmpeg_available ? "FFmpeg 可用" : "FFmpeg 不可用"}</strong>
            </div>
            <div className={status.data.ffprobe_available ? "is-good" : "is-bad"}>
              <span>FFPROBE</span>
              <strong>{status.data.ffprobe_available ? "FFprobe 可用" : "FFprobe 不可用"}</strong>
            </div>
            <div className={status.data.directories_writable ? "is-good" : "is-bad"}>
              <span>STORAGE</span>
              <strong>{status.data.directories_writable ? "目录可写" : "目录不可写"}</strong>
            </div>
            <div>
              <span>LAST SCAN</span>
              <strong>{formatDate(status.data.last_scan_at)}</strong>
            </div>
            <div>
              <span>PROCESSING</span>
              <strong>队列中 {status.data.queued_jobs} 项</strong>
            </div>
            <div>
              <span>RECOVERY</span>
              <strong>可恢复操作 {status.data.recoverable_operations} 项</strong>
            </div>
          </div>
        )}
      </section>

      {settings.isLoading && <div className="loading-panel">正在读取运行设置…</div>}
      {settings.error && (
        <div className="inline-error" role="alert">
          {settings.error instanceof ApiError ? settings.error.message : "无法读取运行设置。"}
        </div>
      )}
      {form && (
        <form
          className="settings-form"
          onSubmit={(event) => {
            event.preventDefault();
            save.mutate({
              inbox_dir: form.inbox_dir,
              library_dir: form.library_dir,
              work_dir: form.work_dir,
              scan_interval_seconds: form.scan_interval_seconds,
              automatic_scan_enabled: form.automatic_scan_enabled,
              processors: form.processors,
            });
          }}
        >
          <section className="settings-section">
            <div className="section-heading">
              <div><span>01</span><h2>运行目录</h2></div>
              <p>四个位置必须互不包含；数据库位置只能在启动时配置。</p>
            </div>
            <div className="path-fields">
              <label>
                <span>待入库目录</span>
                <input value={form.inbox_dir} onChange={(event) => setForm({ ...form, inbox_dir: event.target.value })} />
              </label>
              <label>
                <span>正式文件目录</span>
                <input value={form.library_dir} onChange={(event) => setForm({ ...form, library_dir: event.target.value })} />
              </label>
              <label>
                <span>工作目录</span>
                <input value={form.work_dir} onChange={(event) => setForm({ ...form, work_dir: event.target.value })} />
              </label>
              <label>
                <span>数据库文件</span>
                <input value={form.database_path} readOnly />
              </label>
            </div>
          </section>

          <section className="settings-section">
            <div className="section-heading">
              <div><span>02</span><h2>扫描策略</h2></div>
              <p>手动刷新始终可用；自动扫描按下面的间隔运行。</p>
            </div>
            <div className="setting-row">
              <label className="switch-field">
                <input
                  type="checkbox"
                  checked={form.automatic_scan_enabled}
                  onChange={(event) => setForm({ ...form, automatic_scan_enabled: event.target.checked })}
                />
                <span>启用自动扫描</span>
              </label>
              <label>
                <span>扫描间隔（秒）</span>
                <input
                  type="number"
                  min="2"
                  max="3600"
                  value={form.scan_interval_seconds}
                  onChange={(event) => setForm({ ...form, scan_interval_seconds: Number(event.target.value) })}
                />
              </label>
            </div>
          </section>

          <section className="settings-section">
            <div className="section-heading">
              <div><span>03</span><h2>前置处理器</h2></div>
              <p>处理器来自后端注册表；以后新增能力会自动出现在这里。</p>
            </div>
            <div className="processor-list">
              {form.processors.map((processor) => (
                <article key={processor.processor_name} className="processor-row">
                  <div>
                    <strong>{processor.processor_name}</strong>
                    <small>REGISTERED PROCESSOR</small>
                  </div>
                  <label className="switch-field">
                    <input
                      type="checkbox"
                      checked={processor.enabled}
                      onChange={(event) => updateProcessor(processor.processor_name, { enabled: event.target.checked })}
                    />
                    <span>启用</span>
                  </label>
                  <label className="switch-field">
                    <input
                      type="checkbox"
                      checked={processor.auto_run}
                      onChange={(event) => updateProcessor(processor.processor_name, { auto_run: event.target.checked })}
                    />
                    <span>自动运行</span>
                  </label>
                </article>
              ))}
            </div>
          </section>

          {busyMessage && <p className="command-error settings-error" role="alert">{busyMessage}</p>}
          {save.isSuccess && !busyMessage && <p className="save-confirmation" role="status">设置已保存</p>}
          <footer className="settings-actions">
            <button type="submit" className="primary-button" disabled={save.isPending}>
              {save.isPending ? "正在保存…" : "保存设置"}
            </button>
          </footer>
        </form>
      )}
    </section>
  );
}
