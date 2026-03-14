# QUALITY BASELINE

## 1. 目标

本文档定义 TraceFold 在 Phase 1 / Step 2 阶段的**最小工程质量检查基线**。

当前目标不是一次性引入完整的 CI/CD、静态分析和多工具链，而是先建立：

- 最低可重复执行的检查命令
- 每次改动后的最小验证入口
- 文档与代码同步的最小要求
- 后续让 Codex / 其他模型“改完必须通过”的最低门槛

这是一套 **P0 最小质量护栏**，重点是可执行、可重复、不过度复杂化。

---

## 2. 当前阶段包含什么

当前阶段的质量基线只要求：

1. 能安装当前 API 依赖
2. 能运行最小测试命令
3. 能验证系统级接口与服务启动未被打坏
4. 业务域如尚未实现，可先以 smoke test 占位并明确跳过
5. 文档中的本地命令必须与真实命令一致

---

## 3. 当前阶段不包含什么

当前阶段明确**不要求**：

- GitHub Actions
- pre-commit
- Ruff / Black / Mypy / Coverage 全家桶
- 复杂测试矩阵
- 独立 test DB 平台
- 自动化发布流水线

这些能力后续可以接入，但**不属于当前 Step 2 的最低交付线**。

---

## 4. 最低必跑命令

在 `apps/api/` 目录下，当前最低质量门槛如下。

### 4.1 安装依赖

```powershell
python -m pip install -r requirements.txt
```

### 4.2 运行全部当前测试

```powershell
python -m pytest
```

### 4.3 仅运行系统 smoke tests

```powershell
python -m pytest tests/test_system.py
```

---

## 5. 什么时候必须执行这些命令

以下情况至少必须重新执行一次：

### 必跑 `python -m pytest`

适用于：

- 修改 `app/main.py`
- 修改 `app/api/router.py`
- 修改 `app/api/system.py`
- 修改 `app/core/`
- 修改 `app/db/`
- 修改 `app/domains/system_tasks/`
- 修改已存在的测试文件

### 至少必跑 `python -m pytest tests/test_system.py`

适用于：

- 系统级接口调整
- 应用启动流程调整
- 配置、异常、日志底座调整
- 不涉及现有业务域接口的纯底层修改

---

## 6. 当前测试基线口径

当前阶段允许出现：

- system smoke tests 通过
- capture smoke test 先占位并跳过

因此，在当前仓库状态下，预期结果应类似：

```text
2 passed, 1 skipped
```

这表示：

- 系统基础可验证
- 测试入口已经固定
- 第一个业务域测试位置已预留

这不代表业务功能已完整，只代表**质量护栏入口已存在**。

---

## 7. 文档同步要求

每次涉及运行方式、测试方式、初始化方式的改动，必须同步检查：

- `apps/api/TESTING.md`
- `docs/ENV_CONVENTIONS.md`
- `docs/DATA_MODEL_DRAFT.md`（如有结构变化）
- 当前任务说明 / 验收记录

不允许出现：

- 代码已经变了，测试命令还是旧的
- 依赖已经新增，文档没写安装方式
- 表结构变了，数据模型文档没更新

---

## 8. 当前阶段的最低通过标准

在 Step 2 当前阶段，可判定“基础工程质量护栏已存在”的标准为：

- [ ] `apps/api/tests/` 已建立
- [ ] `pytest.ini` 已建立
- [ ] `python -m pytest` 可执行
- [ ] `tests/test_system.py` 可通过
- [ ] 至少一个业务域已有 smoke test 文件（允许当前先 skip）
- [ ] `TESTING.md` 已写入真实可用命令

---

## 9. 当前结论

TraceFold 在 Step 2 当前阶段采用的质量基线为：

**依赖安装 + pytest 入口固定 + system smoke tests + 文档同步要求**

这满足当前 Phase 1 对“每一步都必须可验证”的要求，
同时避免了在底座尚未稳定时提前引入重型工程体系。
