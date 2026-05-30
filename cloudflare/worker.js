// duk シグナリングサーバー - Cloudflare Workers
// デプロイ: wrangler deploy

// ルーム管理 (Workers の Durable Objects を使う本番実装用)
// ここではシンプルなインメモリ版

const rooms = new Map(); // roomCode → Set<WebSocket>

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    // WebSocket アップグレード
    if (request.headers.get("Upgrade") === "websocket") {
      return handleWebSocket(request);
    }

    // 招待ページリダイレクト: /join?room=XXX → duk://join/XXX
    if (url.pathname === "/join") {
      const room = url.searchParams.get("room") ?? "";
      const html = buildJoinPage(room);
      return new Response(html, {
        headers: { "Content-Type": "text/html; charset=utf-8" }
      });
    }

    // ヘルスチェック
    if (url.pathname === "/health") {
      return new Response(JSON.stringify({ ok: true, rooms: rooms.size }), {
        headers: { "Content-Type": "application/json" }
      });
    }

    return new Response("duk signaling server", { status: 200 });
  }
};

function handleWebSocket(request) {
  const { 0: client, 1: server } = new WebSocketPair();
  server.accept();

  let currentRoom = null;
  let userName    = request.headers.get("X-User") ?? "Anonymous";

  server.addEventListener("message", (event) => {
    try {
      const msg = JSON.parse(event.data);

      switch (msg.type) {
        case "create":
          currentRoom = msg.room;
          if (!rooms.has(currentRoom)) rooms.set(currentRoom, new Set());
          rooms.get(currentRoom).add(server);
          server.send(JSON.stringify({ type: "created", room: currentRoom }));
          break;

        case "join":
          currentRoom = msg.room;
          userName    = msg.name ?? userName;
          if (!rooms.has(currentRoom)) {
            server.send(JSON.stringify({ type: "error", message: "ルームが見つかりません" }));
            return;
          }
          rooms.get(currentRoom).add(server);
          // 他メンバーに通知
          broadcast(currentRoom, server, { type: "join", name: userName });
          server.send(JSON.stringify({ type: "joined", room: currentRoom }));
          break;

        case "leave":
          leaveRoom(server, currentRoom, userName);
          currentRoom = null;
          break;

        case "text":
          // テキスト変更をルーム内の全員に配信
          broadcast(currentRoom, server, {
            type: "text",
            text: msg.text,
            name: userName,
            version: msg.version
          });
          break;

        case "signal":
          // WebRTC シグナリング (offer/answer/candidate) を転送
          broadcast(currentRoom, server, {
            type: "signal",
            data: msg.data,
            from: userName
          });
          break;
      }
    } catch (e) {
      console.error("message error", e);
    }
  });

  server.addEventListener("close", () => {
    leaveRoom(server, currentRoom, userName);
  });

  return new Response(null, {
    status: 101,
    webSocket: client
  });
}

function broadcast(room, sender, msg) {
  if (!room || !rooms.has(room)) return;
  const data = JSON.stringify(msg);
  for (const ws of rooms.get(room)) {
    if (ws !== sender && ws.readyState === 1 /* OPEN */) {
      try { ws.send(data); } catch {}
    }
  }
}

function leaveRoom(ws, room, name) {
  if (!room || !rooms.has(room)) return;
  rooms.get(room).delete(ws);
  if (rooms.get(room).size === 0) rooms.delete(room);
  else broadcast(room, ws, { type: "leave", name });
}

// 招待ページHTML（リンクをクリックするとアプリが起動）
function buildJoinPage(room) {
  return `<!DOCTYPE html>
<html lang="ja">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>duk セッションに参加</title>
  <style>
    * { margin: 0; padding: 0; box-sizing: border-box; }
    body {
      background: #0e0620;
      color: #ccc;
      font-family: 'Segoe UI', sans-serif;
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 100vh;
    }
    .card {
      background: #1a1030;
      border: 1px solid #3c2a6e;
      border-radius: 16px;
      padding: 48px;
      max-width: 440px;
      width: 90%;
      text-align: center;
    }
    .logo { font-size: 48px; font-weight: 900; color: #7c3aed; margin-bottom: 8px; }
    .sub  { color: #6e6e6e; font-size: 14px; margin-bottom: 32px; }
    .code {
      font-family: Consolas, monospace;
      font-size: 28px;
      font-weight: bold;
      color: #a78bfa;
      background: #0e0620;
      border-radius: 8px;
      padding: 12px 24px;
      letter-spacing: 4px;
      margin-bottom: 32px;
      display: inline-block;
    }
    .btn {
      display: inline-block;
      background: #7c3aed;
      color: white;
      padding: 14px 32px;
      border-radius: 8px;
      font-size: 16px;
      font-weight: 600;
      text-decoration: none;
      transition: background 0.2s;
    }
    .btn:hover { background: #6d28d9; }
    .hint { color: #555; font-size: 12px; margin-top: 16px; }
  </style>
</head>
<body>
  <div class="card">
    <div class="logo">duk</div>
    <p class="sub">リアルタイム共同コーディング</p>
    <div class="code">${room}</div>
    <br>
    <a class="btn" href="duk://join/${room}" id="joinBtn">
      duk で参加する
    </a>
    <p class="hint">duk がインストールされている必要があります</p>
  </div>
  <script>
    // ページ表示後に自動でアプリを起動
    setTimeout(() => {
      window.location.href = "duk://join/${room}";
    }, 500);
  </script>
</body>
</html>`;
}
