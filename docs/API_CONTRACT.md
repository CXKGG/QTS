# QTS Quant Trading System — HTTP API Contract (binding)

Backend base URL (dev): `http://localhost:5088`. Frontend dev server proxies `/api` → `http://localhost:5088`.
All endpoints are prefixed with `/api`. Except `GET /api/health`, `POST /api/auth/login` and `POST /api/auth/register`, every endpoint
requires `Authorization: Bearer <token>`.

## Envelope

Success and business errors always use this envelope:

```json
{ "success": true, "data": { }, "message": null }
{ "success": false, "data": null, "message": "友好错误信息", "code": "OPTIONAL_CODE" }
```

HTTP status codes are still meaningful: 200 OK for success (including business `success:false` is normally 400),
400 validation, 401 unauthorized, 404 not found, 409 conflict, 500 server error. The frontend must unwrap `data`
and display `message` on failure.

Paged payloads use:

```json
{ "items": [], "total": 0, "page": 1, "pageSize": 20 }
```

Timestamps are ISO-8601 strings. Chart points use Unix milliseconds in the `time` field to avoid timezone bugs.
Decimals serialized as JSON numbers. All texts are Simplified Chinese where user facing.

## 1. Auth

### POST /api/auth/login
Request: `{ "username": "admin", "password": "admin123" }`
Data: `{ "token": "...", "expiresAt": "2026-11-01T00:00:00Z", "user": { "id": 1, "username": "admin", "displayName": "量化管理员" } }`

### POST /api/auth/register
Anonymous. Request `{ "username": "demo_trader", "password": "Demo123456", "displayName": "演示交易员", "email": "demo@qts.local" }`.
Username 3-32 letters/numbers/underscore/hyphen/Chinese, password 6-64, email optional. Returns the same
`LoginResponse` shape as login (auto sign-in). Duplicate username returns HTTP 409 `USERNAME_TAKEN`.

### GET /api/auth/me
Data: `{ "id": 1, "username": "admin", "displayName": "量化管理员", "email": null, "role": "admin", "createdAt": "...", "lastLoginAt": "..." }`

### PUT /api/auth/profile
Request `{ "displayName": "量化管理员 Pro", "email": "admin@qts.local" }`; data: updated `UserDto`.

### POST /api/auth/change-password
Request `{ "oldPassword": "...", "newPassword": "..." }` → data: `true`.

## 2. Dashboard

### GET /api/dashboard/overview
```json
{
  "paper": {
    "initialBalance": 100, "equity": 118.42, "available": 18.42, "positionValue": 100,
    "usedMargin": 0, "unrealizedPnl": 1.42, "realizedPnl": 17.0, "returnPct": 18.42
  },
  "live": {
    "configured": false, "connected": false, "totalEquity": 0,
    "availableUsdt": 0, "unrealizedPnl": 0, "error": null
  },
  "target": { "initial": 100, "target": 10000, "currentEquity": 118.42, "progressPct": 0.19 },
  "stats": {
    "runningTasks": 1, "totalTasks": 2, "openPositions": 1,
    "todayPnl": 3.2, "totalPnl": 18.42, "todayTrades": 4, "winRate": 62.5
  },
  "equityCurve": [ { "time": 1750000000000, "equity": 100.0 } ],
  "recentOrders": [ { /* OrderDto, newest 8 */ } ],
  "topPositions": [ { /* PositionDto */ } ]
}
```
`live.configured` means API credentials exist, `connected` means last credential test succeeded. `error` is a
short Chinese string or null. If live is not configured, the frontend shows “未配置”.

### GET /api/dashboard/equity-curve?range=7d|30d|90d|all
Data: `[ { "time": 1750000000000, "equity": 100.0 } ]`.

## 3. Market

### GET /api/market/instruments
Data: `[ { "instId": "BTC-USDT", "base": "BTC", "quote": "USDT", "name": "BTC/USDT" } ]`

### GET /api/market/candles?instId=BTC-USDT&bar=1H&limit=300
Data: `[ { "time": 1750000000000, "open": 1, "high": 2, "low": 0.5, "close": 1.5, "volume": 12.5 } ]`
Candles are ascending by time. `bar` is one of `1m, 5m, 15m, 1H, 4H, 1D`.

### GET /api/market/ticker?instId=BTC-USDT
Data: `{ "instId": "BTC-USDT", "last": 1.0, "change24hPct": 1.2, "high24h": 2, "low24h": 0.5, "volume24h": 100, "time": 1750000000000 }`

