# QTS Frontend Build Spec

Deliverable lives in `web/` at the repository root. Read `docs/API_CONTRACT.md` first — it is binding.
Only write inside `web/` (plus `web/package.json` etc.). Do not edit backend files or `docs/`.

## Stack
- Vue 3 (Composition API, `<script setup>`), JavaScript only (no TypeScript).
- Vite, Vue Router, Pinia, Axios, Element Plus, ECharts (`echarts` direct, no wrapper required).
- npm. Build output must go to `../QTS/wwwroot` via vite config (`emptyOutDir: true`).
- Dev server port **5173**, proxy `/api` → `http://localhost:5088`.
- No external font/CDN dependency (network may be limited). Use system fonts.

## Brand & visual direction
Name: **QTS · 量化交易系统**. Subtitle: “OKX 策略回测与自动交易终端”.
Dark professional quant terminal:
- Background `#070b14`, panels `#0d1424` with 1px `rgba(148,163,184,.12)` borders, radius 14px.
- Accent cyan `#22d3ee`, success green `#34d399`, danger red `#fb7185`, warning amber `#fbbf24`.
- Typography: Chinese system sans; numbers use tabular numerals and a mono fallback.
- Sidebar 232px fixed on desktop with logo, nav icons (Element Plus icons) and bottom risk notice; collapsible/mobile drawer.
- Top bar: page title, breadcrumb-ish tagline, server status pill (green dot), user dropdown (修改密码/退出).
- Cards have subtle inner glow and hover lift; do not overdo gradients. Must look like a real product, not a template.
- Use a custom Element Plus dark theme via CSS vars; import element-plus dark CSS.
- All user-facing copy in Simplified Chinese. Numbers/percentages formatted with fixed decimals and comma separators.
- Empty/loading/error states everywhere. Skeleton or spinner while loading.
- Responsive minimum: 1366×768 and 1920×1080 look great; 768px collapses sidebar.

## Auth
- Login page at `/login`: split layout — left brand panel with animated concentric radar/chart background and
  bullet points, right login card. Show default account hint `admin / admin123`. Handle error message.
- Token in `localStorage.qts_token`, user object optionally. Axios request interceptor adds Bearer; response
  interceptor unwraps envelope and on 401 clears session and redirects to `/login`. Router guard.
- Registration is open at `/register`; login page exposes a register link and a “saved accounts” quick-switch list.
- Saved accounts use `localStorage.qts_accounts_v1` (max 8, token+user per account). Switching revalidates the
  saved token through `/auth/me`; logout only clears the current session and keeps saved accounts. All accounts
  share the same OKX API account and trading data.

## Pages / routes
Public: `/login`, `/register`; authenticated layout children:
1. `/` Dashboard
2. `/market` 行情
3. `/strategies` 策略库
4. `/backtest` 策略回测 (list + creator + result detail on same page; `/backtest/:id` optional)
5. `/tasks` 交易任务
6. `/orders` 订单与持仓 (tabs)
7. `/settings` 系统设置
8. `/trade` 交易台（手动交易 + 策略任务联动）
9. `/ai` AI 决策（走势分析 + 策略生成 + 一键回测/任务）
10. `/profile` 个人中心（资料/改密/已保存账号/会话安全）

### 1) Dashboard
- Hero row: “账户总览”. Four stat cards: OKX API 总权益 (or 纸面总资产 fallback when no API account),
  可用 USDT, 浮动/累计盈亏, 运行中任务. Small trend sparkline or icon per card.
- When OKX API account is connected: target panel is replaced by an “OKX API 账户” card (equity, available,
  浮动盈亏, running tasks) and the equity-curve panel shows a “实盘权益曲线采样中” placeholder instead of
  the internal paper ledger curve.
- When no API account: legacy `100 U → 10,000 U` progress panel + paper ledger curve remain available.
- Right column: live OKX account card (未配置 state with link to settings; connected shows equity), open positions
  compact list, running tasks table (name/symbol/status/PnL/actions link).
- Bottom: recent orders table.
- Quick actions: 新建回测 / 新建任务 (links).
- Auto-refresh every 30s, stop timer on unmount.

### 2) Market
- Symbol pills/select: BTC-USDT, ETH-USDT, SOL-USDT, XRP-USDT, DOGE-USDT, BNB-USDT. Interval segmented control 1m/5m/15m/1H/4H/1D.
- Ticker header: last price big, 24h change badge, high/low/volume stats.
- ECharts candlestick + volume subchart + MA5/MA20. Data from `/market/candles?limit=300`.
- “同步全部品种” button posts `/market/sync-all` with the current bar and days 30, loading state, success toast
  containing instrument count, total candles and source. One click refreshes all six instruments.
- A small “数据源” tag shows local/OKX/synthetic warning if returned message.

