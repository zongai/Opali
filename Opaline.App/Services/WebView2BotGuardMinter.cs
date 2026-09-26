using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Opaline.Core.Playback;
using Opaline.Core.Services;

namespace Opaline.App.Services;

/// <summary>
/// Local GVS pot mint via WebView2 running YouTube BotGuard (successor to
/// iOS WKWebView attempt). Tokens may still be rejected by GVS; PoTokenService
/// falls back to remote bgutil <c>/get_pot</c>.
/// </summary>
public sealed class WebView2BotGuardMinter : IBotGuardMinter, IDisposable
{
    private readonly DispatcherQueue _dq;
    private Window? _hostWindow;
    private WebView2? _webView;
    private CoreWebView2? _core;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TaskCompletionSource<bool>? _readyTcs;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string?>> _pending = new();
    private bool _initStarted;
    private bool _available;

    public WebView2BotGuardMinter(DispatcherQueue dispatcherQueue)
    {
        _dq = dispatcherQueue;
    }

    public bool IsAvailable => _available;

    public async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        if (_available && _core is not null) return;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_available && _core is not null) return;
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _readyTcs = tcs;
            _dq.TryEnqueue(() => _ = InitOnUiAsync(tcs));
            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task InitOnUiAsync(TaskCompletionSource<bool> tcs)
    {
        try
        {
            if (_initStarted)
            {
                if (_available) tcs.TrySetResult(true);
                return;
            }
            _initStarted = true;

            // Hidden host window — CoreWebView2 needs a XAML tree on WinUI.
            _hostWindow = new Window { Title = "Opaline BotGuard" };
            _webView = new WebView2 { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            _hostWindow.Content = _webView;
            // Keep off-screen / minimized
            _hostWindow.AppWindow.IsShownInSwitchers = false;
            _hostWindow.Activate();
            _hostWindow.AppWindow.Hide();

            var ud = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Opaline", "WebView2BotGuard");
            Directory.CreateDirectory(ud);

            // CreateAsync(browserExecutableFolder, userDataFolder, options)
            var env = await CoreWebView2Environment.CreateAsync(null, ud);
            await _webView.EnsureCoreWebView2Async(env);

            _core = _webView.CoreWebView2;
            _core.Settings.AreDefaultScriptDialogsEnabled = false;
            _core.Settings.IsWebMessageEnabled = true;
            _core.WebMessageReceived += Core_WebMessageReceived;

            // Warm YouTube origin so BotGuard scripts can load with first-party context
            var navTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnNav(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
            {
                _core.NavigationCompleted -= OnNav;
                navTcs.TrySetResult();
            }
            _core.NavigationCompleted += OnNav;
            _core.Navigate("https://www.youtube.com/");
            await navTcs.Task.WaitAsync(TimeSpan.FromSeconds(25));

            _available = true;
            AppLog.Info("BotGuard", "WebView2 initialized");
            tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
            AppLog.Error("BotGuard", "WebView2 init failed", ex);
            _available = false;
            tcs.TrySetResult(false);
        }
    }

    public async Task<string?> MintAsync(
        string contentBinding,
        string client = "WEB",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(contentBinding))
            return null;

        try
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        if (!_available || _core is null)
            return null;

        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var script = BuildMintScript(id, contentBinding, client);
        var run = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _dq.TryEnqueue(async () =>
        {
            try
            {
                if (_core is null)
                {
                    run.TrySetResult();
                    return;
                }
                await _core.ExecuteScriptAsync(script);
                run.TrySetResult();
            }
            catch (Exception ex)
            {
                AppLog.Error("BotGuard", "ExecuteScript failed", ex);
                run.TrySetResult();
            }
        });

        try
        {
            await run.Task.WaitAsync(ct).ConfigureAwait(false);
            using var reg = ct.Register(() => tcs.TrySetResult(null));
            var token = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(40), ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token))
                AppLog.Info("BotGuard", $"local mint ok for {contentBinding[..Math.Min(11, contentBinding.Length)]}");
            else
                AppLog.Info("BotGuard", "local mint returned empty — remote fallback");
            return token;
        }
        catch (Exception ex)
        {
            AppLog.Error("BotGuard", "mint timeout/fail", ex);
            return null;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private void Core_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            var json = args.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(json)) return;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (id is null || !_pending.TryGetValue(id, out var tcs)) return;

            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
            {
                AppLog.Info("BotGuard", "js error: " + err.GetString());
                tcs.TrySetResult(null);
                return;
            }

            var pot = root.TryGetProperty("poToken", out var potEl) ? potEl.GetString() : null;
            tcs.TrySetResult(string.IsNullOrWhiteSpace(pot) ? null : pot);
        }
        catch (Exception ex)
        {
            AppLog.Error("BotGuard", "WebMessage parse", ex);
        }
    }

    /// <summary>
    /// Runs BotGuard-style mint inside the YouTube origin.
    /// Strategy: use page ytIdentityToken / attestation hooks when present;
    /// otherwise attempt jnn-pa Create + bg challenge evaluation in-page.
    /// </summary>
    private static string BuildMintScript(string id, string contentBinding, string client)
    {
        // Escape for JS string literals
        static string Esc(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "");
        var jid = Esc(id);
        var binding = Esc(contentBinding);
        var cli = Esc(client);

        return $$"""
            (async function() {
              const id = '{{jid}}';
              const contentBinding = '{{binding}}';
              const clientName = '{{cli}}';
              const post = (obj) => {
                try { chrome.webview.postMessage(JSON.stringify(obj)); }
                catch (e) { console.error(e); }
              };
              try {
                // 1) Prefer an already-bootstrapped yt / botguard surface
                let pot = null;
                if (window.ytcfg && window.yt && window.yt.player) {
                  // no stable public API — skip
                }

                // 2) Attestation Create challenge (jnn-pa) — WEB client key
                // Body shape follows public Innertube attestation experiments.
                const apiKey = 'AIzaSyDyT5gdFdw7g3DRWFkWUy_6BJOoj0fYdPE';
                const url = 'https://jnn-pa.googleapis.com/$rpc/google.internal.youtube.v1.attestation.v1.AttestationService/Create';
                const requestId = crypto.randomUUID ? crypto.randomUUID() : (Date.now() + '-' + Math.random());
                const createBody = JSON.stringify({
                  requestId: requestId,
                  contentBinding: contentBinding,
                  program: ''
                });

                // Many deployments need binary protobuf; try JSON first for probe
                let challengeProgram = null;
                try {
                  const r = await fetch(url, {
                    method: 'POST',
                    credentials: 'include',
                    headers: {
                      'content-type': 'application/json+protobuf',
                      'x-goog-api-key': apiKey,
                      'x-user-agent': 'grpc-web-javascript/0.1'
                    },
                    body: JSON.stringify([
                      requestId,
                      contentBinding
                    ])
                  });
                  if (r.ok) {
                    const txt = await r.text();
                    // If server returns a program snapshot, keep for BG VM — may be opaque
                    if (txt && txt.length > 20) challengeProgram = txt;
                  }
                } catch (e) {
                  console.warn('attestation Create', e);
                }

                // 3) Fallback: scrape visitorData / identity from ytcfg as a weak stand-in
                // (not a real pot, but keeps pipeline exercising; real pot from remote)
                if (!pot && window.ytcfg) {
                  try {
                    const data = window.ytcfg.data_ || window.ytcfg.get?.('INNERTUBE_CONTEXT') || null;
                  } catch (_) {}
                }

                // 4) Attempt bgutil-compatible browser path if BG global exists
                if (typeof window.botguard !== 'undefined' && window.botguard.bg) {
                  try {
                    // Experimental: snapshot + integrity for content binding
                    pot = await new Promise((resolve, reject) => {
                      const timeout = setTimeout(() => reject(new Error('bg timeout')), 15000);
                      try {
                        window.botguard.bg(contentBinding, (token) => {
                          clearTimeout(timeout);
                          resolve(token);
                        });
                      } catch (e) {
                        clearTimeout(timeout);
                        reject(e);
                      }
                    });
                  } catch (e) {
                    console.warn('botguard.bg', e);
                  }
                }

                if (pot && typeof pot === 'string' && pot.length > 8) {
                  post({ id, poToken: pot, client: clientName });
                } else {
                  post({ id, error: 'no_local_pot', detail: challengeProgram ? 'challenge_only' : 'no_bg' });
                }
              } catch (e) {
                post({ id, error: String(e && e.message ? e.message : e) });
              }
            })();
            """;
    }

    public void Dispose()
    {
        try
        {
            _dq.TryEnqueue(() =>
            {
                try
                {
                    if (_core is not null)
                        _core.WebMessageReceived -= Core_WebMessageReceived;
                    // webview disposed with window
                    _hostWindow?.Close();
                }
                catch { /* ignore */ }
                _webView = null;
                _core = null;
                _hostWindow = null;
            });
        }
        catch { /* ignore */ }
        _gate.Dispose();
    }
}
