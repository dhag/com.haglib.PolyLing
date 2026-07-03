// Remote/RemoteHtmlClient.cs
// ブラウザ向けHTMLクライアントを文字列として保持する
// RemoteServerがHTTPリクエスト時にこのHTMLを返す

namespace Poly_Ling.Remote
{
    public static class RemoteHtmlClient
    {
        public static string GetHtml(int port)
        {
            return HTML_TEMPLATE.Replace("{{PORT}}", port.ToString());
        }

        private const string HTML_TEMPLATE = @"<!DOCTYPE html>
<html lang=""ja"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>PolyLing Remote</title>
<style>
  * { margin: 0; padding: 0; box-sizing: border-box; }
  body {
    font-family: -apple-system, 'Segoe UI', sans-serif;
    background: #1e1e2e; color: #cdd6f4;
    font-size: 14px;
  }
  header {
    background: #313244; padding: 10px 16px;
    display: flex; align-items: center; gap: 12px;
    border-bottom: 1px solid #45475a;
  }
  header h1 { font-size: 16px; font-weight: 600; }
  #status {
    padding: 3px 10px; border-radius: 10px; font-size: 12px;
  }
  #status.connected { background: #a6e3a1; color: #1e1e2e; }
  #status.disconnected { background: #f38ba8; color: #1e1e2e; }
  #status.connecting { background: #f9e2af; color: #1e1e2e; }

  .toolbar {
    background: #313244; padding: 6px 16px;
    display: flex; gap: 8px; align-items: center;
    border-bottom: 1px solid #45475a;
  }
  .toolbar button {
    background: #585b70; color: #cdd6f4; border: none;
    padding: 4px 12px; border-radius: 4px; cursor: pointer;
    font-size: 12px;
  }
  .toolbar button:hover { background: #6c7086; }

  #meshList {
    padding: 8px;
  }
  .mesh-item {
    display: flex; align-items: center; gap: 8px;
    padding: 6px 10px; margin: 2px 0;
    border-radius: 4px; cursor: pointer;
    transition: background 0.1s;
  }
  .mesh-item:hover { background: #45475a; }
  .mesh-item.selected { background: #585b70; border-left: 3px solid #89b4fa; }
  .mesh-item .index {
    color: #6c7086; font-size: 11px; min-width: 24px; text-align: right;
  }
  .mesh-item .name { flex: 1; }
  .mesh-item .type {
    font-size: 11px; padding: 1px 6px; border-radius: 3px;
    background: #45475a; color: #a6adc8;
  }
  .mesh-item .type.Bone { background: #45475a; color: #f9e2af; }
  .mesh-item .type.Morph { background: #45475a; color: #cba6f7; }
  .mesh-item .stats { color: #6c7086; font-size: 11px; }
  .mesh-item .vis-btn {
    background: none; border: none; cursor: pointer;
    font-size: 14px; padding: 2px; color: #a6adc8;
  }
  .mesh-item .vis-btn.hidden { opacity: 0.3; }

  #detail {
    background: #313244; margin: 8px; padding: 12px;
    border-radius: 6px; font-size: 13px;
    display: none;
  }
  #detail.visible { display: block; }
  #detail h3 { margin-bottom: 8px; font-size: 14px; }
  #detail .row { display: flex; gap: 8px; margin: 3px 0; }
  #detail .label { color: #6c7086; min-width: 100px; }

  #log {
    position: fixed; bottom: 0; left: 0; right: 0;
    background: #181825; border-top: 1px solid #45475a;
    max-height: 120px; overflow-y: auto;
    padding: 6px 12px; font-size: 11px;
    font-family: monospace; color: #6c7086;
  }

  #imagePanel {
    background: #313244; margin: 8px; padding: 12px;
    border-radius: 6px;
  }
  #imagePanel h3 { font-size: 14px; margin-bottom: 8px; }
  #imagePanel .img-grid {
    display: flex; flex-wrap: wrap; gap: 8px;
  }
  #imagePanel .img-entry {
    background: #45475a; border-radius: 4px; padding: 6px;
    text-align: center;
  }
  #imagePanel .img-entry img {
    display: block; margin-bottom: 4px;
    image-rendering: pixelated;
    border: 1px solid #585b70;
  }
  #imagePanel .img-entry .img-info {
    font-size: 11px; color: #a6adc8;
  }
</style>
</head>
<body>

<header>
  <h1>PolyLing Remote</h1>
  <span id=""status"" class=""disconnected"">Disconnected</span>
</header>

<div class=""toolbar"">
  <button onclick=""refreshMeshList()"">Refresh</button>
  <button onclick=""queryModelInfo()"">Model Info</button>