### 3) Strategies
- Grid of strategy cards from `/strategies`: icon/category color, name, description, parameter chips, default params.
- Primary action “去回测” routes to `/backtest?strategy=code`; secondary “创建任务” routes `/tasks?strategy=code`.
- Top explainer strip: 策略只提供信号，风控与仓位由系统执行.

### 4) Backtest
- Two-column: left sticky form card (width ~380px), right results.
- Form: 名称, 策略 select, 交易对, 周期, 时间范围 (Element date-picker daterange), 初始资金 (default 100),
  手续费率 (0.1), 滑点 (0.05), 仓位比例 (95), 杠杆 (1–20×, default 1), 止损/止盈 (0 = 不启用). Dynamic strategy parameter fields from
  `paramsSchema`; selecting strategy resets params to defaults and shows description.
- Buttons: 开始回测 (primary) + 重置. Disable while running; show “正在拉取行情并计算…” progress. On success
  toast + load detail.
- Right results:
  - If no selected result: history table from `/backtests` with “查看详情”. Empty state with a call to run.
  - Metrics cards: 总收益, 最大回撤, 夏普比率, 胜率, 交易次数, 盈亏比 / 手续费. Color positive/negative.
  - Warning banner if `dataSource === "synthetic"`: “欧易当前不可达，本次使用示例行情数据，仅用于功能演示”.
  - ECharts equity curve + drawdown filled curve (same chart with dual y-axis or two charts).
  - Monthly returns bar chart.
  - Trades table paginated client-side.
  - Delete run button with confirm.
- On mount read `?strategy=` to preselect. After run, refresh history and select new id (route query id optional).

### 5) Tasks
- Toolbar: 新建任务, status filter, refresh. Summary chips: 运行中 x / 总计 y.
- Table: 名称/策略, 交易对·周期, 模式 tag (API 实盘), 状态 tag with pulsing dot for Running, 分配资金,
  总盈亏 (colored), 最新信号 (LONG/FLAT/SHORT chips), 最后运行时间, 操作 (详情/平仓/启动/停止/编辑/删除).
- Create/edit dialog (width 680): form same as backtest plus 分配资金, 杠杆 (1–20×), 滑点 (%); risk subgroup
  止损% / 止盈% / 最大仓位%; startImmediately switch for new tasks. No Paper mode option; always shows
  “实盘 · OKX API 账户”. 实盘警告必须说明 “1× 多单走现货，杠杆 >1 或做空走 USDT 永续（逐仓）”，并要求勾选风险确认。
  Dynamic strategy params.
- “平仓” only visible when `positionQty > 0`, calls `POST /api/tasks/{id}/close-position`; Live submits
  `reduceOnly` market order.
- Start/stop/delete confirmations, optimistic refresh afterwards, poll every 10s while page visible.
- Detail drawer optional: position card + last error + recent orders filtered by taskId.

### 6) Orders & Positions
- Tabs: 当前持仓, 订单记录.
- Positions cards/table: symbol, mode, quantity, avg entry, last, unrealized PnL colored, SL/TP, opened time.
- Orders table: time, task, symbol, mode, side Buy/Sell tag, type, price, qty, fee, status, okxOrderId/error.
- Filters (mode, status) and pagination.

### 7) Settings
- Section “欧易 API 接入”: base URL (may show local TLS proxy), API Key, Secret Key, Passphrase (password inputs;
  masked hint if already saved), “OKX 模拟环境” switch (demo account; turn off for real account),
  “允许实盘交易” switch. Save button; “测试连接” button with result.
  If both switches are on, show a warning that requests still carry `x-simulated-trading:1` and will NOT use real
  money; turning real trading on must not silently disable the demo flag.
- Section “AI 决策引擎”: 启用大模型增强 switch, 模型服务下拉 (OpenAI/DeepSeek/Kimi/通义/智谱/自定义),
  Base URL, 模型名称 (filterable + allow-create), API Key (encrypted, masked), 请求超时, 保存/刷新状态.
- Section “K 线配色”: 绿涨红跌 / 红涨绿跌 preset buttons, 上涨/下跌 color pickers, 保存 / 恢复默认.
  Colors are per-browser (localStorage) and must apply instantly to Market, Trade, AI and Backtest charts.
- The internal paper account reset card is removed; only OKX API account and AI/K-line settings remain.
- Section “系统状态”: version, MySQL status/version, K-line count, backtests count, OKX reachability, server time;
  “刷新” button.
- Section “安全提示”: not financial advice, API key permissions should be trade-only/no withdrawal, use IP whitelist.

### 8) Trade desk (交易台)
- Header: “交易台 / OKX API · 模拟环境 or 实盘账户” badge + refresh + “新建策略任务”. Five account cards from
  `trade/overview.account`: 账户总权益, 可用保证金, 占用保证金, 浮动盈亏, 已实现盈亏 (with API account/environment,
  占用、持仓市值、未平仓、手动平仓累计 subtext). PnL numbers use the same up/down colors as the K-line palette.