### POST /api/market/sync
Request `{ "instId": "BTC-USDT", "bar": "1H", "days": 30 }` → data `{ "instId": "...", "bar": "1H", "count": 720, "source": "okx", "message": "..." }`
If OKX is unreachable the server returns business data with `source: "synthetic"` and a warning `message`
(HTTP 200); it never crashes.

### POST /api/market/sync-all
一次同步全部内置交易对（BTC/ETH/SOL/XRP/DOGE/BNB）的历史 K 线。
Request `{ "bar": "1H", "days": 30 }` → data:
```json
{
  "bar": "1H", "days": 30, "instrumentCount": 6, "totalCount": 4326,
  "source": "synthetic",
  "message": "OKX 当前不可达，已为 6 个交易对生成示例行情，共 4326 根 1H K 线（未写入数据库）",
  "items": [{ "instId": "BTC-USDT", "bar": "1H", "count": 721, "source": "synthetic", "message": "..." }]
}
```
当所有品种均从 OKX 成功拉取时 `source` 为 `okx`，否则为 `synthetic`；页面按钮“同步全部品种”调用此接口。

## 4. Strategies

### GET /api/strategies
Data:
```json
[{
  "code": "ma_cross", "name": "双均线趋势", "category": "趋势",
  "description": "快慢均线金叉做多、死叉做空（可关闭），适合趋势行情。",
  "paramsSchema": [
    { "code": "fastPeriod", "label": "快线周期", "type": "int", "default": 7, "min": 2, "max": 100, "step": 1 },
    { "code": "slowPeriod", "label": "慢线周期", "type": "int", "default": 25, "min": 5, "max": 300, "step": 1 },
    { "code": "allowShort", "label": "允许做空", "type": "bool", "default": true,
      "options": [{ "label": "开启", "value": true }, { "label": "关闭", "value": false }] }
  ],
  "defaultParams": { "fastPeriod": 7, "slowPeriod": 25, "allowShort": true }
}]
```
Types: `int`, `decimal`, `bool`, `select`. Select schema has `options: [{ "label": "EMA", "value": "ema" }]`.

## 5. Backtest

### POST /api/backtests
Request:
```json
{
  "name": "BTC 双均线回测", "strategyCode": "ma_cross", "instId": "BTC-USDT", "bar": "1H",
  "startTime": "2026-04-01T00:00:00Z", "endTime": "2026-07-01T00:00:00Z",
  "initialCapital": 100, "params": { "fastPeriod": 7, "slowPeriod": 25 },
  "feeRatePct": 0.1, "slippagePct": 0.05, "positionSizePct": 95, "leverage": 1,
  "stopLossPct": 0, "takeProfitPct": 0, "preferLocalData": false
}
```
`leverage` is 1–20 (isolated margin model; default 1). Strategy signals may be long (1), flat (0) or short (-1);
long/short entry and exit both apply the configured slippage. Data:
`{ "id": 12, "status": "completed", "message": "回测完成", "metrics": { ... } }`.
The request may take a few seconds. If OKX is unreachable and local data is insufficient, the backend automatically
uses deterministic synthetic market data, sets `dataSource: "synthetic"` on the run and shows a warning to the user.
`dataSource` is `"okx"` or `"synthetic"`.

### GET /api/backtests?page=1&pageSize=20&strategyCode=&instId=
Data is a paged object (`PagedResult<BacktestSummaryDto>`):
```json
{
  "items": [{
    "id": 12, "name": "...", "strategyCode": "ma_cross", "strategyName": "双均线趋势",
    "instId": "BTC-USDT", "bar": "1H", "startTime": "...", "endTime": "...",
    "initialCapital": 100, "finalEquity": 146.2, "totalReturnPct": 46.2,
    "maxDrawdownPct": 12.1, "sharpe": 1.4, "winRatePct": 58.3, "tradeCount": 21,
    "leverage": 1, "slippagePct": 0.05, "status": "completed", "dataSource": "synthetic", "createdAt": "..."
  }],
  "total": 1, "page": 1, "pageSize": 20
}
```