</div>

<div id=""meshList""></div>
<div id=""detail""></div>
<div id=""imagePanel"" style=""display:none"">
  <h3>Images</h3>
  <div class=""img-grid"" id=""imageGrid""></div>
</div>
<div id=""log""></div>

<script>
const PORT = {{PORT}};
let ws = null;
let requestId = 0;
const pendingCallbacks = {};

// ================================================================
// WebSocket接続
// ================================================================

function connect() {
  setStatus('connecting');
  ws = new WebSocket(`ws://localhost:${PORT}/`);
  ws.binaryType = 'arraybuffer';

  ws.onopen = () => {
    setStatus('connected');
    log('Connected');
    refreshMeshList();
  };

  ws.onclose = () => {
    setStatus('disconnected');
    log('Disconnected. Reconnecting in 3s...');
    setTimeout(connect, 3000);
  };

  ws.onerror = (e) => {
    log('WebSocket error');
  };

  ws.onmessage = (event) => {
    if (event.data instanceof ArrayBuffer) {
      // Binaryフレーム = DuplexPacket（Json/Binary アイテム混在の可能性あり）
      const items = parseDuplexPacket(event.data) || [];
      const jsonItems = [];
      const binItems = [];
      for (const it of items) {
        if (it.type === 0 || it.type === 3) {
          jsonItems.push(new TextDecoder().decode(it.data));
        } else {
          binItems.push(it.data);
        }
      }
      dispatchEnvelopeItems(jsonItems, binItems);
      return;
    }

    // Textフレーム = JSON封筒 {type,id,items[]}
    const env = decodeTextEnvelope(event.data);
    dispatchEnvelopeItems(env.jsonItems, env.binItems);
  };
}

// ================================================================
// 送受信
// ================================================================

function send(obj) {
  return new Promise((resolve, reject) => {
    if (!ws || ws.readyState !== WebSocket.OPEN) {
      reject(new Error('Not connected'));
      return;
    }
    const id = `r${++requestId}`;
    obj.id = id;
    pendingCallbacks[id] = resolve;
    ws.send(encodeJsonEnvelope(obj));

    // タイムアウト
    setTimeout(() => {
      if (pendingCallbacks[id]) {
        delete pendingCallbacks[id];
        reject(new Error('Timeout'));
      }
    }, 5000);
  });
}

function handlePush(msg) {
  log(`Push: ${msg.event}`);
  if (msg.event === 'meshListChanged') {
    renderMeshList(msg.data);
  }
}

// ================================================================
// クエリ
// ================================================================

async function refreshMeshList() {
  try {
    const res = await send({
      type: 'query',
      target: 'meshList',
      fields: ['Name', 'IsVisible', 'IsLocked', 'Type', 'VertexCount', 'FaceCount', 'Depth']
    });
    if (res.success) {
      renderMeshList(res.data);
    } else {
      log(`Error: ${res.error}`);
    }
  } catch (e) {
    log(`Query failed: ${e.message}`);
  }
}

async function queryModelInfo() {
  try {
    const res = await send({ type: 'query', target: 'modelInfo' });
    if (res.success) {
      log(`Model: ${JSON.stringify(res.data)}`);
    }
  } catch (e) {
    log(`Query failed: ${e.message}`);
  }
}

async function queryMeshDetail(index) {
  try {
    const res = await send({
      type: 'query',
      target: 'meshData',
      params: { index: index.toString() },
      fields: ['Name', 'IsVisible', 'IsLocked', 'Type', 'VertexCount', 'FaceCount',
               'MirrorType', 'MirrorAxis', 'Depth', 'ParentIndex',
               'IsMorph', 'MorphName', 'SelectMode']
    });
    if (res.success) {
      renderDetail(res.data);
    }
  } catch (e) {
    log(`Detail query failed: ${e.message}`);
  }
}

// ================================================================
// コマンド
// ================================================================

async function selectMesh(index) {
  try {
    await send({
      type: 'command',
      action: 'selectMesh',
      params: { index: index.toString() }
    });
    log(`Selected mesh ${index}`);
    refreshMeshList();
    queryMeshDetail(index);
  } catch (e) {
    log(`Select failed: ${e.message}`);
  }
}

async function toggleVisibility(index, currentVisible) {
  try {
    await send({
      type: 'command',
      action: 'updateAttribute',
      params: { index: index.toString(), visible: (!currentVisible).toString() }
    });
    refreshMeshList();
  } catch (e) {
    log(`Toggle failed: ${e.message}`);
  }
}

// ================================================================
// レンダリング
// ================================================================

