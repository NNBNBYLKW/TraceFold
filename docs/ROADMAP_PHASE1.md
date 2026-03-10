# 第一阶段路线图（ROADMAP PHASE 1）

> 本文档定义项目第一阶段的目标、边界、里程碑与实施顺序。
> 第一阶段重点是**打基础平台**，不是追求功能炫酷或一次做全。

---

## 1. 第一阶段总目标

构建一个可长期扩展的本地个人数据平台基础版，具备以下能力：

- 以 SQLite 为唯一真相源
- 以后端 API 为核心
- 支持 Web 工作台
- 支持 Electron 桌面壳
- 支持 Telegram 作为输入入口之一
- 建立统一的 Capture -> Parse -> Pending -> Confirm 主链
- 建立 AI 派生层，但不污染原始数据
- 建立健康模块“硬指标规则 / 主观记录 AI”双通道基础结构

---

## 2. 第一阶段不追求的目标

以下内容明确不在第一阶段范围内：

- Obsidian 深整合
- 多用户与权限系统
- 云同步
- 自动知识关系连边
- 大规模全局图谱
- 模板脚本引擎
- 重型预算引擎
- 本地模型编排平台
- 医疗诊断式 AI
- 完整移动端 App

---

## 3. 第一阶段交付结果

第一阶段结束时，应至少交付：

### 3.1 后端
- FastAPI 服务可运行
- SQLite 模型与迁移可运行
- Capture / Pending / Expense / Knowledge / Health / Templates / AI Views 基础 API 可用
- 基础测试可运行

### 3.2 Web 前端
- Dashboard 页面
- Capture Inbox 页面
- Pending Queue 页面
- Expense 基础页面
- Knowledge 基础页面
- Health 基础页面
- Templates 页面
- 手机端简化查看页

### 3.3 桌面壳
- Electron 可启动
- 托盘常驻
- 全局快捷键显示/隐藏
- 能加载工作台地址
- 基础通知

### 3.4 Telegram
- 作为 API 客户端接入
- 支持文本录入与基础队列操作

### 3.5 AI 派生层
- 至少支持：
  - Dashboard 文本总结
  - Knowledge 摘要
  - Health 主观记录分析

---

## 4. 第一阶段实施原则

### 4.1 先打地基，再做亮点
本阶段优先顺序：
1. 服务化
2. 数据模型
3. Capture / Pending 主链
4. Web 工作台基础页
5. 桌面壳
6. Bot 接入
7. AI 派生层

### 4.2 不做双轨主架构
旧 Tkinter 架构不再作为主线延续。
如需参考旧逻辑，只能作为业务参考，不作为正式运行基础。

### 4.3 每完成一个模块，必须满足可验收
不接受“概念上实现了但系统跑不通”。

---

## 5. 项目结构目标

第一阶段目标结构：

```text
personal-data-platform/
├─ apps/
│  ├─ api/
│  ├─ worker/
│  ├─ bot/
│  ├─ web/
│  └─ desktop/
├─ packages/
│  ├─ core/
│  ├─ db/
│  ├─ services/
│  ├─ parser/
│  ├─ ai/
│  ├─ rules/
│  ├─ reports/
│  ├─ exports/
│  └─ integrations/
├─ data/
├─ configs/
├─ docs/
└─ scripts/