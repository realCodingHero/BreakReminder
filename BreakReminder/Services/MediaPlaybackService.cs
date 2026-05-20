using System.Diagnostics;
using System.Windows.Threading;
using Windows.Media.Control;

namespace BreakReminder.Services;

/// <summary>
/// 使用 Windows SMTC（系统媒体传输控件）检测当前是否有媒体正在播放。
/// 结合事件订阅和定时轮询两种机制，确保可靠检测。
/// </summary>
public sealed class MediaPlaybackService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private readonly DispatcherTimer _pollTimer;
    private readonly List<GlobalSystemMediaTransportControlsSession> _subscribedSessions = new();
    private bool _disposed;
    private bool _isMediaPlaying;

    /// <summary>是否有媒体正在播放</summary>
    public bool IsMediaPlaying
    {
        get => _isMediaPlaying;
        private set
        {
            if (_isMediaPlaying == value) return;
            _isMediaPlaying = value;
            MediaPlaybackChanged?.Invoke(value);
        }
    }

    /// <summary>当媒体播放状态发生变化时触发</summary>
    public event Action<bool>? MediaPlaybackChanged;

    public MediaPlaybackService()
    {
        // 每 3 秒轮询一次作为备用机制
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _pollTimer.Tick += (_, _) => RefreshPlaybackState();
    }

    /// <summary>
    /// 异步初始化：获取 SMTC 会话管理器并订阅事件。
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _sessionManager.SessionsChanged += OnSessionsChanged;

            SubscribeToCurrentSessions();
            RefreshPlaybackState();

            _pollTimer.Start();

            Debug.WriteLine("[MediaPlaybackService] Initialized successfully.");
        }
        catch (Exception ex)
        {
            // SMTC 在某些环境（如远程桌面或没有音频设备）下可能不可用
            Debug.WriteLine($"[MediaPlaybackService] Initialization failed: {ex.Message}");
            IsMediaPlaying = false;
        }
    }

    // ======================================================================
    //  Event handlers
    // ======================================================================

    /// <summary>会话列表变化时重新订阅</summary>
    private void OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        // 需要回到 UI 线程以安全操作
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            SubscribeToCurrentSessions();
            RefreshPlaybackState();
        });
    }

    /// <summary>单个会话的播放状态变化</summary>
    private void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(RefreshPlaybackState);
    }

    // ======================================================================
    //  Core logic
    // ======================================================================

    /// <summary>
    /// 取消之前的订阅，订阅当前所有活跃会话的 PlaybackInfoChanged 事件。
    /// </summary>
    private void SubscribeToCurrentSessions()
    {
        // 取消旧订阅
        foreach (var session in _subscribedSessions)
        {
            try { session.PlaybackInfoChanged -= OnPlaybackInfoChanged; }
            catch { /* session may already be invalid */ }
        }
        _subscribedSessions.Clear();

        if (_sessionManager is null) return;

        var sessions = _sessionManager.GetSessions();
        foreach (var session in sessions)
        {
            try
            {
                session.PlaybackInfoChanged += OnPlaybackInfoChanged;
                _subscribedSessions.Add(session);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaPlaybackService] Failed to subscribe to session: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 检查所有会话，判断是否有任何媒体正在播放。
    /// </summary>
    private void RefreshPlaybackState()
    {
        if (_sessionManager is null)
        {
            IsMediaPlaying = false;
            return;
        }

        try
        {
            var sessions = _sessionManager.GetSessions();
            bool anyPlaying = false;

            foreach (var session in sessions)
            {
                try
                {
                    var info = session.GetPlaybackInfo();
                    if (info.PlaybackStatus ==
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        anyPlaying = true;
                        break;
                    }
                }
                catch
                {
                    // 忽略已失效的会话
                }
            }

            IsMediaPlaying = anyPlaying;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MediaPlaybackService] Error refreshing playback state: {ex.Message}");
            IsMediaPlaying = false;
        }
    }

    // ======================================================================
    //  IDisposable
    // ======================================================================

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _pollTimer.Stop();

        // 取消会话事件订阅
        foreach (var session in _subscribedSessions)
        {
            try { session.PlaybackInfoChanged -= OnPlaybackInfoChanged; }
            catch { /* ignore */ }
        }
        _subscribedSessions.Clear();

        if (_sessionManager is not null)
        {
            _sessionManager.SessionsChanged -= OnSessionsChanged;
            _sessionManager = null;
        }

        Debug.WriteLine("[MediaPlaybackService] Disposed.");
    }
}
