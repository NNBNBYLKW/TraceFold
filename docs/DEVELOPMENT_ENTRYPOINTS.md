# 开发入口

本页按当前组件选择入口。普通媒体整理使用 [用户指南](getting-started.md)；源码开发从 [开发导航](development/README.md) 选择任务。

## 原生媒体桌面

- [开发起步](development/getting-started.md)：SDK、依赖恢复、Release 构建与 Debug 启动。
- [测试与验证](development/testing.md)：.NET 与 PowerShell runner、受影响接缝及证据分层。
- [本地发布](development/releasing.md)：Stage 5C-C 的输入、参数、P/K 交付物及独立 smoke。
- [工作区布局](development/workspace-layout.md)：源码、持久证据、缓存和构建产物。
- [环境与配置](ENV_CONVENTIONS.md)：真实环境变量、原生宿主与外层 TEMP/TMP 包装。
- [桌面源码说明](../apps/media-desktop/README.md)。

原生媒体桌面的日常启动不要求同时运行下方共享 API/Web。

## API / Web 独立开发上下文

下列流程适用于 `apps/api` 与 `apps/web` 的共享 API 演示环境，使用独立 SQLite 演示库。组件契约与深入说明：

1. [API 迁移](API_MIGRATION_BASELINE.md)
2. [API 演示数据](API_DEMO_SEED_BASELINE.md)
3. [API seeded smoke](API_SEEDED_INTEGRATION_SMOKE.md)
4. [Web API 消费](WEB_CONSUMPTION_BASELINE.md)
5. [Web 信息结构合同](WEB_INFORMATION_HIERARCHY_CONTRACT.md)
6. [API 架构规则](ARCHITECTURE_RULES.md)

以下命令是该独立上下文的当前入口；不得把演示库当作个人正式数据库。配置 owner、唯一读取入口和敏感性遵守 [ENV_CONVENTIONS](ENV_CONVENTIONS.md)。

## Standard Fresh Demo DB Flow

Recommended order:

1. Choose a fresh SQLite file
2. Run migrations to `head`
3. Run demo seed
4. Point API at that DB
5. Start API
6. Check `/api/healthz`
7. Check `/api/system/status`
8. Point Web at the API
9. Start Web
10. Run the minimum smoke reads

Do not use an unstamped or legacy local DB for this flow.

## Standard Commands

Run from the repo root unless noted otherwise.

### 1. Migrate Fresh Demo DB

```powershell
python apps/api/scripts/run_migrations.py upgrade head --db-url sqlite:///./data/tracefold_demo.db
```

### 2. Seed Fresh Demo DB

```powershell
python apps/api/scripts/seed_demo_data.py --db-url sqlite:///./data/tracefold_demo.db --with-derivations
```

If you intentionally want to recreate an existing prior demo DB:

```powershell
python apps/api/scripts/seed_demo_data.py --db-url sqlite:///./data/tracefold_demo.db --force --with-derivations
```

### 3. Point API At The Demo DB

In `apps/api/.env`:

```env
TRACEFOLD_API_DB_URL=sqlite:///../../data/tracefold_demo.db
TRACEFOLD_API_HOST=127.0.0.1
TRACEFOLD_API_PORT=8000
```

Start API:

```powershell
cd apps/api
uvicorn app.main:app --host 127.0.0.1 --port 8000
```

The API accepts only loopback `Host` values (`127.0.0.1`, `localhost`, and `[::1]`). Browser requests from other origins are rejected before they reach API handlers; local frontends on the documented development ports remain allowed. Keep the listener on loopback. A network-facing deployment requires separate authentication and an explicit host policy.

### 4. Point Web At The Shared API

In `apps/web/.env`:

```env
VITE_API_BASE_URL=http://127.0.0.1:8000
```

Start Web:

```powershell
cd apps/web
npm run dev
```

## Minimum Verification Path

Check these API routes first:

- `GET http://127.0.0.1:8000/api/healthz`
- `GET http://127.0.0.1:8000/api/system/status`
- `GET http://127.0.0.1:8000/api/system/local-operability`
- `GET http://127.0.0.1:8000/api/capture?page=1&page_size=20`
- `GET http://127.0.0.1:8000/api/pending?page=1&page_size=20`
- `GET http://127.0.0.1:8000/api/expense?page=1&page_size=20`
- `GET http://127.0.0.1:8000/api/knowledge?page=1&page_size=20`
- `GET http://127.0.0.1:8000/api/health?page=1&page_size=20`
- `GET http://127.0.0.1:8000/api/alerts?domain=health&status=open`
- `GET http://127.0.0.1:8000/api/tasks?limit=20`
- `GET http://127.0.0.1:8000/api/dashboard`
- `GET http://127.0.0.1:8000/api/workbench/home`
- `POST http://127.0.0.1:8000/api/capture/bulk-intake/preview`
- `POST http://127.0.0.1:8000/api/capture/bulk-intake/import`

Then check these Web routes:

- `http://127.0.0.1:3000/workbench`
- `http://127.0.0.1:3000/quick-capture`
- `http://127.0.0.1:3000/bulk-intake`
- `http://127.0.0.1:3000/capture`
- `http://127.0.0.1:3000/pending`
- `http://127.0.0.1:3000/expense`
- `http://127.0.0.1:3000/knowledge`
- `http://127.0.0.1:3000/health`

Expected baseline:

- API starts cleanly on the seeded DB
- runtime status is readable
- local continuity status is readable
- quick capture route is reachable and can submit plain text into Capture
- bulk intake route is reachable and can preview/import text-file candidates into Capture
- capture and pending list routes are readable
- expenses, knowledge, and health are not empty
- at least one health alert exists
- at least one task run exists
- workbench and dashboard are no longer empty shells

## One-Shot Smoke

For the full fresh-demo recreation path:

```powershell
python apps/api/scripts/smoke_seeded_demo.py --db-url sqlite:///./data/tracefold_demo.db --force
```

This is a development smoke helper, not a production runbook.

## Common First Checks

### API unavailable

Check:

- API process is actually running
- `apps/api/.env`
- `TRACEFOLD_API_DB_URL`
- the DB file path is writable

### `/api/healthz` unreachable

Check:

- host and port are `127.0.0.1:8000`
- another process is not holding the port

### Wrong schema state

Check:

```powershell
python apps/api/scripts/run_migrations.py current --db-url sqlite:///./data/tracefold_demo.db
```

Also verify the API is pointing at the same DB you migrated.

### Seed refused

The seed baseline intentionally refuses:

- non-migrated DBs
- non-demo formal data
- repeated demo seeding without `--force`

Start with a brand-new SQLite file unless you explicitly mean to recreate a prior demo DB.

### Web shows API unavailable

Check:

- `http://127.0.0.1:8000/api/healthz`
- `apps/web/.env`
- `VITE_API_BASE_URL=http://127.0.0.1:8000`

Restart the Web dev server after fixing `.env`.

## Related Docs

- `docs/API_MIGRATION_BASELINE.md`
- `docs/API_DEMO_SEED_BASELINE.md`
- `docs/API_SEEDED_INTEGRATION_SMOKE.md`
- `docs/WEB_CONSUMPTION_BASELINE.md`
- `docs/PHASE2_V1_CLOSURE_BASELINE.md`
- `docs/POST_PHASE1_A3_TASK2_STARTUP_BASELINE.md`
- `docs/POST_PHASE1_A3_TASK4_VALIDATION_BASELINE.md`