### GET /api/backtests/{id}
Data:
```json
{
  "summary": { /* BacktestSummaryDto, plus params, feeRatePct, slippagePct, positionSizePct, leverage, stopLossPct, takeProfitPct, fees, durationMs, error */ },
  "equityCurve": [ { "time": 1750000000000, "equity": 100, "drawdownPct": 0 } ],
  "trades": [{
    "id": 1, "entryTime": "...", "exitTime": "...", "entryPrice": 1, "exitPrice": 2,
    "quantity": 3, "fee": 0.1, "pnl": 2.2, "pnlPct": 1.4, "side": "long", "margin": 95,
    "exitReason": "信号离场", "holdingBars": 8
  } ],
  "monthlyReturns": [ { "month": "2026-05", "returnPct": 3.2 } ],
  "benchmark": { "buyAndHoldReturnPct": 12.0 }
}
```

### DELETE /api/backtests/{id}
Data: `true`.

## 6. Trading tasks

### GET /api/tasks?page=1&pageSize=20&status=
Data paged `TaskDto`:
```json
{
  "id": 3, "name": "BTC 趋势实盘", "strategyCode": "ma_cross", "strategyName": "双均线趋势",
  "instId": "BTC-USDT", "bar": "1H", "mode": "Live", "status": "Running",
  "allocatedCapital": 100, "leverage": 1, "slippagePct": 0.05,
  "params": { "fastPeriod": 7, "slowPeriod": 25, "allowShort": true },
  "risk": { "stopLossPct": 5, "takeProfitPct": 15, "maxPositionPct": 95 },
  "totalPnl": 2.1, "lastSignal": "LONG", "lastRunAt": "...", "lastError": null,
  "positionQty": 0.001, "positionAvgPrice": 60000, "createdAt": "...", "updatedAt": "..."
}
```
Modes: `Live` is the only accepted mode for new tasks. Historical `Paper` tasks remain readable for audit, but
`POST /api/tasks` with `mode: "Paper"` and `POST /api/tasks/{id}/start` on a Paper task are rejected. Statuses:
`Stopped`, `Running`, `Paused`, `Error`.

### POST /api/tasks
Request:
```json
{
  "name": "BTC 趋势", "strategyCode": "ma_cross", "instId": "BTC-USDT", "bar": "1H",
  "mode": "Live", "allocatedCapital": 100, "leverage": 1, "slippagePct": 0.05,
  "params": { "fastPeriod": 7, "slowPeriod": 25, "allowShort": true },
  "risk": { "stopLossPct": 5, "takeProfitPct": 15, "maxPositionPct": 95 },
  "startImmediately": true
}
```
`leverage` is 1–20 (default 1). The task may go long, flat or short based on strategy signals; entries and exits
apply `slippagePct` (0–5, default 0.05). Live mode requires OKX credentials AND live trading enabled in settings.
Live execution routes 1× long to spot (`BTC-USDT`); leverage > 1 or a short target routes to the USDT perpetual swap
(`BTC-USDT-SWAP`) with isolated margin. Contract size is converted from base quantity using the instrument `ctVal`,
`lotSz` and `minSz` returned by OKX. The API account is still the demo/simulated environment until the operator
switches to live; verify keys and environment before claiming real-money readiness. Data: TaskDto.

### POST /api/tasks/{id}/close-position
Data: `true`. Closes the task's newest open position at market: Live positions send a `reduceOnly` market order.
Requires the task to have an open position.

### GET /api/tasks/{id} → TaskDto
### PUT /api/tasks/{id}
Request same editable fields as POST (without startImmediately); only allowed while Stopped/Error. Data: TaskDto.
### POST /api/tasks/{id}/start → TaskDto
### POST /api/tasks/{id}/stop → TaskDto
### DELETE /api/tasks/{id} → true (only after stopped)

## 7. Orders & positions

### GET /api/orders?page=1&pageSize=20&taskId=&mode=&status=
Data paged `OrderDto`:
```json
{
  "id": 1, "taskId": 3, "taskName": "BTC 趋势", "instId": "BTC-USDT", "mode": "Paper",
  "side": "Buy", "orderType": "Market", "price": 60000, "quantity": 0.001,
  "filledQuantity": 0.001, "avgFillPrice": 60000, "fee": 0.06, "leverage": 1, "status": "Filled",
  "okxOrderId": null, "error": null, "reason": "双均线金叉",
  "createdAt": "...", "filledAt": "..."
}
```
`side` is `Buy`/`Sell`; for short positions a `Sell` opens and a `Buy` closes. `taskId=0` means a manual trade.