function renderMeshList(data) {
  const container = document.getElementById('meshList');
  if (!data || !data.meshes) {
    container.innerHTML = '<p style=""padding:20px;color:#6c7086"">No data</p>';
    return;
  }

  container.innerHTML = data.meshes.map(m => {
    const selClass = m.selected ? ' selected' : '';
    const typeClass = m.Type || 'Mesh';
    const visIcon = m.IsVisible ? '👁' : '👁';
    const visClass = m.IsVisible ? '' : ' hidden';
    const indent = (m.Depth || 0) * 16;

    return `<div class=""mesh-item${selClass}"" style=""padding-left:${10 + indent}px"">
      <span class=""index"">${m.index}</span>
      <button class=""vis-btn${visClass}"" onclick=""event.stopPropagation(); toggleVisibility(${m.index}, ${m.IsVisible})"">${visIcon}</button>
      <span class=""name"" onclick=""selectMesh(${m.index})"">${esc(m.Name)}</span>
      <span class=""type ${typeClass}"">${m.Type || 'Mesh'}</span>
      <span class=""stats"">V:${m.VertexCount || 0} F:${m.FaceCount || 0}</span>
    </div>`;
  }).join('');
}

function renderDetail(data) {
  const el = document.getElementById('detail');
  if (!data) { el.className = ''; return; }
  el.className = 'visible';

  const rows = Object.entries(data).map(([k, v]) =>
    `<div class=""row""><span class=""label"">${k}</span><span>${JSON.stringify(v)}</span></div>`
  ).join('');

  el.innerHTML = `<h3>${esc(data.Name || 'Detail')}</h3>${rows}`;
}

// ================================================================
// ユーティリティ
// ================================================================

function setStatus(state) {
  const el = document.getElementById('status');
  el.className = state;
  el.textContent = state.charAt(0).toUpperCase() + state.slice(1);
}

function log(msg) {
  const el = document.getElementById('log');
  const time = new Date().toLocaleTimeString();
  el.innerHTML += `<div>[${time}] ${esc(msg)}</div>`;
  el.scrollTop = el.scrollHeight;
}

function esc(s) {
  if (s == null) return '';
  const d = document.createElement('div');
  d.textContent = String(s);
  return d.innerHTML;
}

// ================================================================
// Duplex 封筒（com.haglib.net_duplexchannel 互換）
// Text: {type,id,items:[{type,mimeType,data,encoding}]}
// Binary: DuplexPacket(16B header 'DPX\n'|ver|type|id|payloadLen|tagLen) + tag + TypedPayload
// ContentType: Text=0, Binary=1, Image=2, Json=3, Custom=255
// ================================================================

let envId = 0;

function b64ToBytes(b64) {
  const bin = atob(b64);
  const u = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) u[i] = bin.charCodeAt(i);
  return u;
}

function encodeJsonEnvelope(appObj) {
  return JSON.stringify({
    type: 'request',
    id: ++envId,
    items: [{ type: 3, mimeType: 'application/json', data: JSON.stringify(appObj) }]
  });
}

function decodeTextEnvelope(str) {
  const jsonItems = [];
  const binItems = [];
  let root;
  try { root = JSON.parse(str); } catch (e) { return { jsonItems, binItems }; }
  const items = Array.isArray(root.items) ? root.items : null;
  if (items) {
    for (const it of items) {
      const t = it.type | 0;
      if (t === 0 || t === 3) {
        jsonItems.push(it.data != null ? String(it.data) : '');
      } else {
        const raw = it.data != null ? String(it.data) : '';
        binItems.push(it.encoding === 'base64' ? b64ToBytes(raw) : new TextEncoder().encode(raw));
      }
    }
  } else if (root.text != null) {
    jsonItems.push(String(root.text));
  } else if (root.json != null) {
    jsonItems.push(typeof root.json === 'string' ? root.json : JSON.stringify(root.json));
  }
  return { jsonItems, binItems };
}

function parseDuplexPacket(buffer) {
  const view = new DataView(buffer);
  if (buffer.byteLength < 16) return null;
  // Magic 'DPX\n' = 0x44 0x50 0x58 0x0A
  if (view.getUint8(0) !== 0x44 || view.getUint8(1) !== 0x50 ||
      view.getUint8(2) !== 0x58 || view.getUint8(3) !== 0x0A) return null;
  if (view.getUint8(4) !== 1) return null; // version
  const payloadLen = view.getUint32(10, true);
  const tagLen = view.getUint16(14, true);
  const payloadStart = 16 + tagLen;
  if (payloadStart + payloadLen > buffer.byteLength) return null;
  const payload = new Uint8Array(buffer, payloadStart, payloadLen);
  return parseTypedPayload(payload);
}

