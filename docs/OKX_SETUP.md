# 欧易（OKX）联调配置指南

本文说明如何安全地把 QTS 接到欧易模拟盘或实盘。**默认只用模拟盘**，实盘需要显式开启并自行承担风险。

## 1. 先区分「模拟盘」与「实盘」

| | 模拟盘（Demo Trading） | 实盘（Live） |
| --- | --- | --- |
| OKX API Key 创建入口 | OKX → 模拟交易 → 模拟盘 API | OKX → 账户 → API |
| QTS 设置 | 打开「模拟盘」开关 | 关闭模拟盘，并手动打开「实盘交易」 |
| 请求头 | `x-simulated-trading: 1` | 不发送该头 |
| 资金风险 | 无真实资金 | 真实本金风险 |

QTS 的 `OkxClient` 会根据设置自动附加 `x-simulated-trading: 1`，因此**模拟盘密钥必须配合模拟盘开关使用**，
两者不匹配会出现签名/权限/账户不存在类错误。

## 2. 创建 API Key 的安全要求

- 权限：只勾选 **读取 + 交易**，**绝对不要开启提币**；
- 绑定 **IP 白名单**（服务器出口 IP）；
- 为 QTS 单独创建一把 Key，不要复用其他系统；
- Passphrase 是创建 Key 时单独设置的密码短语，不是登录密码，也不是 2FA 验证码；
- 任何密钥都不要提交到 Git、不要发到聊天/工单/截图里。若已泄露，立即删除并重建。

## 3. 在 QTS 中配置

1. 启动后端并打开 <http://localhost:5088/settings>；
2. 填写 `API Key`、`Secret Key`、`Passphrase`；
3. 模拟环境联调：保持「OKX 模拟环境」开启；真实账户需关闭该开关；
4. 保存后点击「测试连接」；
5. 测试成功会显示账户币种与总权益；失败会显示中文原因（未配置/网络/签名/权限）。

> 已保存的凭据使用 ASP.NET Core DataProtection 加密后写入 MySQL（密文前缀 `qts:v1:`）。
> 密钥目录在 `App_Data/keys/`，请备份；删除后旧密文无法解密，需要重新录入。
> 在设置页修改时，API Key / Secret Key / Passphrase 留空表示保持原值，只补填缺失项即可。

## 4. 联调顺序（建议）

1. **连接测试**：`设置 → 测试连接`，确认能读取 API 账户；
2. **公共行情**：行情页确认 K 线与 Ticker 正常（公共接口不需要签名）；
3. **任务联调**：任务页新建 API 任务，选择 `BTC-USDT / 1H / 双均线`，观察后台在新 K 线后是否产生订单；
4. **合约链路**：如需杠杆/做空，在模拟环境创建 Live 任务（例如 ETH-USDT、2×，分配 5 USDT），确认订单出现在
   OKX 演示账户，任务页“平仓”能提交 `reduceOnly` 市价单；`GET /api/settings/okx/contract?instId=ETH-USDT-SWAP`
   可查看账户持仓模式与合约面值/最小张数；
5. **对账**：订单/持仓页核对成交价、数量、手续费、任务归属；
6. **异常演练**：断网、错误 Key、错误 Passphrase 各测一次，确认任务进入 Error 且前端可读错误；
7. **实盘评估**：以上全部稳定后，再考虑极小资金实盘；QTS 默认拒绝没有凭据或未开启开关的 Live 任务。
   **1× 多单走现货，杠杆 >1 或做空走 USDT 永续（逐仓）**。已用真实资金完成最小仓验证：ETH-USDT-SWAP
   0.01 张、3×，开仓 2324.43、平仓 2324.10，价差毛利约 -0.00033 USDT，手续费约 0.0023 USDT（以 OKX 账单为准），
   无遗留持仓。当前真实权益约 15.06 USDT、可用约 7.72 USDT。手动交易台与策略任务都已改为 OKX API 账户，
   不再使用内部纸面账户。

## 5. 常见问题

| 现象 | 排查 |
| --- | --- |
| `请先在系统设置中保存...` | 三项凭据未补齐；注意 Passphrase 不是登录密码 |
| `API Key 与 Secret Key 已保存，但仍缺少 Passphrase` | 只需在设置页补填 Passphrase 后保存 |
| `OK-ACCESS-SIGN` 签名错误 | 检查服务器时间是否准确、Passphrase 是否正确、BaseUrl 是否为 `https://www.okx.com` |
| 连接超时 / DNS 失败 | 服务器无法访问 `www.okx.com`；检查 DNS、代理、防火墙、出口 IP 白名单 |
| `50101 APIKey does not match current environment` | Key 与「模拟盘」开关环境不匹配。模拟盘 Key 必须开启模拟盘；实盘 Key 必须关闭模拟盘。请到对应环境的 API 管理页确认 Key 类型 |
| `The SSL connection could not be established` / `安全包中没有可用的凭证` | .NET 的 Windows TLS 凭证在受限容器/沙箱内不可用。请用桌面进程直接运行 `start.bat`，不要在受限沙箱中启动 QTS |
| 模拟盘账户不存在 | Key 是在实盘创建的，或「模拟盘」开关与 Key 类型不一致 |
| 实盘任务被拒绝 | 需要同时满足：完整凭据 + 实盘开关开启 + 任务模式为 Live |
| `59103 Account margin is insufficient and the leverage is too low` | 任务的有效保证金不足以覆盖最小合约名义价值。有效保证金 = 分配资金 × 最大仓位%。BTC-USDT-SWAP 最小约 8 USDT 名义、ETH 约 2.3 USDT；请提高杠杆、增加分配资金/最大仓位%，或改用 ETH。QTS 现在会在发单前本地检查并返回中文建议，不再直接抛原始 59103 |
| 最小仓测算 | 张数 = 有效保证金 × 杠杆 ÷ 价格 ÷ ctVal，向下取整到 `lotSz`；低于 `minSz` 即无法开仓。以 BTC 80,000、ctVal=0.01 为例，0.01 张名义约 8 USDT，2× 至少需要 4 USDT 有效保证金 |

## 6. 当前运行环境说明

- 普通桌面进程（双击 `start.bat`）会同时启动本机 TLS 代理 `scripts/okx-proxy.mjs`（127.0.0.1:5090），
  QTS 的 OKX 请求通过该代理完成 TLS；设置页 API 地址显示代理地址属于正常现象。模拟环境账户测试成功可读取 USDT 权益。
- 当前真实环境 Key 已连接成功；真实资金最小仓验证已完成（ETH-USDT-SWAP 0.01 张、3×，开仓 2324.43、
  平仓 2324.10），当前权益约 15.06 USDT、可用约 7.72 USDT，无持仓。若切换环境报 `50101`，说明该 Key 属于另一个环境。
- 若 QTS 由受限沙箱/容器/某些 IDE 终端启动，Windows Schannel 可能报
  `The SSL connection could not be established` 或 `安全包中没有可用的凭证`，这不是 OKX 或 API Key 的问题；
  改用 `start.bat` 启动即可恢复。
- 当 OKX 确实不可达时，行情仍会自动切换为确定性合成数据，并在页面与接口中标注 `synthetic`；
  不会伪造真实账户/成交数据，也不会在未满足条件时下真实订单。
