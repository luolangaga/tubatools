// Report module/WebGL failures to the native fallback, including failure before scene initialization.
let failed = false;
function fail() { if (!failed) { failed = true; const hint=document.getElementById('loading'); if(hint)hint.textContent = '3D 无法启动，请切换 2D 备用版'; window.chrome?.webview?.postMessage({ type: 'error' }); } }
addEventListener('error', fail);
addEventListener('unhandledrejection', fail);
const script = document.createElement('script'); script.src = 'scene.bundle.js'; script.onerror = fail; document.head.append(script);
