// Poll for SENTINEL in body text (up to SENTINEL_TIMEOUT_MS), then snapshot.
// Sends progress events via the background worker to the harness's loopback
// listener (port passed in via #harness_port=NNNN in the URL hash).

(async () => {
  const SENTINEL = 'Turn on extra usage to keep using Claude if you hit a limit';
  const SENTINEL_TIMEOUT_MS = 30000;
  const POLL_INTERVAL_MS = 250;
  const POST_SENTINEL_DELAY_MS = 2000;

  const portMatch = location.hash.match(/harness_port=(\d+)/);
  const harnessPort = portMatch ? parseInt(portMatch[1], 10) : null;

  const progress = (stage) => {
    if (!harnessPort) return;
    try { chrome.runtime.sendMessage({ type: 'progress', stage, port: harnessPort }); } catch (_) {}
  };

  progress('page opened');

  const pageContains = (needle) =>
    (document.body && document.body.textContent || '').includes(needle);

  const started = Date.now();
  let sentinelSeen = false;
  while (!(sentinelSeen = pageContains(SENTINEL))) {
    if (Date.now() - started > SENTINEL_TIMEOUT_MS) break;
    await new Promise(r => setTimeout(r, POLL_INTERVAL_MS));
  }
  progress(sentinelSeen ? 'content loaded' : 'sentinel timed out');

  await new Promise(r => setTimeout(r, POST_SENTINEL_DELAY_MS));

  const doctype = document.doctype ? `<!DOCTYPE ${document.doctype.name}>\n` : '';
  const html = doctype + document.documentElement.outerHTML;
  progress('HTML captured');

  chrome.runtime.sendMessage({
    type: 'snapshot',
    url: location.href,
    sentinelSeen,
    html
  });
})();
