namespace TubaWinUi3.Services;

public static class FluentPageHtml
{
    public static string GetHtml() => """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>局域网文件分享</title>
<style>
:root {
  --bg: #f5f5f5;
  --bg-card: #fafafa;
  --bg-hover: #f0f0f0;
  --bg-subtle: #f9f9f9;
  --text-primary: rgba(0,0,0,0.895);
  --text-secondary: rgba(0,0,0,0.606);
  --text-tertiary: rgba(0,0,0,0.445);
  --text-disabled: rgba(0,0,0,0.36);
  --accent: #005fb8;
  --accent-hover: #004e8c;
  --accent-light: rgba(0,95,184,0.08);
  --accent-text: #fff;
  --border: rgba(0,0,0,0.078);
  --border-strong: rgba(0,0,0,0.16);
  --danger: #c42b1c;
  --danger-hover: #a4231a;
  --danger-light: rgba(196,43,28,0.08);
  --success: #0f7b0f;
  --success-light: rgba(15,123,15,0.08);
  --shadow2: 0 1px 2px rgba(0,0,0,0.06), 0 2px 4px rgba(0,0,0,0.04);
  --shadow4: 0 2px 4px rgba(0,0,0,0.04), 0 4px 8px rgba(0,0,0,0.08);
  --shadow8: 0 2px 4px rgba(0,0,0,0.04), 0 8px 16px rgba(0,0,0,0.12);
  --radius-sm: 4px;
  --radius-md: 8px;
  --radius-lg: 12px;
  --font: 'Segoe UI Variable', 'Segoe UI', system-ui, -apple-system, sans-serif;
  --font-num: 'Segoe UI Variable', 'Segoe UI', ui-monospace, monospace;
}

@media (prefers-color-scheme: dark) {
  :root {
    --bg: #1a1a1a;
    --bg-card: #2d2d2d;
    --bg-hover: #383838;
    --bg-subtle: #252525;
    --text-primary: rgba(255,255,255,0.895);
    --text-secondary: rgba(255,255,255,0.606);
    --text-tertiary: rgba(255,255,255,0.445);
    --text-disabled: rgba(255,255,255,0.36);
    --accent: #60cdff;
    --accent-hover: #4db8e8;
    --accent-light: rgba(96,205,255,0.1);
    --accent-text: #000;
    --border: rgba(255,255,255,0.078);
    --border-strong: rgba(255,255,255,0.16);
    --danger: #ff6b6b;
    --danger-hover: #ff5252;
    --danger-light: rgba(255,107,107,0.1);
    --success: #6ccb5f;
    --success-light: rgba(108,203,95,0.1);
    --shadow2: 0 1px 2px rgba(0,0,0,0.2), 0 2px 4px rgba(0,0,0,0.16);
    --shadow4: 0 2px 4px rgba(0,0,0,0.16), 0 4px 8px rgba(0,0,0,0.24);
    --shadow8: 0 2px 4px rgba(0,0,0,0.16), 0 8px 16px rgba(0,0,0,0.32);
  }
}

* { margin: 0; padding: 0; box-sizing: border-box; }

body {
  font-family: var(--font);
  background: var(--bg);
  color: var(--text-primary);
  line-height: 1.5;
  min-height: 100vh;
  -webkit-font-smoothing: antialiased;
}

.icon {
  display: inline-block;
  width: 1em; height: 1em;
  vertical-align: -0.125em;
  fill: currentColor;
}
.icon-sm { width: 14px; height: 14px; }
.icon-md { width: 18px; height: 18px; }
.icon-lg { width: 24px; height: 24px; }
.icon-xl { width: 36px; height: 36px; }

.header {
  background: var(--bg-subtle);
  border-bottom: 1px solid var(--border);
  padding: 20px 32px;
  position: sticky;
  top: 0;
  z-index: 100;
  backdrop-filter: blur(20px);
  -webkit-backdrop-filter: blur(20px);
}

.header-inner {
  max-width: 960px;
  margin: 0 auto;
  display: flex;
  align-items: center;
  gap: 16px;
}

.header-icon {
  width: 40px; height: 40px;
  background: var(--accent);
  border-radius: var(--radius-md);
  display: flex; align-items: center; justify-content: center;
  color: var(--accent-text);
  flex-shrink: 0;
}

.header-text h1 {
  font-size: 20px;
  font-weight: 600;
  letter-spacing: -0.01em;
}

.header-text p {
  font-size: 12px;
  color: var(--text-secondary);
  margin-top: 2px;
}

.main {
  max-width: 960px;
  margin: 0 auto;
  padding: 24px 32px 48px;
}

.connect-card {
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  padding: 20px 24px;
  margin-bottom: 20px;
  box-shadow: var(--shadow2);
  display: flex;
  align-items: center;
  gap: 24px;
}

.connect-info { flex: 1; min-width: 0; }

.connect-label {
  font-size: 12px;
  color: var(--text-tertiary);
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  margin-bottom: 6px;
}

.connect-url {
  font-family: var(--font-num);
  font-size: 16px;
  font-weight: 600;
  color: var(--accent);
  user-select: all;
  cursor: pointer;
  word-break: break-all;
  line-height: 1.4;
}

.connect-actions {
  display: flex;
  gap: 8px;
  margin-top: 10px;
}

.connect-qr {
  flex-shrink: 0;
  background: #fff;
  border-radius: var(--radius-md);
  padding: 8px;
  border: 1px solid var(--border);
}

.connect-qr img {
  display: block;
  border-radius: 2px;
}

.btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 5px 12px;
  border: 1px solid var(--border-strong);
  border-radius: var(--radius-sm);
  background: var(--bg-card);
  color: var(--text-primary);
  font-size: 13px;
  font-family: var(--font);
  cursor: pointer;
  transition: all 0.1s;
  white-space: nowrap;
  line-height: 20px;
}
.btn:hover { background: var(--bg-hover); }
.btn:active { transform: scale(0.98); }

.btn-accent {
  background: var(--accent);
  color: var(--accent-text);
  border-color: var(--accent);
}
.btn-accent:hover { background: var(--accent-hover); border-color: var(--accent-hover); }

.btn-danger {
  color: var(--danger);
  border-color: var(--danger);
  background: var(--danger-light);
}
.btn-danger:hover { background: var(--danger); color: #fff; }

.toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 16px;
  flex-wrap: wrap;
}

.toolbar-spacer { flex: 1; }

.file-count {
  font-size: 12px;
  color: var(--text-tertiary);
}

.breadcrumb {
  display: flex;
  align-items: center;
  gap: 4px;
  margin-bottom: 12px;
  font-size: 13px;
  flex-wrap: wrap;
}
.breadcrumb-item {
  color: var(--accent);
  cursor: pointer;
  padding: 2px 6px;
  border-radius: var(--radius-sm);
  transition: background 0.1s;
}
.breadcrumb-item:hover { background: var(--accent-light); }
.breadcrumb-item.current {
  color: var(--text-primary);
  cursor: default;
  font-weight: 600;
}
.breadcrumb-item.current:hover { background: transparent; }
.breadcrumb-sep {
  color: var(--text-tertiary);
  font-size: 11px;
}

.drop-zone {
  border: 2px dashed var(--border-strong);
  border-radius: var(--radius-lg);
  padding: 40px 24px;
  text-align: center;
  margin-bottom: 20px;
  transition: all 0.2s;
  cursor: pointer;
  background: var(--bg-card);
}
.drop-zone:hover {
  border-color: var(--accent);
  background: var(--accent-light);
}
.drop-zone.drag-over {
  border-color: var(--accent);
  background: var(--accent-light);
  transform: scale(1.005);
  box-shadow: var(--shadow4);
}
.drop-zone-icon {
  color: var(--text-tertiary);
  margin-bottom: 8px;
}
.drop-zone.drag-over .drop-zone-icon { color: var(--accent); }
.drop-zone-title {
  font-size: 14px;
  font-weight: 600;
  margin-bottom: 4px;
}
.drop-zone-hint {
  font-size: 12px;
  color: var(--text-tertiary);
}

.file-list {
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  overflow: hidden;
  box-shadow: var(--shadow2);
}

.file-list-header {
  display: grid;
  grid-template-columns: 28px 1fr 100px 140px 80px;
  gap: 8px;
  padding: 8px 16px;
  font-size: 12px;
  color: var(--text-tertiary);
  font-weight: 600;
  border-bottom: 1px solid var(--border);
  background: var(--bg-subtle);
  align-items: center;
}

.file-row {
  display: grid;
  grid-template-columns: 28px 1fr 100px 140px 80px;
  gap: 8px;
  padding: 10px 16px;
  font-size: 13px;
  border-bottom: 1px solid var(--border);
  align-items: center;
  transition: background 0.1s;
}
.file-row:last-child { border-bottom: none; }
.file-row:hover { background: var(--bg-hover); }

.file-icon {
  color: var(--accent);
  display: flex;
  align-items: center;
  justify-content: center;
}
.file-icon.folder { color: var(--text-tertiary); }

.file-name {
  font-weight: 500;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  cursor: pointer;
}
.file-name.folder-name { color: var(--accent); }

.file-size {
  color: var(--text-secondary);
  font-family: var(--font-num);
  font-size: 12px;
}

.file-time {
  color: var(--text-secondary);
  font-size: 12px;
}

.file-actions {
  display: flex;
  gap: 4px;
  justify-content: flex-end;
}

.file-btn {
  width: 28px; height: 28px;
  border: none;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-secondary);
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.1s;
}
.file-btn:hover { background: var(--bg-hover); color: var(--text-primary); }
.file-btn.delete:hover { background: var(--danger-light); color: var(--danger); }

.empty-state {
  text-align: center;
  padding: 48px 24px;
  color: var(--text-tertiary);
}
.empty-state-icon { margin-bottom: 12px; }
.empty-state-text { font-size: 14px; }

.upload-progress {
  position: fixed;
  bottom: 24px;
  right: 24px;
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  padding: 16px 20px;
  box-shadow: var(--shadow8);
  min-width: 280px;
  z-index: 200;
  display: none;
}
.upload-progress.show { display: block; }
.upload-progress-title {
  font-size: 13px;
  font-weight: 600;
  margin-bottom: 8px;
}
.upload-progress-bar {
  height: 4px;
  background: var(--border);
  border-radius: 2px;
  overflow: hidden;
}
.upload-progress-fill {
  height: 100%;
  background: var(--accent);
  border-radius: 2px;
  transition: width 0.2s;
  width: 0%;
}

.preview-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0,0,0,0.85);
  z-index: 500;
  display: none;
  align-items: center;
  justify-content: center;
  flex-direction: column;
  padding: 24px;
}
.preview-overlay.show {
  display: flex;
}
.preview-close {
  position: absolute;
  top: 16px;
  right: 16px;
  width: 36px; height: 36px;
  border: none;
  border-radius: 50%;
  background: rgba(255,255,255,0.15);
  color: #fff;
  font-size: 18px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: background 0.1s;
}
.preview-close:hover { background: rgba(255,255,255,0.3); }
.preview-title {
  position: absolute;
  top: 20px;
  left: 24px;
  right: 70px;
  color: #fff;
  font-size: 14px;
  font-weight: 600;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.preview-content {
  max-width: 90vw;
  max-height: 80vh;
}
.preview-content img {
  max-width: 90vw;
  max-height: 80vh;
  object-fit: contain;
  border-radius: var(--radius-md);
}
.preview-content video {
  max-width: 90vw;
  max-height: 80vh;
  border-radius: var(--radius-md);
}
.preview-content audio {
  width: 400px;
  max-width: 90vw;
}
.preview-nav {
  position: absolute;
  top: 50%;
  transform: translateY(-50%);
  width: 40px; height: 40px;
  border: none;
  border-radius: 50%;
  background: rgba(255,255,255,0.15);
  color: #fff;
  font-size: 20px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: background 0.1s;
}
.preview-nav:hover { background: rgba(255,255,255,0.3); }
.preview-nav.prev { left: 16px; }
.preview-nav.next { right: 16px; }

.toast {
  position: fixed;
  bottom: 24px;
  left: 50%;
  transform: translateX(-50%) translateY(80px);
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  padding: 10px 20px;
  box-shadow: var(--shadow8);
  font-size: 13px;
  z-index: 300;
  opacity: 0;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  pointer-events: none;
}
.toast.show {
  opacity: 1;
  transform: translateX(-50%) translateY(0);
}

#fileInput { display: none; }
#folderInput { display: none; }

@media (max-width: 640px) {
  .header { padding: 16px; }
  .main { padding: 16px; }
  .file-list-header,
  .file-row {
    grid-template-columns: 28px 1fr 80px 60px;
  }
  .file-time { display: none; }
  .connect-card { flex-direction: column; align-items: stretch; }
  .connect-qr { align-self: center; }
}
</style>
</head>
<body>

<svg xmlns="http://www.w3.org/2000/svg" style="display:none">
<symbol id="i-share" viewBox="0 0 24 24"><path d="M18 16.08c-.76 0-1.44.3-1.96.77L8.91 12.7c.05-.23.09-.46.09-.7s-.04-.47-.09-.7l7.05-4.11c.54.5 1.25.81 2.04.81 1.66 0 3-1.34 3-3s-1.34-3-3-3-3 1.34-3 3c0 .24.04.47.09.7L8.04 9.81C7.5 9.31 6.79 9 6 9c-1.66 0-3 1.34-3 3s1.34 3 3 3c.79 0 1.5-.31 2.04-.81l7.12 4.16c-.05.21-.08.43-.08.65 0 1.61 1.31 2.92 2.92 2.92s2.92-1.31 2.92-2.92-1.31-2.92-2.92-2.92z"/></symbol>
<symbol id="i-folder" viewBox="0 0 24 24"><path d="M10 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z"/></symbol>
<symbol id="i-file" viewBox="0 0 24 24"><path d="M14 2H6c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V8l-6-6zm4 18H6V4h7v5h5v11z"/></symbol>
<symbol id="i-image" viewBox="0 0 24 24"><path d="M21 19V5c0-1.1-.9-2-2-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2zM8.5 13.5l2.5 3.01L14.5 12l4.5 6H5l3.5-4.5z"/></symbol>
<symbol id="i-video" viewBox="0 0 24 24"><path d="M18 4l2 4h-3l-2-4h-2l2 4h-3l-2-4H8l2 4H7L5 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V4h-4z"/></symbol>
<symbol id="i-audio" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z"/></symbol>
<symbol id="i-zip" viewBox="0 0 24 24"><path d="M20 6h-8l-2-2H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm-2 6h-2v2h2v2h-2v2h-2v-2h2v-2h-2v-2h2v-2h-2V8h2v2h2v2z"/></symbol>
<symbol id="i-code" viewBox="0 0 24 24"><path d="M9.4 16.6L4.8 12l4.6-4.6L8 6l-6 6 6 6 1.4-1.4zm5.2 0l4.6-4.6-4.6-4.6L16 6l6 6-6 6-1.4-1.4z"/></symbol>
<symbol id="i-text" viewBox="0 0 24 24"><path d="M14 2H6c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z"/></symbol>
<symbol id="i-download" viewBox="0 0 24 24"><path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"/></symbol>
<symbol id="i-delete" viewBox="0 0 24 24"><path d="M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z"/></symbol>
<symbol id="i-upload" viewBox="0 0 24 24"><path d="M9 16h6v-6h4l-7-7-7 7h4v6zm-4 2h14v2H5v-2z"/></symbol>
<symbol id="i-refresh" viewBox="0 0 24 24"><path d="M17.65 6.35C16.2 4.9 14.21 4 12 4c-4.42 0-7.99 3.58-7.99 8s3.57 8 7.99 8c3.73 0 6.84-2.55 7.73-6h-2.08c-.82 2.33-3.04 4-5.65 4-3.31 0-6-2.69-6-6s2.69-6 6-6c1.66 0 3.14.69 4.22 1.78L13 11h7V4l-2.35 2.35z"/></symbol>
<symbol id="i-copy" viewBox="0 0 24 24"><path d="M16 1H4c-1.1 0-2 .9-2 2v14h2V3h12V1zm3 4H8c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h11c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2zm0 16H8V7h11v14z"/></symbol>
<symbol id="i-open" viewBox="0 0 24 24"><path d="M19 19H5V5h7V3H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2v-7h-2v7zM14 3v2h3.59l-9.83 9.83 1.41 1.41L19 6.41V10h2V3h-7z"/></symbol>
<symbol id="i-info" viewBox="0 0 24 24"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z"/></symbol>
<symbol id="i-left" viewBox="0 0 24 24"><path d="M15.41 7.41L14 6l-6 6 6 6 1.41-1.41L10.83 12z"/></symbol>
<symbol id="i-right" viewBox="0 0 24 24"><path d="M10 6L8.59 7.41 13.17 12l-4.58 4.59L10 18l6-6z"/></symbol>
<symbol id="i-close" viewBox="0 0 24 24"><path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z"/></symbol>
</svg>

<div class="header">
  <div class="header-inner">
    <div class="header-icon"><svg class="icon icon-lg"><use href="#i-share"/></svg></div>
    <div class="header-text">
      <h1>局域网文件分享</h1>
      <p>同一网络下的设备可通过浏览器访问和下载共享文件</p>
    </div>
  </div>
</div>

<div class="main">
  <div class="connect-card" id="connectCard">
    <div class="connect-info">
      <div class="connect-label">其他设备访问地址</div>
      <div class="connect-url" id="shareUrl" title="点击复制">加载中...</div>
      <div class="connect-actions">
        <button class="btn btn-accent" onclick="copyUrl()"><svg class="icon icon-sm"><use href="#i-copy"/></svg> 复制地址</button>
        <button class="btn" onclick="openInBrowser()"><svg class="icon icon-sm"><use href="#i-open"/></svg> 在浏览器打开</button>
      </div>
    </div>
    <div class="connect-qr">
      <img id="qrImage" width="120" height="120" alt="QR Code" />
    </div>
  </div>

  <div class="toolbar">
    <button class="btn btn-accent" onclick="document.getElementById('fileInput').click()">
      <svg class="icon icon-sm"><use href="#i-upload"/></svg> 添加文件
    </button>
    <button class="btn" onclick="document.getElementById('folderInput').click()">
      <svg class="icon icon-sm"><use href="#i-folder"/></svg> 添加文件夹
    </button>
    <button class="btn" onclick="refreshFiles()">
      <svg class="icon icon-sm"><use href="#i-refresh"/></svg> 刷新
    </button>
    <div class="toolbar-spacer"></div>
    <span class="file-count" id="fileCount"></span>
    <button class="btn btn-danger" onclick="clearAll()">
      <svg class="icon icon-sm"><use href="#i-delete"/></svg> 清空
    </button>
  </div>

  <div id="breadcrumbContainer"></div>

  <input type="file" id="fileInput" multiple onchange="uploadFiles(this.files)" />
  <input type="file" id="folderInput" webkitdirectory onchange="uploadFiles(this.files)" />

  <div class="drop-zone" id="dropZone">
    <div class="drop-zone-icon"><svg class="icon icon-xl"><use href="#i-upload"/></svg></div>
    <div class="drop-zone-title">拖拽文件到此处上传</div>
    <div class="drop-zone-hint">或点击上方按钮选择文件 / 文件夹</div>
  </div>

  <div id="fileListContainer"></div>
</div>

<div class="upload-progress" id="uploadProgress">
  <div class="upload-progress-title" id="uploadTitle">上传中...</div>
  <div class="upload-progress-bar">
    <div class="upload-progress-fill" id="uploadFill"></div>
  </div>
</div>

<div class="preview-overlay" id="previewOverlay">
  <button class="preview-close" onclick="closePreview()"><svg class="icon icon-lg"><use href="#i-close"/></svg></button>
  <div class="preview-title" id="previewTitle"></div>
  <button class="preview-nav prev" id="previewPrev" onclick="navigatePreview(-1)"><svg class="icon icon-lg"><use href="#i-left"/></svg></button>
  <button class="preview-nav next" id="previewNext" onclick="navigatePreview(1)"><svg class="icon icon-lg"><use href="#i-right"/></svg></button>
  <div class="preview-content" id="previewContent"></div>
</div>

<div class="toast" id="toast"></div>

<script>
const ICONS = {
  folder: '#i-folder',
  image: '#i-image',
  video: '#i-video',
  audio: '#i-audio',
  zip: '#i-zip',
  code: '#i-code',
  text: '#i-text',
  file: '#i-file',
};

const IMG_EXTS = ['jpg','jpeg','png','gif','bmp','webp','svg','ico'];
const VIDEO_EXTS = ['mp4','webm','mov','ogg'];
const AUDIO_EXTS = ['mp3','wav','flac','aac','ogg','wma','m4a'];
const PREVIEW_EXTS = [...IMG_EXTS, ...VIDEO_EXTS, ...AUDIO_EXTS];

function getExt(name) { return name.split('.').pop().toLowerCase(); }

function getFileIcon(name, isFolder) {
  if (isFolder) return ICONS.folder;
  const ext = getExt(name);
  if (IMG_EXTS.includes(ext)) return ICONS.image;
  if (VIDEO_EXTS.includes(ext)) return ICONS.video;
  if (AUDIO_EXTS.includes(ext)) return ICONS.audio;
  if (ext === 'pdf') return ICONS.text;
  if (['zip','rar','7z','tar','gz'].includes(ext)) return ICONS.zip;
  if (['js','ts','py','cs','java','cpp','c','h','html','css','json','xml'].includes(ext)) return ICONS.code;
  if (['txt','md','log','ini','cfg','yaml','yml'].includes(ext)) return ICONS.text;
  return ICONS.file;
}

function formatSize(bytes) {
  if (bytes === 0) return '-';
  if (bytes < 1024) return bytes + ' B';
  if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
  if (bytes < 1024 * 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
  return (bytes / (1024 * 1024 * 1024)).toFixed(2) + ' GB';
}

function formatTime(iso) {
  if (!iso) return '-';
  const d = new Date(iso);
  const pad = n => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth()+1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

// The access secret travels only in the URL fragment, never in HTTP URLs/referrers.
const shareSession = (async () => {
  const token = new URLSearchParams(location.hash.slice(1)).get('access');
  history.replaceState(null, '', location.pathname);
  if (token) {
    const response = await fetch('/api/session', { method: 'POST',
      headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ token }) });
    if (!response.ok) throw new Error('分享链接已失效，请重新从电脑复制');
  }
  document.getElementById('qrImage').src = '/qr';
})();
async function fetchApi(path, opts = {}) {
  await shareSession;
  const res = await fetch(path, opts);
  if (!res.ok) throw new Error(res.status === 401 ? '请使用电脑上复制的完整分享链接' : '操作失败：' + res.status);
  return res.json();
}

let _shareUrl = '';
let _allFiles = [];
let _currentPath = '';

async function loadInfo() {
  try {
    const info = await fetchApi('/api/info');
    _shareUrl = info.url || '';
    document.getElementById('shareUrl').textContent = _shareUrl || '未获取到地址';
  } catch {
    document.getElementById('shareUrl').textContent = '获取地址失败';
  }
}

function copyUrl() {
  if (!_shareUrl) return;
  navigator.clipboard.writeText(_shareUrl).then(() => showToast('已复制到剪贴板'));
}

function openInBrowser() {
  if (!_shareUrl) return;
  window.open(_shareUrl, '_blank');
}

document.getElementById('shareUrl').addEventListener('click', copyUrl);

function escHtml(s) {
  const d = document.createElement('div');
  d.textContent = s;
  return d.innerHTML;
}

function escAttr(s) {
  return s.replace(/\\/g, '\\\\').replace(/'/g, "\\'")
    .replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/\r/g, '\\r').replace(/\n/g, '\\n');
}

function renderBreadcrumb() {
  const container = document.getElementById('breadcrumbContainer');
  if (!_currentPath) {
    container.innerHTML = '';
    return;
  }
  const parts = _currentPath.split('/');
  let html = '<div class="breadcrumb">';
  html += `<span class="breadcrumb-item" onclick="navigateTo('')">根目录</span>`;
  for (let i = 0; i < parts.length; i++) {
    const path = parts.slice(0, i + 1).join('/');
    const isCurrent = i === parts.length - 1;
    html += `<span class="breadcrumb-sep">/</span>`;
    html += `<span class="breadcrumb-item${isCurrent ? ' current' : ''}" ${isCurrent ? '' : `onclick="navigateTo('${escAttr(path)}')"`}>${escHtml(parts[i])}</span>`;
  }
  html += '</div>';
  container.innerHTML = html;
}

function navigateTo(path) {
  _currentPath = path;
  renderCurrentDir();
  renderBreadcrumb();
}

function renderCurrentDir() {
  const container = document.getElementById('fileListContainer');
  const countEl = document.getElementById('fileCount');

  const prefix = _currentPath ? _currentPath + '/' : '';
  const items = [];

  const seenDirs = new Set();

  for (const f of _allFiles) {
    if (!f.RelativePath.startsWith(prefix)) continue;
    const rest = f.RelativePath.slice(prefix.length);
    if (!rest) continue;
    const slashIdx = rest.indexOf('/');
    if (slashIdx >= 0) {
      const dirName = rest.slice(0, slashIdx);
      const dirPath = prefix + dirName;
      if (!seenDirs.has(dirPath)) {
        seenDirs.add(dirPath);
        items.push({ Name: dirName, RelativePath: dirPath, IsFolder: true, Size: 0, LastModified: '' });
      }
    } else {
      items.push({ Name: rest, RelativePath: f.RelativePath, IsFolder: false, Size: f.Size, LastModified: f.LastModified });
    }
  }

  if (!items.length && !_currentPath) {
    container.innerHTML = `
      <div class="empty-state">
        <div class="empty-state-icon"><svg class="icon icon-xl"><use href="#i-file"/></svg></div>
        <div class="empty-state-text">暂无共享文件，拖拽或选择文件开始分享</div>
      </div>`;
    countEl.textContent = '';
    return;
  }

  if (!items.length && _currentPath) {
    container.innerHTML = `
      <div class="empty-state">
        <div class="empty-state-icon"><svg class="icon icon-xl"><use href="#i-folder"/></svg></div>
        <div class="empty-state-text">此文件夹为空</div>
      </div>`;
    countEl.textContent = '';
    return;
  }

  countEl.textContent = `${items.length} 个项目`;

  const sorted = items.sort((a, b) => {
    if (a.IsFolder && !b.IsFolder) return -1;
    if (!a.IsFolder && b.IsFolder) return 1;
    return a.Name.localeCompare(b.Name);
  });

  let html = `<div class="file-list">
    <div class="file-list-header">
      <span></span>
      <span>名称</span>
      <span>大小</span>
      <span>修改时间</span>
      <span></span>
    </div>`;

  for (const f of sorted) {
    const iconRef = getFileIcon(f.Name, f.IsFolder);
    const iconClass = f.IsFolder ? 'file-icon folder' : 'file-icon';
    const nameClass = f.IsFolder ? 'file-name folder-name' : 'file-name';
    const size = f.IsFolder ? '-' : formatSize(f.Size);
    const dlLink = f.IsFolder ? '' : `/download/${encodeURIComponent(f.RelativePath)}`;
    const ext = getExt(f.Name);
    const canPreview = !f.IsFolder && PREVIEW_EXTS.includes(ext);

    html += `<div class="file-row">
      <span class="${iconClass}"><svg class="icon icon-md"><use href="${iconRef}"/></svg></span>
      <span class="${nameClass}" ${f.IsFolder ? `onclick="navigateTo('${escAttr(f.RelativePath)}')"` : (canPreview ? `onclick="openPreview('${escAttr(f.RelativePath)}')"` : '')}>${escHtml(f.Name)}</span>
      <span class="file-size">${size}</span>
      <span class="file-time">${formatTime(f.LastModified)}</span>
      <span class="file-actions">
        ${f.IsFolder ? '' : `<button class="file-btn" title="下载" onclick="window.open('${dlLink}','_blank')"><svg class="icon icon-sm"><use href="#i-download"/></svg></button>`}
        <button class="file-btn delete" title="删除" onclick="deleteFile('${escAttr(f.RelativePath)}')"><svg class="icon icon-sm"><use href="#i-delete"/></svg></button>
      </span>
    </div>`;
  }

  html += '</div>';
  container.innerHTML = html;
}

async function refreshFiles() {
  _allFiles = await fetchApi('/api/files');
  renderCurrentDir();
  renderBreadcrumb();
}

async function deleteFile(path) {
  await fetchApi('/api/files', {
    method: 'DELETE',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ path }),
  });
  showToast('已删除');
  refreshFiles();
}

async function clearAll() {
  if (!confirm('确定要清空所有共享文件吗？此操作不可恢复。')) return;
  await fetchApi('/api/clear', { method: 'DELETE' });
  showToast('已清空');
  _currentPath = '';
  refreshFiles();
}

async function uploadFiles(fileList) {
  if (!fileList || !fileList.length) return;

  const prog = document.getElementById('uploadProgress');
  const fill = document.getElementById('uploadFill');
  const title = document.getElementById('uploadTitle');

  prog.classList.add('show');
  title.textContent = `正在上传 0/${fileList.length} ...`;
  fill.style.width = '0%';

  const formData = new FormData();
  for (let i = 0; i < fileList.length; i++) {
    const file = fileList[i];
    const relPath = file.webkitRelativePath || file.name;
    formData.append('files', file, relPath);
  }

  await shareSession;
  const xhr = new XMLHttpRequest();
  xhr.open('POST', '/api/upload');

  xhr.upload.onprogress = e => {
    if (e.lengthComputable) {
      const pct = Math.round(e.loaded / e.total * 100);
      fill.style.width = pct + '%';
      title.textContent = `正在上传 ${pct}%`;
    }
  };

  xhr.onload = () => {
    prog.classList.remove('show');
    fill.style.width = '0%';
    if (xhr.status >= 200 && xhr.status < 300) {
      showToast(`已上传 ${fileList.length}个文件`);
      document.getElementById('fileInput').value = '';
      document.getElementById('folderInput').value = '';
    } else {
      let message = `上传失败（HTTP ${xhr.status}），请重试`;
      try { const result = JSON.parse(xhr.responseText); message = result.error || message; } catch {}
      showToast(message);
    }
    refreshFiles();
  };

  xhr.onerror = () => {
    prog.classList.remove('show');
    showToast('上传失败');
  };

  xhr.send(formData);

}

const dropZone = document.getElementById('dropZone');
dropZone.addEventListener('dragover', e => {
  e.preventDefault();
  dropZone.classList.add('drag-over');
});
dropZone.addEventListener('dragleave', e => {
  e.preventDefault();
  dropZone.classList.remove('drag-over');
});
dropZone.addEventListener('drop', e => {
  e.preventDefault();
  dropZone.classList.remove('drag-over');
  if (e.dataTransfer.files.length) uploadFiles(e.dataTransfer.files);
});
dropZone.addEventListener('click', () => {
  document.getElementById('fileInput').click();
});

let _previewIndex = -1;
let _previewItems = [];

function getPreviewItems() {
  const prefix = _currentPath ? _currentPath + '/' : '';
  const items = [];
  for (const f of _allFiles) {
    if (!f.RelativePath.startsWith(prefix)) continue;
    const rest = f.RelativePath.slice(prefix.length);
    if (rest.includes('/')) continue;
    const ext = getExt(rest);
    if (PREVIEW_EXTS.includes(ext)) {
      items.push({ name: rest, path: f.RelativePath, ext });
    }
  }
  return items;
}

function openPreview(path) {
  _previewItems = getPreviewItems();
  _previewIndex = _previewItems.findIndex(i => i.path === path);
  if (_previewIndex < 0) _previewIndex = 0;
  showPreviewAt(_previewIndex);
}

function showPreviewAt(idx) {
  if (idx < 0 || idx >= _previewItems.length) return;
  _previewIndex = idx;
  const item = _previewItems[idx];
  const overlay = document.getElementById('previewOverlay');
  const content = document.getElementById('previewContent');
  const title = document.getElementById('previewTitle');
  const prevBtn = document.getElementById('previewPrev');
  const nextBtn = document.getElementById('previewNext');

  title.textContent = item.name;
  content.innerHTML = '';
  const url = `/download/${encodeURIComponent(item.path)}`;

  if (IMG_EXTS.includes(item.ext)) {
    const img = document.createElement('img');
    img.src = url;
    img.alt = item.name;
    content.appendChild(img);
  } else if (VIDEO_EXTS.includes(item.ext)) {
    const video = document.createElement('video');
    video.src = url;
    video.controls = true;
    video.autoplay = true;
    content.appendChild(video);
  } else if (AUDIO_EXTS.includes(item.ext)) {
    const audio = document.createElement('audio');
    audio.src = url;
    audio.controls = true;
    audio.autoplay = true;
    content.appendChild(audio);
  }

  prevBtn.style.display = idx > 0 ? 'flex' : 'none';
  nextBtn.style.display = idx < _previewItems.length - 1 ? 'flex' : 'none';
  overlay.classList.add('show');
}

function navigatePreview(dir) {
  showPreviewAt(_previewIndex + dir);
}

function closePreview() {
  const overlay = document.getElementById('previewOverlay');
  const content = document.getElementById('previewContent');
  overlay.classList.remove('show');
  content.innerHTML = '';
}

document.addEventListener('keydown', e => {
  if (!document.getElementById('previewOverlay').classList.contains('show')) return;
  if (e.key === 'Escape') closePreview();
  if (e.key === 'ArrowLeft') navigatePreview(-1);
  if (e.key === 'ArrowRight') navigatePreview(1);
});

function showToast(msg) {
  const t = document.getElementById('toast');
  t.textContent = msg;
  t.classList.add('show');
  setTimeout(() => t.classList.remove('show'), 2500);
}

let _lastFileHash = '';

async function pollFiles() {
  try {
    const files = await fetchApi('/api/files');
    const hash = files.map(f => f.RelativePath + '|' + f.Size + '|' + f.LastModified).join(',');
    if (_lastFileHash && _lastFileHash !== hash) {
      _allFiles = files;
      renderCurrentDir();
      renderBreadcrumb();
      if (files.length > 0) showToast('文件列表已更新');
    }
    _lastFileHash = hash;
    if (!_allFiles.length) _allFiles = files;
  } catch {}
}

setInterval(pollFiles, 3000);

loadInfo();
refreshFiles().catch(e => showToast(e.message));
</script>
</body>
</html>
""";
}