### GET /api/positions?status=open
Data: `[ { "id": 1, "taskId": 3, "taskName": "...", "instId": "BTC-USDT", "mode": "Paper", "side": "long",
"quantity": 0.001, "avgEntryPrice": 60000, "lastPrice": 61000, "unrealizedPnl": 1, "unrealizedPnlPct": 1.6,
"stopLoss": 57000, "takeProfit": 69000, "margin": 0, "leverage": 1, "openedAt": "..." } ]`
`stopLoss`/`takeProfit` are absolute prices. `margin`/`leverage` describe the isolated margin used by the position.

## 8. Manual trading (OKX API account)

The manual trading desk now reads and trades the **OKX API account** (USDT perpetual, isolated margin). It no
longer uses the internal paper account. Environment depends on the saved OKX config: when `simulatedTrading=true`
the requests carry `x-simulated-trading: 1` and only the OKX demo account is touched.

### GET /api/trade/overview?instId=BTC-USDT
Data:
```json
{
  "account": {
    "configured": true, "connected": true, "simulated": true,
    "accountType": "OKX API 账户", "environment": "模拟环境",
    "totalEquity": 98179.13, "available": 4999.94, "usedMargin": 0,
    "positionValue": 0, "unrealizedPnl": 0, "realizedPnl": 0,
    "currency": "USDT", "error": null
  },
  "paper": { "initialBalance": 100, "equity": 99.6, "available": 99.6, "usedMargin": 0,
             "positionValue": 0, "unrealizedPnl": 0, "realizedPnl": 0, "returnPct": -0.4 },
  "instruments": [ { "instId": "BTC-USDT", "base": "BTC", "quote": "USDT", "name": "BTC/USDT" } ],
  "ticker": { "instId": "BTC-USDT", "last": 70000, "change24hPct": -0.2, "high24h": 71000,
              "low24h": 69000, "volume24h": 22800, "time": 1750000000000 },
  "positions": [ { /* PositionDto, all open Live manual positions; taskId=0 ones are manual */ } ],
  "tasks": [ { /* TaskDto, newest 100 */ } ]
}
```
`paper` is kept for backward compatibility only; the UI uses `account`.

### POST /api/trade/open
Request:
```json
{ "instId": "BTC-USDT", "direction": "short", "margin": 10, "leverage": 5,
  "slippagePct": 0.05, "stopLossPct": 3, "takeProfitPct": 8 }
```
`direction` is `long` or `short`; `margin` 1–1,000,000, `leverage` 1–20, `slippagePct` 0–5,
`stopLossPct` 0–50, `takeProfitPct` 0–200. The order is sent to `BTC-USDT-SWAP` with isolated margin and the
leverage set on OKX before the market order. Requires configured credentials and (for real environment) the live
trading switch. Data: `{ "order": OrderDto, "position": PositionDto, "account": TradeAccountDto, "message": "开仓成功..." }`.

### POST /api/trade/close
Request `{ "positionId": 12, "slippagePct": 0.05 }`. Only manual (`taskId=0`) open Live positions can be closed
here; the order is `reduceOnly` on the OKX swap. Strategy positions are managed by their task. Data: same shape as open.

## 9. AI decision

The AI desk analyzes recent candles, generates a parameterized strategy plan, and can hand the plan to the
existing backtest/task APIs. It is research tooling only; it never places live orders by itself.

Default mode is the built-in offline multi-factor engine (EMA / MACD / RSI / ATR / volume).
A model provider can be selected in **System settings → AI decision engine** (encrypted key in MySQL, no restart
required): `openai`, `deepseek`, `moonshot` (Kimi), `qwen` (通义千问), `zhipu` (GLM) or `custom` (any
OpenAI-compatible `/chat/completions` endpoint). The provider presets fill a sensible Base URL and model list;
both remain editable. `Ai:Enabled`, `Ai:BaseUrl`, `Ai:ApiKey`, `Ai:Model`, `Ai:Provider` in `appsettings.json`
or environment variables can still be used as defaults. If the LLM call fails the service automatically falls
back to the local engine.

### GET /api/ai/status
Data: `{ "mode": "local", "providerType": "local", "provider": "本地量化引擎",
"model": "qts-local-v1", "llmConfigured": false, "message": "未配置大模型 Key..." }`.

