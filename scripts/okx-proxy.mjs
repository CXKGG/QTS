import http from 'node:http'
import https from 'node:https'

const listenPort = Number(process.env.OKX_PROXY_PORT || 5090)
const upstreamHost = 'www.okx.com'

const server = http.createServer((req, res) => {
  const headers = { ...req.headers, host: upstreamHost }
  const upstream = https.request(
    {
      hostname: upstreamHost,
      port: 443,
      path: req.url,
      method: req.method,
      headers,
      timeout: 30000
    },
    (upRes) => {
      res.writeHead(upRes.statusCode || 502, upRes.headers)
      upRes.pipe(res)
    }
  )

  upstream.on('timeout', () => upstream.destroy(new Error('upstream timeout')))
  upstream.on('error', (err) => {
    if (!res.headersSent) {
      res.writeHead(502, { 'content-type': 'application/json; charset=utf-8' })
    }
    res.end(JSON.stringify({ code: '59000', msg: 'local OKX proxy: ' + err.message, data: [] }))
  })
  req.pipe(upstream)
})

server.listen(listenPort, '127.0.0.1', () => {
  console.log(`[okx-proxy] listening on http://127.0.0.1:${listenPort} -> https://${upstreamHost}`)
})
