# QTS Web 前端

QTS 量化交易系统的 Web 前端，提供策略回测、AI 决策、任务管理、行情图表、订单持仓与系统设置等完整界面。

- 技术栈：Vue 3（`<script setup>`）+ Vite 5 + Vue Router 4 + Pinia + Element Plus + ECharts
- 语言：JavaScript（不使用 TypeScript）
- 视觉风格：深色量化终端（深蓝黑背景、青/绿/红数据色、等宽数字）

## 环境要求

- Node.js ≥ 18（当前开发使用 Node 24）
- npm ≥ 9（当前开发使用 npm 11）
- 后端 QTS API 默认运行在 `http://localhost:5088`

## 安装依赖

```powershell
cd web
npm install
```

> 说明：如果本机 npm 默认缓存目录不可写，可使用项目内缓存：
>
> ```powershell
> npm install --cache .npm-cache
> ```
>
> `.npm-cache` 为本地缓存目录，可随时删除。

## 本地开发

```powershell
cd web
npm run dev
```

- 开发服务：<http://localhost:5173>
- Vite 代理：`/api` → `http://localhost:5088`（见 `vite.config.js`）
- 开发时需要先启动后端 QTS API；未启动时页面会展示接口错误提示，登录接口也不可用。

## 生产构建

```powershell
cd web
npm run build
```

构建产物输出到 `../QTS/wwwroot`（`build.outDir: '../QTS/wwwroot'`，构建前会清空该目录），包含：

- `QTS/wwwroot/index.html`
- `QTS/wwwroot/assets/*.js`、`assets/*.css` 等静态资源
- `QTS/wwwroot/favicon.svg`

构建使用 `base: './'`，静态资源为相对路径，部署在任意子路径下均可访问（由后端根路径直接托管）。构建过程只依赖本地 `node_modules`，不需要后端在运行。

### 受限环境构建兼容

`package.json` 的 `build` 脚本是 `node --import ./scripts/register-loader.mjs scripts/build.mjs`：

- 正常环境：`scripts/register-loader.mjs` 探测到可用的子进程管道，直接调用 Vite 官方构建（esbuild 压缩）；
- 受限环境（如禁止子进程管道的沙箱，esbuild 会 `spawn EPERM`）：自动注册 `scripts/esbuild-shim-loader.mjs`，把 Vite 内部对 `esbuild` 的 `transform` 调用替换为纯 JS 的实现（仅覆盖 Vite 构建用到的 define 替换），并改用 `terser` 做 JS 压缩、关闭 CSS 压缩后完成构建。两条路径产出的都是可部署的静态资源。

如构建脚本报错，可先 `node --version` 确认 Node ≥ 18，并删除 `node_modules` 后重新安装依赖。

### 后端托管注意事项

前端路由使用 History 模式（`createWebHistory`），后端需要配置 SPA 回退：所有非 `/api`、非静态文件的路径都回退到 `index.html`，否则直接访问 `/market`、`/backtest` 等地址会 404。示例（ASP.NET Core）：

```csharp
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");
```

## 页面与路由

| 路由 | 页面 | 说明 |
| --- | --- | --- |
| `/login` | 登录 | 账号密码登录，默认账号 `admin / admin123` |
| `/` | 仪表盘 | 纸面资产、目标进度、权益曲线、实盘账户、持仓、运行任务、最近订单；30s 自动刷新 |
| `/market` | 行情中心 | 交易对/周期切换、行情快照、K 线（MA5/MA20 + 成交量）、同步历史数据 |
| `/strategies` | 策略库 | 后端注册策略卡片、参数摘要、去回测/创建任务 |
| `/ai` | AI 决策 | AI 走势分析、策略方案（置信度/参数/风险/理由/警告）、一键回测、创建模拟任务；支持本地引擎与大模型 |
| `/backtest` | 策略回测 | 回测参数（含杠杆）表单、收益/回撤/月度收益图表、交易明细（方向）、历史回测管理 |
| `/trade` | 交易台 | 模拟盘手动多空下单（保证金/杠杆/滑点/止损止盈）、持仓管理、策略任务快捷启停；8s 自动刷新 |
| `/tasks` | 任务中心 | 策略任务 CRUD（含杠杆/滑点）、启停、详情抽屉、10s 刷新 |
| `/orders` | 订单与持仓 | 订单流水筛选分页、当前持仓卡片、15s 刷新 |
| `/settings` | 系统设置 | OKX API 配置与连接测试、纸面账户重置、系统状态、安全提示 |

## 目录结构

```
web/
├── index.html
├── README.md
├── vite.config.js
├── package.json
├── scripts/                    # 构建入口（受限环境 esbuild 兼容层）
│   ├── build.mjs
│   ├── register-loader.mjs
│   ├── esbuild-shim-loader.mjs
│   └── esbuild-shim.mjs
├── public/
│   └── favicon.svg
└── src/
    ├── main.js                 # 应用入口：Element Plus / 图标 / 深色主题 / 未授权处理
    ├── App.vue
    ├── api/index.js            # axios 实例、信封拆解、401 处理、全部 API 方法
    ├── stores/                 # Pinia（认证会话）
    ├── router/index.js         # 路由与登录守卫
    ├── layouts/MainLayout.vue  # 侧边栏 + 顶栏 + 内容区
    ├── components/             # 通用组件（统计卡、图表、空状态等）
    ├── views/                  # 10 个页面视图（登录/仪表盘/行情/策略/AI 决策/回测/交易台/任务/订单/设置）
    ├── utils/                  # 格式化与枚举常量
    └── styles/main.css         # 设计令牌与 Element Plus 深色覆盖
```

## API 约定（摘要）

完整契约见 `docs/API_CONTRACT.md`。前端封装要点：

- 所有请求 `/api` 前缀；除登录、健康检查外自动携带 `Authorization: Bearer <token>`
- 统一响应信封 `{ success, data, message, code }`，前端自动拆出 `data`；失败时抛出带后端 `message` 的错误
- 分页数据兼容 `{ items, total, page, pageSize }` 与裸数组两种形态（`normalizePaged`）
- 401 时清除本地会话并跳转登录页；登录后按 `redirect` 参数返回原页面
- AI 相关接口：`GET /api/ai/status` 返回当前引擎（本地/大模型）信息，`POST /api/ai/analyze` 返回走势指标与策略建议；AI 页面的一键回测、创建任务复用回测与任务接口
- 会话 token 与用户信息保存在 `localStorage`（`qts_token` / `qts_user`）

## 构建说明

- `build.outDir` 指向 `../QTS/wwwroot`，`emptyOutDir: true`，构建前会清空后端静态目录
- ECharts 单独分包（`manualChunks`），各页面路由按需拆分为独立 chunk
- 正常环境使用 Vite 官方 esbuild 构建；受限环境自动降级为 `scripts/` 下的兼容构建（见上文“受限环境构建兼容”）
- 构建失败时请先检查 Node 版本与 `node_modules` 是否完整安装