### POST /api/ai/analyze
Request:
```json
{ "instId": "BTC-USDT", "bar": "4H", "riskProfile": "balanced",
  "lookbackDays": 180, "allowShort": true }
```
`bar` is `1H` / `4H` / `1D`; `riskProfile` is `conservative` / `balanced` / `aggressive`;
`lookbackDays` 30–365. Data:
```json
{
  "instId": "BTC-USDT", "bar": "4H", "generatedAt": "...",
  "provider": "本地量化引擎", "model": "qts-local-v1",
  "dataSource": "synthetic", "warning": "当前数据为示例行情...",
  "candles": 1080, "lastPrice": 70000, "changePct": -1.2,
  "trend": { "direction": "down", "label": "下降趋势", "score": -62, "strength": 77 },
  "indicators": { "ema20": 71000, "ema60": 72000, "rsi14": 31.2, "macd": -520,
                  "macdSignal": -454, "atrPct": 0.92, "volumeRatio": 1.6,
                  "support": 69000, "resistance": 73000 },
  "suggestion": {
    "strategyCode": "macd_trend", "strategyName": "MACD 趋势",
    "directionBias": "short", "confidence": 82, "leverage": 2, "slippagePct": 0.05,
    "params": { "fastPeriod": 12, "slowPeriod": 26, "signalPeriod": 9,
                "minHoldBars": 2, "allowShort": true },
    "risk": { "stopLossPct": 1.84, "takeProfitPct": 3.68, "maxPositionPct": 35 },
    "capitalPct": 35, "summary": "...", "rationale": ["..."], "warnings": ["..."]
  }
}
```
The plan can be sent unchanged to `POST /api/backtests` (map `params`, `risk`, `leverage`, `slippagePct`)
or `POST /api/tasks` with `mode: "Paper"`. AI-generated plans are not investment advice and are not a promise
of returns.

## 10. Settings

### GET /api/settings/okx
Data:
```json
{
  "baseUrl": "https://www.okx.com", "hasApiKey": false, "apiKeyMasked": "",
  "hasSecretKey": false, "hasPassphrase": false, "simulatedTrading": true,
  "liveTradingEnabled": false, "connected": false, "connectionError": null,
  "savedAt": null, "dataSource": "okx"
}
```

### PUT /api/settings/okx
Request `{ "apiKey": "...", "secretKey": "...", "passphrase": "...", "baseUrl": "https://www.okx.com",
"simulatedTrading": true, "liveTradingEnabled": false }`. Empty/omitted secret fields keep the previous value.
Data: same shape as GET.

### POST /api/settings/okx/test
Data: `{ "success": false, "message": "请先在系统设置中保存欧易 API Key、Secret Key 与 Passphrase，再测试连接", "account": null }`.
When credentials exist: `{ "success": true, "message": "连接成功", "account": { "totalEquity": 100, "currency": "USDT" } }`.
This endpoint always answers HTTP 200 with the envelope; inspect `data.success`. Missing credentials are a normal
business result, not an error.

### GET /api/settings/okx/contract?instId=ETH-USDT-SWAP
Read-only diagnostic for the API account's position mode and the swap contract specification:
`{ "account": { "positionMode": "net_mode", "isLongShortMode": false },
"instrument": { "instId": "ETH-USDT-SWAP", "ctVal": 0.1, "lotSz": 0.01, "minSz": 0.01, "tickSz": ... } }`.
Used before enabling Live leverage/short tasks; no order is sent.

### POST /api/settings/paper-account/reset (deprecated)
Request `{ "balance": 100 }` → data: true. The internal paper account is no longer used by the UI; this endpoint is
kept only for backward compatibility and does not affect the OKX API account.

### GET /api/settings/ai
Data:
```json
{
  "enabled": false, "providerType": "deepseek",
  "baseUrl": "https://api.deepseek.com/v1", "model": "deepseek-chat",
  "hasApiKey": false, "apiKeyMasked": "", "timeoutSeconds": 30,
  "llmConfigured": false, "provider": "本地量化引擎",
  "message": "未配置大模型 Key，当前使用离线多因子引擎。"
}
```

### PUT /api/settings/ai
Request `{ "enabled": true, "providerType": "deepseek", "baseUrl": "https://api.deepseek.com/v1",
"model": "deepseek-chat", "apiKey": "sk-...", "timeoutSeconds": 30 }`. `providerType` is one of `openai`,
`deepseek`, `moonshot`, `qwen`, `zhipu`, `custom`; empty `baseUrl`/`model` falls back to the provider preset.
Empty `apiKey` keeps the stored key; the key is encrypted with Data Protection and never returned.
Data: same shape as GET. This is the UI-backed alternative to the
`Ai:Enabled/Ai:BaseUrl/Ai:ApiKey/Ai:Model/Ai:Provider` appsettings/env configuration and takes effect without a restart.