- Left column: 当前持仓 table (manual positions closable, task positions read-only; symbol, direction, qty, entry,
  last, PnL, margin, leverage, SL/TP, close action); 策略任务 list with status/PnL and link to tasks.
- Left column top: selected-instrument K-line card with 15分/1时/4时/1日 interval switch, MA5/MA20, volume,
  refresh button and 30s polling. Uses `/market/candles?instId=&bar=&limit=300`; switching the order symbol
  reloads the chart synchronously. Initially shows the latest 60 bars; after the user zooms, refreshes keep the
  zoomed bar count and stay anchored to the newest bar instead of resetting to the full series. Candle colors follow
  the Settings → K 线配色 palette (default green-up / red-down).
- Right column: 手动下单 panel — ticker card, inst select, direction Long/Short segmented, margin input + 25%/50%/
  全部 quick buttons, leverage slider 1–20, slippage, optional stop-loss/take-profit, estimated notional/quantity,
  liquidation reference (only meaningful for leverage > 1), submit button, paper-only disclaimer.
- All account numbers update via `/api/trade/overview` every 8s; open/close call `/api/trade/open|close`;
  K-line refreshes every 30s and is not part of the account polling loop.
- Risk copy: manual trading is paper-only and does not send real orders; strategy positions must be managed from tasks.

### 9) AI decision (AI 决策)
- Header: “AI 决策” + engine badge (`/api/ai/status`; local engine / LLM, failure falls back to local badge) + “刷新引擎状态”.
- Control card: instrument select, bar 1H/4H/1D (default 1D), risk profile 保守/平衡/激进, lookback days 30–365,
  allow-short switch, primary “开始 AI 分析” with loading; explanatory hint that analysis is research-only.
- Left column: selected-instrument K-line card (MA5/MA20/volume, 200 candles, synthetic warning), indicator grid
  (trend direction/score/strength, EMA20/60, RSI14, MACD, ATR%, volume ratio, support/resistance with distance),
  summary text “K 线 N 根 · 分析于…”。
- Right column AI plan card: strategy name/code, direction tag, confidence progress, leverage/slippage/stop/take/
  max-position/capital% and estimated USDT, Chinese parameter chips, summary, rationale list, warnings and risk copy;
  bottom actions “一键回测 / 创建 API 任务 / 重新分析”.
- One-click backtest posts `/api/backtests` with the generated plan (365d, 100 USDT, fee/slippage/position/leverage/
  SL/TP/params), shows inline metrics and can link to the backtest page; task creation uses Live API mode with
  `allocatedCapital = account equity × capitalPct` and `startImmediately=true`, then navigates to `/tasks`.
- Never hide synthetic data warnings; AI output is not investment advice.

### 10) Profile (个人中心)
- Header: “个人中心” + role tag; account hero card with initial avatar, display name, @username, email, created-at,
  last-login and online status.
- Edit card: read-only username, display name (required, 1–32), optional email; save via `PUT /api/auth/profile`.
- Security card: old/new/confirm password, submit via `/api/auth/change-password`, success logs out for re-login.
- Saved accounts: current account highlighted; other saved accounts show switch and remove; “添加账号” goes to
  `/login?add=1`; copy explains switching is local multi-session and all accounts share the same OKX API account.
- Session card: account id, Bearer HS256, localStorage storage, logout/switch actions.
- Registration page `/register`: dark split layout, username/display name/optional email/password/confirm, inline
  rules, register auto-sign-in, link back to login; `?add=1` returns to the previous login flow.

## Shared code requirements
- `web/src/api/index.js`: axios client, envelope unwrapping, named API modules.
- `web/src/stores/auth.js`: login/register/logout/fetchMe/updateProfile/switchAccount and persistence.
- Components: `StatCard.vue`, `PageHeader.vue` optional, `EmptyState.vue`, `EquityChart.vue`, `KlineChart.vue` maybe.
- `web/src/utils/format.js`: money, pct, number, datetime, duration, pnl class.
- ECharts: import only needed modules via `echarts/core` to keep bundle sane, or full `echarts` acceptable.
  Charts must resize on window resize and dispose on unmount.
- Global `web/src/styles/main.css`: theme tokens, Element Plus overrides, utilities, scrollbars, animations.
- `npm run build` must exit 0. Also run `npx vite build` as final check.
- `web/README.md` with dev/build commands.

## Acceptance
- `npm install` + `npm run build` succeed offline-friendly (registry is reachable, no CDN at runtime).
- No uncaught console errors on any route with mocked/unavailable backend (show empty states gracefully).
- All API calls exactly match `docs/API_CONTRACT.md`.
- Login, dashboard, backtest form/result, task CRUD dialogs, settings forms are fully wired (no dead buttons).
- The built `index.html` and assets exist under `QTS/wwwroot/`.
