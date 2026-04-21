// Saves the snapshot HTML under Downloads/page-grabber/<host>/<path>-<ts>.html
// then closes the sender's window.

const slugify = (s) => s.replace(/[^a-zA-Z0-9._-]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 80);

const buildFilename = (urlStr, sentinelSeen) => {
  const u = new URL(urlStr);
  const pathSlug = slugify(u.pathname) || 'root';
  const ts = new Date().toISOString().replace(/[:.]/g, '-');
  const suffix = sentinelSeen === false ? '-NOSENTINEL' : '';
  return `page-grabber/${u.hostname}/${pathSlug}-${ts}${suffix}.html`;
};

const waitForDownload = (downloadId) => new Promise((resolve) => {
  const on = (d) => {
    if (d.id !== downloadId || !d.state) return;
    if (d.state.current === 'complete' || d.state.current === 'interrupted') {
      chrome.downloads.onChanged.removeListener(on);
      resolve(d.state.current);
    }
  };
  chrome.downloads.onChanged.addListener(on);
});

const reportProgress = (port, stage) => {
  if (!port || !stage) return;
  const url = `http://localhost:${port}/progress/${encodeURIComponent(stage)}`;
  try { fetch(url, { method: 'POST', keepalive: true }).catch(() => {}); } catch (_) {}
};

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (msg && msg.type === 'progress') {
    reportProgress(msg.port, msg.stage);
    sendResponse && sendResponse({ ok: true });
    return false;
  }
  if (msg && msg.type === 'snapshot') {
    (async () => {
      try {
        const id = await chrome.downloads.download({
          url: 'data:text/html;charset=utf-8,' + encodeURIComponent(msg.html),
          filename: buildFilename(msg.url, msg.sentinelSeen),
          conflictAction: 'uniquify',
          saveAs: false
        });
        await waitForDownload(id);
      } catch (e) {
        console.error('snapshot save failed', e);
      } finally {
        const winId = sender && sender.tab && sender.tab.windowId;
        if (winId != null) {
          try { await chrome.windows.remove(winId); } catch (_) {}
        }
      }
    })();
    sendResponse && sendResponse({ ok: true });
  }
  return false;
});