function parseTypedPayload(bytes) {
  const dv = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  if (bytes.byteLength < 4) return [];
  const count = dv.getInt32(0, true);
  let off = 4;
  const out = [];
  for (let i = 0; i < count; i++) {
    if (off + 4 > bytes.byteLength) break;
    const size = dv.getInt32(off, true); off += 4;
    if (off + size > bytes.byteLength) break;
    const type = dv.getUint8(off);
    const mimeLen = dv.getUint16(off + 1, true);
    const dataStart = off + 3 + mimeLen;
    const dataLen = size - 3 - mimeLen;
    const data = bytes.subarray(dataStart, dataStart + dataLen);
    out.push({ type, data });
    off += size;
  }
  return out;
}

// 封筒アイテムを既存処理へ振り分ける（Text/Binary フレーム共通）。
// jsonItems: 応答/pushのJSON文字列群、binItems: PLRx バイト列(Uint8Array)群。
function dispatchEnvelopeItems(jsonItems, binItems) {
  for (const j of jsonItems) {
    let msg;
    try { msg = JSON.parse(j); } catch (e) { continue; }
    if (msg.id && pendingCallbacks[msg.id]) {
      pendingCallbacks[msg.id](msg);
      delete pendingCallbacks[msg.id];
    } else if (msg.type === 'push') {
      handlePush(msg);
    }
  }
  for (const b of binItems) {
    handleBinaryMessage(b.slice().buffer);
  }
}

// ================================================================
// バイナリメッセージ処理
// ================================================================

// マジック定数（リトルエンディアン）
const MAGIC_MESH  = 0x4D524C50; // ""PLRM""
const MAGIC_IMAGE = 0x49524C50; // ""PLRI""

function handleBinaryMessage(buffer) {
  const view = new DataView(buffer);
  if (buffer.byteLength < 4) { log('Binary too short'); return; }

  const magic = view.getUint32(0, true); // little-endian

  if (magic === MAGIC_IMAGE) {
    handleImageList(buffer, view);
  } else if (magic === MAGIC_MESH) {
    log(`Mesh binary received: ${buffer.byteLength}B`);
  } else {
    log(`Unknown binary magic: 0x${magic.toString(16)}`);
  }
}

function handleImageList(buffer, view) {
  // Header: Magic(4) + Version(1) + ImageCount(2) + Reserved(1) = 8B
  if (buffer.byteLength < 8) { log('Image header too short'); return; }

  const version = view.getUint8(4);
  const imageCount = view.getUint16(5, true);
  log(`Image list: ${imageCount} images (v${version})`);

  const panel = document.getElementById('imagePanel');
  const grid = document.getElementById('imageGrid');
  panel.style.display = 'block';
  grid.innerHTML = '';

  let offset = 8; // after header

  for (let i = 0; i < imageCount; i++) {
    if (offset + 15 > buffer.byteLength) {
      log(`Image entry ${i}: truncated`);
      break;
    }

    // Entry: ImageId(2) + Format(1) + Width(4) + Height(4) + DataLength(4) = 15B
    const imgId = view.getUint16(offset, true); offset += 2;
    const format = view.getUint8(offset); offset += 1;
    const width = view.getUint32(offset, true); offset += 4;
    const height = view.getUint32(offset, true); offset += 4;
    const dataLength = view.getUint32(offset, true); offset += 4;

    if (offset + dataLength > buffer.byteLength) {
      log(`Image ${imgId}: data truncated`);
      break;
    }

    const imageData = new Uint8Array(buffer, offset, dataLength);
    offset += dataLength;

    // Blob → URL
    const mimeType = format === 0 ? 'image/png' : 'image/jpeg';
    const blob = new Blob([imageData], { type: mimeType });
    const url = URL.createObjectURL(blob);

    // DOM要素作成
    const entry = document.createElement('div');
    entry.className = 'img-entry';

    const img = document.createElement('img');
    img.src = url;
    img.width = Math.max(width, 64);
    img.height = Math.max(height, 64);
    img.title = `ID:${imgId} ${width}x${height} ${mimeType}`;

    const info = document.createElement('div');
    info.className = 'img-info';
    info.textContent = `#${imgId} ${width}x${height} (${(dataLength/1024).toFixed(1)}KB)`;

    entry.appendChild(img);
    entry.appendChild(info);
    grid.appendChild(entry);
  }

  log(`Rendered ${imageCount} images`);
}

// ================================================================
// 起動
// ================================================================

connect();
</script>
</body>
</html>";
    }
}