### GET /api/settings/system
Data: `{ "version": "1.0.0", "database": "connected", "mysqlVersion": "...", "klineCount": 5000,
"backtestCount": 10, "taskCount": 2, "serverTime": "...", "okxReachable": false, "syntheticFallbackEnabled": true }`

## 11. Health

### GET /api/health (anonymous)
Data: `{ "status": "ok", "time": "..." }`.

## Error message examples
- `用户名或密码错误`
- `策略不存在`
- `回测区间内没有可用的 K 线数据`
- `请先在设置中配置欧易 API Key`
- `实盘交易未开启，请先在设置页开启并确认风险`
- `任务运行中，请先停止任务`
- `OKX 当前不可达，已自动切换为示例行情数据`
- `保证金必须在 1 到 1,000,000 USDT 之间`
- `手动交易只支持模拟盘；策略仓位请在交易任务页管理`
- `读取合约规格失败：...`
- `设置杠杆失败：...`
- `该任务当前没有可平仓的持仓`
- `可用余额不足，无法冻结保证金`

## Seed account
`admin / admin123` (first login should change password). Paper account starts at **100 USDT**.

## Implementation notes (2026-10)
- Business/validation failures are returned as HTTP 400/401/404 with the same `{success:false,...}` envelope. The
  frontend unwraps `message` for both HTTP errors and `success:false` payloads.
- `POST /api/settings/okx/test` is the one endpoint that intentionally uses HTTP 200 for a negative business
  result; check `data.success` instead of the HTTP status.
- Nullable fields (for example `connectionError`, `savedAt`, `filledAt`) are omitted from JSON when null.
- `GET /api/backtests` returns `PagedResult<BacktestSummaryDto>`; the frontend accepts both paged and bare arrays.
- `GET /api/positions?status=open` returns a bare array of `PositionDto`, not a paged object.
- The internal paper engine remains for historical audit only; new tasks reject `mode: "Paper"` and the UI no longer
  exposes the paper account. Deleting a stopped legacy paper task may auto-close its paper positions before removal.
- Legacy paper accounting (historical records): long/short signals and isolated leverage (1–20) with a margin freeze
  model; opening reserved `margin` and charged a 0.1% fee, PnL was direction-aware, max loss was capped at margin.
- Live strategy tasks execute through the user's OKX account. 1× long uses spot; leverage > 1 or a short target
  uses the USDT perpetual swap (`<instId>-SWAP`) with isolated margin, `posSide` when the account is in long/short
  mode, and `reduceOnly` when closing. Positions store base-asset quantity plus `ctVal` for PnL.
- The manual trading desk reads and trades the OKX API account directly. A real-environment key is connected and a
  minimum-size real test has been completed: ETH-USDT-SWAP long 0.01 contracts (0.001 ETH) at 3×, entry 2324.43,
  reduceOnly exit 2324.10, orderIds `4043337784531226624` / `4043338265768890368`. Price PnL was about -0.00033 USDT;
  actual fees are visible in the OKX bill. No position remains.
- The OKX contract round-trip self-test endpoint was removed after verification; only the read-only
  `GET /api/settings/okx/contract?instId=...` diagnostic remains.
- K-line colors are a client-side setting (`localStorage qts_kline_colors_v2`), default green-up/red-down. The trade
  chart shows 60 bars initially and preserves the user's zoom range on refresh while staying anchored to the latest bar.
- `Synthetic` market data is now consistent across timeframes (`1m/5m/15m/30m/1H/4H/1D` share one price path per
  instrument), so the ticker and positions agree. The seed sample backtest is `+5.68%` with `-6.35%` max drawdown.
- Every strategy has an `allowShort` boolean parameter (default `true`); setting it to false turns short signals into
  flat for that strategy.
- The AI decision engine runs locally by default (no API key, no network): EMA/MACD/RSI/ATR/volume/support-resistance
  produce a trend score and a parameterized plan. If `Ai:Enabled=true` and an OpenAI-compatible endpoint is configured,
  the plan is refined by the model but strategy code, parameters and risk values are still validated locally; failures
  fall back to the local engine. AI output is for research only and never sends live orders.
- `GET /api/market/candles` falls back to a full synthetic window when local cached candles cover less than 80% of the
  requested limit, so chart pages are not blank after a database reset.

