# Codex 任务模板（TASK TEMPLATE）

> 每次给 Codex 下任务时，尽量复制本模板并填完整。
> 目标：减少跑偏，提高一次完成率。

---

## 任务标题
一句话描述任务，例如：
- 实现 captures 模块后端第一版
- 为 health metrics 增加规则引擎和 alerts
- 搭建 Electron 托盘壳最小可运行版本

---

## 1. 任务目标
清楚描述这次任务的最终目标。

示例：
实现 `captures` 模块的后端第一版，包括：
- SQLAlchemy model
- Pydantic schema
- service
- router
- 单元测试

---

## 2. 范围（做什么）
明确这次任务**允许做**的内容。

示例：
- 只处理后端代码
- 只实现 `/api/v1/captures` 的新增、列表、详情
- 允许创建必要的 model/schema/service/router/test 文件
- 允许补充最小数据库初始化依赖

---

## 3. 非范围（不要做什么）
明确这次任务**不允许做**的内容。

示例：
- 不要实现 parser
- 不要改前端页面
- 不要引入 Obsidian
- 不要修改别的模块 schema
- 不要顺手实现 pending 逻辑

---

## 4. 输入上下文
告诉 Codex 当前项目背景与已知条件。

示例：
- 项目使用 FastAPI + SQLAlchemy 2 + SQLite
- SQLite 是唯一真相源
- 所有前端都走 API
- Router 不写业务逻辑
- 业务逻辑写在 Service 层
- Electron 只做桌面壳
- AI 内容与原始数据分离
- 详见 `docs/PROJECT_BRIEF.md` 和 `docs/ARCHITECTURE_RULES.md`

---

## 5. 相关文件
列出 Codex 必须阅读或重点参考的文件。

示例：
- `docs/PROJECT_BRIEF.md`
- `docs/ARCHITECTURE_RULES.md`
- `packages/db/base.py`
- `packages/db/models/__init__.py`
- `apps/api/main.py`

---

## 6. 输出要求
写清楚你希望产出的结果。

示例：
- 新增 `packages/db/models/capture.py`
- 新增 `packages/services/capture_service.py`
- 新增 `apps/api/routers/captures.py`
- 新增对应 schema 和 tests
- 更新 `apps/api/main.py` 注册路由

---

## 7. 实现约束
这些是硬性要求，Codex 必须遵守。

示例：
- 不要在 router 里写业务逻辑
- 不要直接在前端访问数据库
- 不要把 AI 字段塞进原始业务表
- 不要使用假数据替代正式 service
- 尽量保持命名清晰，和已有模块风格一致
- 不要引入重量级新依赖，除非任务明确要求

---

## 8. 数据/接口约束
如涉及表结构、字段或 API，写清楚。

示例：
### captures 表字段
- id
- module_hint
- source_type
- raw_text
- raw_payload_json
- status
- created_at
- updated_at

### API
- `POST /api/v1/captures`
- `GET /api/v1/captures`
- `GET /api/v1/captures/{id}`

---

## 9. 验收标准
写成可检查的清单。

示例：
- [ ] 接口可启动
- [ ] OpenAPI 文档中可见新路由
- [ ] pytest 通过
- [ ] 新增记录后可查到
- [ ] 列表支持基础分页
- [ ] 代码通过 lint / type check（如项目已配置）

---

## 10. 提交格式要求
告诉 Codex你希望它怎么汇报结果。

示例：
请在完成后输出：
1. 修改了哪些文件
2. 每个文件做了什么
3. 还未完成的点
4. 需要我手动确认的地方
5. 本地运行/测试命令

---

## 11. 风险提醒（可选）
如果某任务容易跑偏，可以提前说明。

示例：
- 注意不要把 capture 和 knowledge 混成同一个实体
- 注意 health 主观记录不走规则引擎
- 注意图谱第一版只支持人工关系

---

# 示例任务

## 示例：实现 health metrics + alerts 第一版

### 任务目标
实现健康硬指标的记录与规则提醒，包括：
- `health_metrics` 表
- `health_alerts` 表
- metrics API
- rule engine
- alerts API
- 单元测试

### 范围
- 后端实现
- 规则配置读取
- 数据落库
- alert 查询接口

### 非范围
- 不要实现主观健康记录 AI 分析
- 不要实现前端页面
- 不要接入 Telegram

### 输入上下文
- 硬指标如心率、睡眠时间、血压、体重走规则
- 主观感受交给 AI 派生层，不在本任务中处理
- AI 不得覆盖原始数据

### 验收标准
- [ ] 可新增 health metric
- [ ] 规则命中后生成 alert
- [ ] 可查询 alerts 列表
- [ ] 至少覆盖 3 类指标测试
- [ ] pytest 通过