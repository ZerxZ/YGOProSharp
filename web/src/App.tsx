import React, { useEffect, useState, useCallback, useRef } from 'react';
import {
  Activity,
  Database,
  Gamepad2,
  KeyRound,
  Layers,
  LogOut,
  Plus,
  RefreshCw,
  Send,
  Shield,
  ShieldAlert,
  Sparkles,
  Swords,
  Trash2,
  Upload,
  Users,
  Video,
  Copy,
  Check,
  Eye,
  EyeOff,
  Lock
} from 'lucide-react';
import { ApiError, apiFetch, AuthUser, hasPerm, openAuthenticatedWebSocket, setCsrfToken } from './api';
import { SurfaceInfo, UserPortalApp } from './PortalView';
import { BanlistOption, QuickCopyPill, RoomConfigurator } from './RoomConfigurator';

type TabKey = 'dashboard' | 'rooms' | 'studio' | 'mcp' | 'security';

export default function App() {
  const [pathname, setPathname] = useState<string>(() => window.location.pathname || '/');
  const [surface, setSurface] = useState<SurfaceInfo | null>(null);
  const [surfaceChecked, setSurfaceChecked] = useState(false);
  const [user, setUser] = useState<AuthUser | null>(null);
  const [adminAuthChecked, setAdminAuthChecked] = useState(false);
  const [activeTab, setActiveTab] = useState<TabKey>('dashboard');
  const [toast, setToast] = useState<{ text: string; kind: 'ok' | 'err' } | null>(null);

  const isAdminRoute = pathname === '/admin' || pathname.startsWith('/admin/');

  const navigateTo = useCallback((nextPath: string) => {
    if (window.location.pathname !== nextPath) {
      window.history.pushState({}, '', nextPath);
    }
    setPathname(nextPath);
  }, []);

  useEffect(() => {
    const onPopState = () => setPathname(window.location.pathname || '/');
    window.addEventListener('popstate', onPopState);
    return () => window.removeEventListener('popstate', onPopState);
  }, []);

  const notify = useCallback((text: string, kind: 'ok' | 'err' = 'ok') => {
    setToast({ text, kind });
    setTimeout(() => setToast((cur) => (cur?.text === text ? null : cur)), 3800);
  }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const sf = await apiFetch<SurfaceInfo>('/api/surface').catch(() => ({
        surface: 'admin' as const,
        unifiedPortal: true,
        defaultPage: 'portal',
        adminPath: '/admin',
        gamePort: 7911
      }));
      if (!cancelled) {
        setSurface(sf);
        setSurfaceChecked(true);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const refreshMe = useCallback(async () => {
    try {
      const res = await apiFetch<{ user: AuthUser }>('/api/v1/auth/me');
      setCsrfToken(res.user.csrfToken);
      setUser(res.user);
    } catch {
      setCsrfToken('');
      setUser(null);
    } finally {
      setAdminAuthChecked(true);
    }
  }, []);

  const shouldRenderPortal =
    surface?.surface === 'portal' || (!isAdminRoute && surface?.unifiedPortal !== false);

  useEffect(() => {
    if (!surfaceChecked || shouldRenderPortal) return;
    refreshMe();
  }, [surfaceChecked, shouldRenderPortal, refreshMe]);

  const handleLogout = async () => {
    try {
      await apiFetch('/api/v1/auth/logout', { method: 'POST', body: '{}' });
    } catch {
      // ignore
    }
    setCsrfToken('');
    setUser(null);
  };

  if (!surfaceChecked) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-slate-950 text-slate-400">
        正在加载页面...
      </div>
    );
  }

  if (shouldRenderPortal && surface) {
    return <UserPortalApp surfaceInfo={surface} />;
  }

  if (!adminAuthChecked) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-slate-950 text-slate-400">
        正在验证管理控制面会话...
      </div>
    );
  }

  if (!user) {
    return (
      <LoginScreen
        onBackToPortal={surface?.unifiedPortal !== false ? () => navigateTo('/') : undefined}
        onLoggedIn={(u) => {
          setCsrfToken(u.csrfToken);
          setUser(u);
        }}
      />
    );
  }

  if (user.mustChangePassword) {
    return (
      <ForcePasswordChangeScreen
        user={user}
        onChanged={(updated) => {
          if (updated) {
            setCsrfToken(updated.csrfToken);
            setUser(updated);
          } else {
            refreshMe();
          }
          notify('账号与密码设置已生效，欢迎进入管理控制台。');
        }}
        onLogout={handleLogout}
      />
    );
  }

  const tabs: { key: TabKey; label: string; icon: React.ReactNode; show: boolean }[] = [
    { key: 'dashboard', label: '总览与缓存', icon: <Activity className="w-4 h-4" />, show: hasPerm(user, 'dashboard:view') },
    { key: 'rooms', label: '房间 / 裁判 / 录像', icon: <Gamepad2 className="w-4 h-4" />, show: hasPerm(user, 'room:view') },
    {
      key: 'studio',
      label: '卡组池 & DIY 工作台',
      icon: <Layers className="w-4 h-4" />,
      show: hasPerm(user, 'deck_pool:view') || hasPerm(user, 'diy:view') || hasPerm(user, 'card:search')
    },
    {
      key: 'mcp',
      label: 'MCP 接入中心',
      icon: <KeyRound className="w-4 h-4" />,
      show: hasPerm(user, 'mcp_token:issue_self') || hasPerm(user, 'mcp_token:manage_all')
    },
    { key: 'security', label: '用户 / 权限 / 封禁 / 审计', icon: <Shield className="w-4 h-4" />, show: true }
  ];

  return (
    <div className="min-h-screen bg-slate-950 text-slate-100 flex flex-col">
      <header className="border-b border-slate-800 bg-slate-900/80 backdrop-blur sticky top-0 z-30">
        <div className="max-w-7xl mx-auto px-4 h-14 flex items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <div className="w-8 h-8 rounded-lg bg-indigo-600 flex items-center justify-center font-bold text-white shadow">
              Y
            </div>
            <div>
              <div className="font-semibold text-sm tracking-wide flex items-center gap-2">
                YGOProSharp 管理后台
                <span className="px-1.5 py-0.5 rounded text-[10px] font-mono bg-indigo-500/20 text-indigo-300 border border-indigo-500/30">
                  /admin · :{window.location.port || surface?.adminPort || 7912}
                </span>
                {surface?.unifiedPortal !== false ? (
                  <button
                    type="button"
                    onClick={() => navigateTo('/')}
                    className="px-2 py-0.5 rounded text-[11px] bg-emerald-500/20 text-emerald-300 border border-emerald-500/30 hover:bg-emerald-500/30 cursor-pointer"
                  >
                    返回前台玩家大厅 (/)
                  </button>
                ) : (
                  surface?.portalPort && (
                    <a
                      href={`${window.location.protocol}//${window.location.hostname}:${surface.portalPort}`}
                      target="_blank"
                      rel="noreferrer"
                      className="px-1.5 py-0.5 rounded text-[10px] font-mono bg-emerald-500/20 text-emerald-300 border border-emerald-500/30 hover:bg-emerald-500/30"
                    >
                      打开玩家面板 :{surface.portalPort} ↗
                    </a>
                  )
                )}
                {user.isStreamerOnly && (
                  <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-xs bg-amber-500/20 text-amber-300 border border-amber-500/30">
                    <EyeOff className="w-3 h-3" /> 直播脱敏模式
                  </span>
                )}
              </div>
              <div className="text-xs text-slate-400">
                游戏服 :{surface?.gamePort ?? 7911} · 默认路径 `/` 玩家大厅 · `/admin` 管理控制面
              </div>
            </div>
          </div>

          <nav className="flex items-center gap-1 overflow-x-auto">
            {tabs
              .filter((t) => t.show)
              .map((t) => (
                <button
                  key={t.key}
                  onClick={() => setActiveTab(t.key)}
                  className={`px-3 py-1.5 rounded-md text-xs font-medium flex items-center gap-1.5 transition cursor-pointer ${
                    activeTab === t.key
                      ? 'bg-indigo-600 text-white shadow-sm'
                      : 'text-slate-300 hover:bg-slate-800 hover:text-white'
                  }`}
                >
                  {t.icon}
                  {t.label}
                </button>
              ))}
          </nav>

          <div className="flex items-center gap-3">
            <div className="text-right hidden sm:block">
              <div className="text-xs font-medium text-slate-200">{user.displayName}</div>
              <div className="text-[11px] text-slate-400">{user.roleCodes.join(', ')}</div>
            </div>
            <button
              onClick={handleLogout}
              className="p-2 rounded-md text-slate-400 hover:text-rose-400 hover:bg-slate-800 transition cursor-pointer"
              title="退出登录"
            >
              <LogOut className="w-4 h-4" />
            </button>
          </div>
        </div>
      </header>

      {toast && (
        <div
          className={`fixed bottom-5 right-5 z-50 px-4 py-2.5 rounded-lg shadow-xl border text-sm font-medium ${
            toast.kind === 'ok'
              ? 'bg-emerald-950/95 border-emerald-500/40 text-emerald-200'
              : 'bg-rose-950/95 border-rose-500/40 text-rose-200'
          }`}
        >
          {toast.text}
        </div>
      )}

      <main className="max-w-7xl w-full mx-auto px-4 py-6 flex-1">
        {activeTab === 'dashboard' && <DashboardView user={user} notify={notify} />}
        {activeTab === 'rooms' && <RoomsView user={user} notify={notify} />}
        {activeTab === 'studio' && <StudioView user={user} notify={notify} />}
        {activeTab === 'mcp' && <McpHubView user={user} notify={notify} />}
        {activeTab === 'security' && <SecurityView user={user} notify={notify} onRefreshMe={refreshMe} />}
      </main>
    </div>
  );
}

function LoginScreen({
  onLoggedIn,
  onBackToPortal
}: {
  onLoggedIn: (u: AuthUser) => void;
  onBackToPortal?: () => void;
}) {
  const [username, setUsername] = useState('admin');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      const res = await apiFetch<{ user: AuthUser; csrfToken: string }>('/api/v1/auth/login', {
        method: 'POST',
        body: JSON.stringify({ username, password })
      });
      setCsrfToken(res.csrfToken);
      onLoggedIn(res.user);
    } catch (err) {
      setError(err instanceof Error ? err.message : '登录失败');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-950 p-4">
      <form onSubmit={submit} className="w-full max-w-md bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-2xl space-y-5">
        <div className="flex items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl bg-indigo-600 flex items-center justify-center font-bold text-lg">Y</div>
            <div>
              <h1 className="text-lg font-bold">YGOProSharp 管理控制台 (/admin)</h1>
              <p className="text-xs text-slate-400">Argon2id 认证 · 细粒度 RBAC · 零外发大模型密钥</p>
            </div>
          </div>
          {onBackToPortal && (
            <button
              type="button"
              onClick={onBackToPortal}
              className="px-2.5 py-1 rounded-lg bg-slate-800 hover:bg-slate-700 text-xs text-emerald-300 cursor-pointer shrink-0"
            >
              返回玩家页
            </button>
          )}
        </div>

        {error && (
          <div className="p-3 rounded-lg bg-rose-950/70 border border-rose-500/40 text-xs text-rose-200">{error}</div>
        )}

        <div className="space-y-3">
          <div>
            <label className="block text-xs text-slate-400 mb-1">管理员 / 成员账号</label>
            <input
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              className="w-full px-3 py-2 rounded-lg bg-slate-950 border border-slate-700 text-sm focus:outline-none focus:border-indigo-500"
              required
            />
          </div>
          <div>
            <label className="block text-xs text-slate-400 mb-1">登录密码（首次启动请查看服务端启动日志中的一次性密码）</label>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="w-full px-3 py-2 rounded-lg bg-slate-950 border border-slate-700 text-sm focus:outline-none focus:border-indigo-500"
              required
            />
          </div>
        </div>

        <button
          disabled={loading}
          className="w-full py-2.5 rounded-lg bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-sm font-semibold transition cursor-pointer"
        >
          {loading ? '正在验证...' : '安全登录'}
        </button>
      </form>
    </div>
  );
}

function ForcePasswordChangeScreen({
  user,
  onChanged,
  onLogout
}: {
  user: AuthUser;
  onChanged: (u?: AuthUser) => void;
  onLogout: () => void;
}) {
  const [currentPassword, setCurrentPassword] = useState('');
  const [customUsername, setCustomUsername] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (newPassword && newPassword.length < 6) {
      setError('密码长度至少需要 6 位字符。');
      return;
    }
    if (newPassword && newPassword !== confirmPassword) {
      setError('两次输入的新密码不一致。');
      return;
    }
    setError('');
    setLoading(true);
    try {
      const res = await apiFetch<{ user?: AuthUser }>('/api/v1/auth/change-password', {
        method: 'POST',
        body: JSON.stringify({
          currentPassword,
          newUsername: customUsername.trim() || undefined,
          newPassword: newPassword || undefined
        })
      });
      onChanged(res.user);
    } catch (err) {
      setError(err instanceof Error ? err.message : '保存设置失败');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-950 p-4">
      <form onSubmit={submit} className="w-full max-w-md bg-slate-900 border border-amber-500/40 rounded-2xl p-6 shadow-2xl space-y-4">
        <div className="flex items-center gap-2 text-amber-300 font-semibold">
          <Lock className="w-5 h-5" /> 首次安全引导：管理员账号与密码设置
        </div>
        <p className="text-xs text-slate-400">
          当前默认账号为 <span className="text-slate-200 font-mono">{user.username}</span>。可自定义管理员用户名与新密码（最少 6 位字符）；未填写的项将自动保留默认设置。
        </p>
        {error && <div className="p-3 rounded-lg bg-rose-950/70 border border-rose-500/40 text-xs text-rose-200">{error}</div>}
        <div>
          <label className="block text-xs text-slate-400 mb-1">当前初始/临时密码（必填校验）</label>
          <input
            type="password"
            placeholder="输入当前登录使用的初始密码"
            value={currentPassword}
            onChange={(e) => setCurrentPassword(e.target.value)}
            className="w-full px-3 py-2 rounded-lg bg-slate-950 border border-slate-700 text-sm"
            required
          />
        </div>
        <div>
          <label className="block text-xs text-slate-400 mb-1">自定义管理员用户名（选填，留空默认保持 {user.username}）</label>
          <input
            placeholder={`留空默认使用 ${user.username}`}
            value={customUsername}
            onChange={(e) => setCustomUsername(e.target.value)}
            className="w-full px-3 py-2 rounded-lg bg-slate-950 border border-slate-700 text-sm"
          />
        </div>
        <div>
          <label className="block text-xs text-slate-400 mb-1">自定义新密码（最少 6 位字符；选填，留空保留当前默认密码）</label>
          <input
            type="password"
            minLength={6}
            placeholder="至少 6 位字符（不填则保持当前默认密码）"
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
            className="w-full px-3 py-2 rounded-lg bg-slate-950 border border-slate-700 text-sm"
          />
        </div>
        {newPassword && (
          <div>
            <label className="block text-xs text-slate-400 mb-1">确认自定义新密码</label>
            <input
              type="password"
              minLength={6}
              placeholder="再次输入新密码"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              className="w-full px-3 py-2 rounded-lg bg-slate-950 border border-slate-700 text-sm"
              required
            />
          </div>
        )}
        <div className="flex gap-2">
          <button
            type="button"
            onClick={onLogout}
            className="px-4 py-2 rounded-lg bg-slate-800 hover:bg-slate-700 text-xs cursor-pointer"
          >
            退出
          </button>
          <button
            disabled={loading}
            className="flex-1 py-2 rounded-lg bg-amber-500 hover:bg-amber-400 text-slate-950 font-semibold text-sm cursor-pointer"
          >
            {loading ? '正在保存...' : '保存设置并进入控制台'}
          </button>
        </div>
      </form>
    </div>
  );
}

function DashboardView({ user, notify }: { user: AuthUser; notify: (t: string, k?: 'ok' | 'err') => void }) {
  const [status, setStatus] = useState<any>(null);
  const [logs, setLogs] = useState<any[]>([]);
  const [logRedacted, setLogRedacted] = useState(false);
  const [shoutText, setShoutText] = useState('');
  const [maintReason, setMaintReason] = useState('');
  const wsRef = useRef<WebSocket | null>(null);

  const load = useCallback(async () => {
    try {
      const [st, lg] = await Promise.all([
        apiFetch('/api/v1/server/status'),
        apiFetch('/api/v1/system/logs?limit=120')
      ]);
      setStatus(st);
      setLogs(lg.entries || []);
      setLogRedacted(Boolean(lg.redactedForStreamer));
    } catch (err) {
      notify(err instanceof Error ? err.message : '加载总览失败', 'err');
    }
  }, [notify]);

  useEffect(() => {
    load();
    let active = true;
    openAuthenticatedWebSocket((evt) => {
      if (!active) return;
      if (evt.type === 'log.entry') {
        setLogs((prev) => [...prev.slice(-149), evt.data]);
      } else if (evt.type === 'cache.stats') {
        setStatus((prev: any) => (prev ? { ...prev, cache: evt.data } : prev));
      } else if (evt.type === 'room.created' || evt.type === 'room.updated' || evt.type === 'diy.reloaded') {
        load();
      }
    })
      .then((ws) => {
        wsRef.current = ws;
      })
      .catch(() => {});

    return () => {
      active = false;
      wsRef.current?.close();
    };
  }, [load]);

  const toggleMaintenance = async () => {
    if (!status) return;
    try {
      await apiFetch('/api/v1/server/maintenance', {
        method: 'POST',
        body: JSON.stringify({ enabled: !status.maintenanceMode, reason: maintReason })
      });
      notify(!status.maintenanceMode ? '已开启维护模式' : '已解除维护模式');
      load();
    } catch (err) {
      notify(err instanceof Error ? err.message : '操作失败', 'err');
    }
  };

  const sendBroadcast = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!shoutText.trim()) return;
    try {
      await apiFetch('/api/v1/server/broadcast', {
        method: 'POST',
        body: JSON.stringify({ message: shoutText })
      });
      setShoutText('');
      notify('全服黄字广播已发送');
    } catch (err) {
      notify(err instanceof Error ? err.message : '广播失败', 'err');
    }
  };

  const flushCache = async (ns?: string) => {
    try {
      await apiFetch('/api/v1/system/cache/flush', {
        method: 'POST',
        body: JSON.stringify({ namespace: ns || null })
      });
      notify(`已清理缓存 (${ns || '全部命名空间'})`);
      load();
    } catch (err) {
      notify(err instanceof Error ? err.message : '清理缓存失败', 'err');
    }
  };

  const triggerReload = async () => {
    try {
      const res = await apiFetch('/api/v1/diy/reload', { method: 'POST', body: '{}' });
      notify(`热重载完成：卡库代数 v${res.reload.catalogVersion}，盲盒池 ${res.reload.presetDeckCount} 套`);
      load();
    } catch (err) {
      notify(err instanceof Error ? err.message : '热重载失败', 'err');
    }
  };

  if (!status) {
    return <div className="text-sm text-slate-400">正在读取服务器运行指标...</div>;
  }

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <MetricCard
          title="游戏服务状态"
          value={status.maintenanceMode ? '维护拦截中' : status.isRunning ? `运行中 :${status.port}` : '已停止'}
          sub={`在线连接 ${status.clientCount} · 活动房间 ${status.roomCount}`}
          accent={status.maintenanceMode ? 'text-amber-400' : 'text-emerald-400'}
        />
        <MetricCard
          title="运营数据库健康"
          value={`${status.database.provider} (${status.database.healthy ? '正常' : '异常'})`}
          sub={`探针耗时 ${status.database.latencyMs} ms · ${status.database.journalMode || 'relational'}`}
          accent={status.database.healthy ? 'text-sky-400' : 'text-rose-400'}
        />
        <MetricCard
          title="HybridCache 命中率"
          value={`${status.cache.hitRatioPercent}%`}
          sub={`L1:${status.cache.l1Hits} · Redis:${status.cache.l2RedisHits} · Miss:${status.cache.misses}`}
          accent="text-indigo-400"
        />
        <MetricCard
          title="卡库与随机池快照"
          value={`Catalog v${status.catalogVersion}`}
          sub={`盲盒 ${status.presetDeckCount} 套 · DIY包 ${status.enabledDiyPackageCount}/${status.diyPackageCount} · 禁限表 ${status.banlistCount}`}
          accent="text-purple-400"
        />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-3">
          <div className="flex items-center justify-between">
            <h2 className="text-sm font-semibold flex items-center gap-2">
              <Database className="w-4 h-4 text-indigo-400" /> 缓存与热重载控制
            </h2>
            <button onClick={load} className="text-xs text-slate-400 hover:text-white flex items-center gap-1 cursor-pointer">
              <RefreshCw className="w-3.5 h-3.5" /> 刷新
            </button>
          </div>
          <div className="text-xs text-slate-400 space-y-1">
            <div>Redis L2 状态：{status.cache.redisConnected ? '已连接共享缓存' : '单机 L1 内存模式（自动降级）'}</div>
            <div>平均读取延迟：{status.cache.averageLatencyMs} ms · 活跃原子票据：{status.cache.activeTickets}</div>
          </div>
          <div className="flex flex-wrap gap-2 pt-1">
            {hasPerm(user, 'diy:reload') && (
              <button
                onClick={triggerReload}
                className="px-3 py-1.5 rounded-lg bg-indigo-600 hover:bg-indigo-500 text-xs font-medium cursor-pointer"
              >
                原子热重载 DIY & 卡组池
              </button>
            )}
            {hasPerm(user, 'system:cache_flush') && (
              <>
                <button
                  onClick={() => flushCache('cards')}
                  className="px-2.5 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-xs cursor-pointer"
                >
                  清卡库缓存
                </button>
                <button
                  onClick={() => flushCache()}
                  className="px-2.5 py-1.5 rounded-lg bg-slate-800 hover:bg-rose-900/60 text-xs cursor-pointer"
                >
                  清空全部缓存
                </button>
              </>
            )}
          </div>
        </div>

        <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-3">
          <h2 className="text-sm font-semibold flex items-center gap-2">
            <Send className="w-4 h-4 text-amber-400" /> 全服黄字公告 (Shout)
          </h2>
          <form onSubmit={sendBroadcast} className="flex gap-2">
            <input
              value={shoutText}
              onChange={(e) => setShoutText(e.target.value)}
              placeholder="向所有活动房间发送黄色系统提示..."
              disabled={!hasPerm(user, 'room:shout')}
              className="flex-1 px-3 py-1.5 rounded-lg bg-slate-950 border border-slate-700 text-xs"
            />
            <button
              disabled={!hasPerm(user, 'room:shout')}
              className="px-3 py-1.5 rounded-lg bg-amber-500 hover:bg-amber-400 disabled:opacity-40 text-slate-950 font-semibold text-xs cursor-pointer"
            >
              发送
            </button>
          </form>

          {hasPerm(user, 'server:stop') && (
            <div className="pt-2 border-t border-slate-800 flex items-center gap-2">
              <input
                value={maintReason}
                onChange={(e) => setMaintReason(e.target.value)}
                placeholder="维护说明（可选）"
                className="flex-1 px-2.5 py-1 rounded bg-slate-950 border border-slate-700 text-xs"
              />
              <button
                onClick={toggleMaintenance}
                className={`px-3 py-1 rounded text-xs font-medium cursor-pointer ${
                  status.maintenanceMode ? 'bg-emerald-600 text-white' : 'bg-rose-600 text-white'
                }`}
              >
                {status.maintenanceMode ? '恢复迎客' : '停服维护'}
              </button>
            </div>
          )}
        </div>

        <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-2 text-xs text-slate-300">
          <h2 className="text-sm font-semibold text-slate-100">当前会话权限摘要</h2>
          <div>登录身份：{user.username} ({user.roleCodes.join(', ')})</div>
          <div>IP 可见权限：{user.canViewIp ? '允许查看选手 IP' : '已自动脱敏隐藏 IP'}</div>
          <div>可签发 MCP 最高角色：{user.allowedMcpRolesToIssue.join(', ') || '无'}</div>
        </div>
      </div>

      <div className="bg-slate-900 border border-slate-800 rounded-xl p-4">
        <div className="flex items-center justify-between mb-3">
          <h2 className="text-sm font-semibold">实时结构化日志流 (WebSocket)</h2>
          <span className="text-xs text-slate-400">{logs.length} 条记录</span>
        </div>
        {logRedacted ? (
          <div className="text-xs text-amber-300 py-6 text-center">
            当前处于主播只读模式 (streamer)，已自动屏蔽底层连接日志以防直播画面泄露玩家 IP。
          </div>
        ) : (
          <div className="h-64 overflow-y-auto font-mono text-[11px] bg-slate-950 rounded-lg p-3 space-y-1 border border-slate-800">
            {logs.map((l) => (
              <div key={l.sequence} className="leading-relaxed break-all">
                <span className="text-slate-500">{new Date(l.timestampUtc).toLocaleTimeString()}</span>{' '}
                <span
                  className={
                    l.level === 'Error' || l.level === 'Critical'
                      ? 'text-rose-400'
                      : l.level === 'Warning'
                        ? 'text-amber-400'
                        : 'text-indigo-400'
                  }
                >
                  [{l.level}]
                </span>{' '}
                <span className="text-slate-400">{l.category}:</span>{' '}
                <span className="text-slate-200">{l.message}</span>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

function RoomsView({ user, notify }: { user: AuthUser; notify: (t: string, k?: 'ok' | 'err') => void }) {
  const [rooms, setRooms] = useState<any[]>([]);
  const [banlists, setBanlists] = useState<BanlistOption[]>([]);
  const [botDecks, setBotDecks] = useState<string[]>([]);
  const [replays, setReplays] = useState<any[]>([]);
  const [selectedRoom, setSelectedRoom] = useState<any>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [roomChatMsg, setRoomChatMsg] = useState('');
  const [selectedBotDeck, setSelectedBotDeck] = useState('random');

  const loadRooms = useCallback(async () => {
    try {
      const res = await apiFetch('/api/v1/rooms');
      setRooms(res.rooms || []);
      setBanlists(res.banlists || []);
      setBotDecks(res.availableBotDecks || []);
      if (hasPerm(user, 'duel_log:view') || hasPerm(user, 'duel_log:view_own')) {
        const rep = await apiFetch('/api/v1/replays?limit=25');
        setReplays(rep.records || []);
      }
    } catch (err) {
      notify(err instanceof Error ? err.message : '加载房间失败', 'err');
    }
  }, [user, notify]);

  const loadRoomDetail = useCallback(
    async (roomName: string) => {
      try {
        const res = await apiFetch(`/api/v1/rooms/${encodeURIComponent(roomName)}`);
        setSelectedRoom(res.room);
      } catch (err) {
        notify(err instanceof Error ? err.message : '读取房间详情失败', 'err');
      }
    },
    [notify]
  );

  useEffect(() => {
    loadRooms();
    const timer = setInterval(loadRooms, 3000);
    return () => clearInterval(timer);
  }, [loadRooms]);

  const spawnBot = async (roomName: string, fillAll: boolean) => {
    try {
      const res = await apiFetch(`/api/v1/rooms/${encodeURIComponent(roomName)}/bots`, {
        method: 'POST',
        body: JSON.stringify({ deck: selectedBotDeck, fillAll })
      });
      notify(`已召唤 AI: ${(res.spawned || []).join(', ')}`);
      setTimeout(() => loadRoomDetail(roomName), 400);
      loadRooms();
    } catch (err) {
      notify(err instanceof Error ? err.message : '召唤 AI 失败', 'err');
    }
  };

  const toggleDeath = async (roomName: string, enable: boolean) => {
    try {
      await apiFetch(`/api/v1/rooms/${encodeURIComponent(roomName)}/death`, {
        method: 'POST',
        body: JSON.stringify({ enabled: enable, extraTurns: 4 })
      });
      notify(enable ? '已开启额外回合猝死 (ED)' : '已关闭猝死规则');
      loadRoomDetail(roomName);
    } catch (err) {
      notify(err instanceof Error ? err.message : '操作失败', 'err');
    }
  };

  const sendRoomChat = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedRoom || !roomChatMsg.trim()) return;
    const rn = selectedRoom.summary.roomName;
    try {
      await apiFetch(`/api/v1/rooms/${encodeURIComponent(rn)}/chat`, {
        method: 'POST',
        body: JSON.stringify({ message: roomChatMsg })
      });
      setRoomChatMsg('');
      loadRoomDetail(rn);
    } catch (err) {
      notify(err instanceof Error ? err.message : '发送失败', 'err');
    }
  };

  const kickSlot = async (roomName: string, slotIndex: number) => {
    try {
      await apiFetch(`/api/v1/rooms/${encodeURIComponent(roomName)}/kick`, {
        method: 'POST',
        body: JSON.stringify({ slotIndex })
      });
      notify('已踢出该席位玩家');
      loadRoomDetail(roomName);
      loadRooms();
    } catch (err) {
      notify(err instanceof Error ? err.message : '踢人失败', 'err');
    }
  };

  const kickByName = async (roomName: string, playerName: string) => {
    try {
      await apiFetch(`/api/v1/rooms/${encodeURIComponent(roomName)}/kick`, {
        method: 'POST',
        body: JSON.stringify({ playerName })
      });
      notify(`已将 ${playerName} 请出房间`);
      loadRoomDetail(roomName);
      loadRooms();
    } catch (err) {
      notify(err instanceof Error ? err.message : '踢人失败', 'err');
    }
  };

  const closeRoom = async (roomName: string) => {
    try {
      await apiFetch(`/api/v1/rooms/${encodeURIComponent(roomName)}`, { method: 'DELETE' });
      notify(`房间 ${roomName} 已关闭`);
      setSelectedRoom(null);
      loadRooms();
    } catch (err) {
      notify(err instanceof Error ? err.message : '关闭失败', 'err');
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-base font-bold flex items-center gap-2">
            <Swords className="w-5 h-5 text-indigo-400" /> 实时房间大厅 · 裁判巡房 · 斗蛐蛐围观
          </h1>
          <p className="text-xs text-slate-400">严格遵守对局战争迷雾：后台仅展示公开场面 LP、回合数与公开事件，绝不透视暗手牌与牌堆顺序</p>
        </div>
        <div className="flex gap-2">
          <button onClick={loadRooms} className="px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-xs flex items-center gap-1 cursor-pointer">
            <RefreshCw className="w-3.5 h-3.5" /> 刷新
          </button>
          {(hasPerm(user, 'room:create') || hasPerm(user, 'room:create_own')) && (
            <button
              onClick={() => setCreateOpen((v) => !v)}
              className="px-3 py-1.5 rounded-lg bg-indigo-600 hover:bg-indigo-500 text-xs font-semibold flex items-center gap-1 cursor-pointer"
            >
              <Plus className="w-4 h-4" /> {createOpen ? '收起建房面板' : '新建房间 / 选择规则配置'}
            </button>
          )}
        </div>
      </div>

      {createOpen && (
        <RoomConfigurator
          apiEndpoint="/api/v1/rooms"
          theme="indigo"
          canCreateOnServer={hasPerm(user, 'room:create') || hasPerm(user, 'room:create_own')}
          canIssueAiTokens={user.allowedMcpRolesToIssue.includes('player')}
          banlists={banlists}
          onCreated={(res) => {
            loadRooms();
            if (res?.room?.roomName) loadRoomDetail(res.room.roomName);
          }}
          onClose={() => setCreateOpen(false)}
          notify={notify}
        />
      )}

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <div className="lg:col-span-2 grid grid-cols-1 md:grid-cols-2 gap-3">
          {rooms.length === 0 && (
            <div className="col-span-2 bg-slate-900 border border-slate-800 rounded-xl p-8 text-center text-xs text-slate-400">
              当前暂无活动房间，可点击右上角「新建房间 / 选择规则配置」或从游戏客户端直接输入房号密码连入。
            </div>
          )}
          {rooms.map((r) => {
            const observers: any[] = r.observers || [];
            const copyPw = r.hostPassword || r.joinPassword;
            return (
              <div
                key={r.roomName}
                onClick={() => loadRoomDetail(r.roomName)}
                className={`bg-slate-900 border rounded-xl p-4 space-y-3 cursor-pointer transition ${
                  selectedRoom?.summary?.roomName === r.roomName ? 'border-indigo-500' : 'border-slate-800 hover:border-slate-700'
                }`}
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="font-bold text-sm truncate">{r.roomName}</span>
                  <div className="flex items-center gap-1.5 shrink-0">
                    <span className="px-2 py-0.5 rounded text-[11px] bg-indigo-500/20 text-indigo-300">
                      {r.mode === 2 ? '2v2 Tag' : r.mode === 1 ? 'Match' : 'Single'}
                    </span>
                    {r.randomDeckMode !== 'None' && (
                      <span className="px-2 py-0.5 rounded text-[11px] bg-purple-500/20 text-purple-300">{r.randomDeckMode}</span>
                    )}
                    {r.isSuddenDeath && <span className="px-2 py-0.5 rounded text-[11px] bg-rose-500/20 text-rose-300">ED</span>}
                    <span className="px-2 py-0.5 rounded text-[11px] bg-slate-800 text-slate-300">{r.state}</span>
                  </div>
                </div>

                {!user.isStreamerOnly && copyPw && (
                  <div className="flex items-center justify-between bg-slate-950 px-2.5 py-1.5 rounded border border-slate-800/80 text-xs">
                    <span className="text-slate-400 truncate">
                      主机密码: <code className="font-mono text-indigo-300 font-semibold">{copyPw}</code>
                    </span>
                    <QuickCopyPill text={copyPw} label="复制主机密码" onCopied={() => notify(`已复制主机密码：${copyPw}`)} />
                  </div>
                )}

                <div className="grid grid-cols-2 gap-2 text-xs bg-slate-950 p-2.5 rounded-lg">
                  <div>
                    <div className="text-slate-400">A 队 LP</div>
                    <div className="font-mono font-bold text-emerald-400">{r.team0Lp}</div>
                  </div>
                  <div>
                    <div className="text-slate-400">B 队 LP · T{r.turnCount}</div>
                    <div className="font-mono font-bold text-sky-400">{r.team1Lp}</div>
                  </div>
                </div>

                <div className="space-y-1 text-xs">
                  {r.slots.map((s: any) => (
                    <div key={s.seatIndex} className="flex items-center justify-between text-slate-300">
                      <span>
                        #{s.seatIndex} {s.playerName || '<空位>'}{' '}
                        {s.isHost && <span className="text-amber-400">[房主]</span>}
                      </span>
                      <span className="text-[11px] text-slate-400">
                        {s.clientIp ? `${s.clientIp} · ` : ''}
                        {s.playerName ? (s.isReady ? '已准备' : s.seatOrigin) : ''}
                      </span>
                    </div>
                  ))}
                </div>

                <div className="pt-2 border-t border-slate-800/80 flex items-center gap-1 text-[11px] text-slate-400 truncate">
                  <Eye className="w-3.5 h-3.5 text-sky-400 shrink-0" />
                  <span>观众 ({r.observerCount ?? observers.length})：</span>
                  <span className="text-slate-300 truncate">
                    {observers.length > 0 ? observers.map((o: any) => o.playerName).join('、') : '暂无观众'}
                  </span>
                </div>
              </div>
            );
          })}
        </div>

        <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-4">
          {selectedRoom ? (
            <>
              <div className="flex items-center justify-between border-b border-slate-800 pb-2">
                <div>
                  <h2 className="font-bold text-sm">房间详情：{selectedRoom.summary.roomName}</h2>
                  <div className="text-[11px] text-slate-400">
                    MR{selectedRoom.summary.masterRule} · 禁限表: {selectedRoom.summary.banlistName || '无'} · 观战 {selectedRoom.summary.observerCount}
                  </div>
                </div>
                {(hasPerm(user, 'room:close') || hasPerm(user, 'room:create_own')) && (
                  <button
                    onClick={() => closeRoom(selectedRoom.summary.roomName)}
                    className="px-2.5 py-1 rounded bg-rose-600/80 hover:bg-rose-600 text-xs cursor-pointer"
                  >
                    关闭房间
                  </button>
                )}
              </div>

              {!user.isStreamerOnly && selectedRoom.summary.hostPassword && (
                <div className="bg-slate-950 border border-slate-800 rounded-lg p-2.5 text-xs space-y-1.5">
                  <div className="flex items-center justify-between gap-2 flex-wrap">
                    <span className="text-slate-400">
                      主机密码: <code className="font-mono text-white">{selectedRoom.summary.hostPassword}</code>
                    </span>
                    <QuickCopyPill
                      text={selectedRoom.summary.hostPassword}
                      label="复制主机密码"
                      onCopied={() => notify(`已复制主机密码：${selectedRoom.summary.hostPassword}`)}
                    />
                  </div>
                  {selectedRoom.summary.fullHostPassword &&
                    selectedRoom.summary.fullHostPassword !== selectedRoom.summary.hostPassword && (
                      <div className="flex items-center justify-between gap-2 flex-wrap pt-1 border-t border-slate-800/70">
                        <span className="text-slate-400 truncate">
                          完整口令: <code className="font-mono text-indigo-300">{selectedRoom.summary.fullHostPassword}</code>
                        </span>
                        <QuickCopyPill
                          text={selectedRoom.summary.fullHostPassword}
                          label="复制规则串"
                          onCopied={() => notify(`已复制规则口令：${selectedRoom.summary.fullHostPassword}`)}
                        />
                      </div>
                    )}
                </div>
              )}

              {hasPerm(user, 'bot:spawn') && selectedRoom.summary.state === 'Lobby' && (
                <div className="space-y-2 bg-slate-950 p-3 rounded-lg border border-slate-800">
                  <div className="text-xs font-medium text-slate-300">召唤内置 WindBot</div>
                  <div className="flex gap-2">
                    <select
                      value={selectedBotDeck}
                      onChange={(e) => setSelectedBotDeck(e.target.value)}
                      className="flex-1 px-2 py-1 rounded bg-slate-900 border border-slate-700 text-xs"
                    >
                      <option value="random">随机 AI 卡组 (random)</option>
                      {botDecks.map((d) => (
                        <option key={d} value={d}>
                          {d}
                        </option>
                      ))}
                    </select>
                    <button
                      onClick={() => spawnBot(selectedRoom.summary.roomName, false)}
                      className="px-2.5 py-1 rounded bg-indigo-600 text-xs cursor-pointer"
                    >
                      +1 AI
                    </button>
                    <button
                      onClick={() => spawnBot(selectedRoom.summary.roomName, true)}
                      className="px-2.5 py-1 rounded bg-purple-600 text-xs cursor-pointer"
                    >
                      补满
                    </button>
                  </div>
                </div>
              )}

              {hasPerm(user, 'room:start_death') && (
                <div className="flex items-center justify-between text-xs bg-slate-950 p-2.5 rounded-lg border border-slate-800">
                  <span>额外回合猝死规则 (ED)</span>
                  <button
                    onClick={() => toggleDeath(selectedRoom.summary.roomName, !selectedRoom.summary.isSuddenDeath)}
                    className={`px-2.5 py-1 rounded cursor-pointer ${
                      selectedRoom.summary.isSuddenDeath ? 'bg-slate-700' : 'bg-rose-600 text-white'
                    }`}
                  >
                    {selectedRoom.summary.isSuddenDeath ? '取消 ED' : '开启 ED'}
                  </button>
                </div>
              )}

              <div className="space-y-1">
                <div className="text-xs font-semibold text-slate-300">席位管理</div>
                {selectedRoom.summary.slots.map((s: any) => (
                  <div key={s.seatIndex} className="flex items-center justify-between text-xs bg-slate-950 px-2.5 py-1.5 rounded">
                    <span>
                      #{s.seatIndex} {s.playerName || '<空>'} {s.clientIp ? `(${s.clientIp})` : ''}
                    </span>
                    {s.playerName && hasPerm(user, 'room:kick') && (
                      <button
                        onClick={() => kickSlot(selectedRoom.summary.roomName, s.seatIndex)}
                        className="text-rose-400 hover:underline cursor-pointer"
                      >
                        踢出
                      </button>
                    )}
                  </div>
                ))}
              </div>

              <div className="space-y-1">
                <div className="text-xs font-semibold text-sky-300 flex items-center gap-1">
                  <Eye className="w-3.5 h-3.5" /> 观战观众列表 ({(selectedRoom.summary.observers || []).length} 人)
                </div>
                {(selectedRoom.summary.observers || []).length > 0 ? (
                  <div className="space-y-1 max-h-28 overflow-y-auto">
                    {(selectedRoom.summary.observers || []).map((obs: any, idx: number) => (
                      <div key={`${obs.playerName}-${idx}`} className="flex items-center justify-between text-xs bg-slate-950 px-2.5 py-1.5 rounded">
                        <span>
                          {obs.playerName} {obs.clientIp ? `(${obs.clientIp})` : ''}
                        </span>
                        {hasPerm(user, 'room:kick') && (
                          <button
                            onClick={() => kickByName(selectedRoom.summary.roomName, obs.playerName)}
                            className="text-rose-400 hover:underline cursor-pointer"
                          >
                            踢出观众
                          </button>
                        )}
                      </div>
                    ))}
                  </div>
                ) : (
                  <div className="text-[11px] text-slate-500 bg-slate-950 px-2.5 py-2 rounded">当前暂无观战观众</div>
                )}
              </div>

              <div className="space-y-2">
                <div className="text-xs font-semibold text-slate-300">公开事件与房间聊天</div>
                <div className="h-44 overflow-y-auto bg-slate-950 rounded p-2.5 text-xs space-y-1 border border-slate-800">
                  {(selectedRoom.publicEvents || []).map((ev: any, idx: number) => (
                    <div key={`ev-${idx}`} className="text-indigo-300">
                      · {ev.summary}
                    </div>
                  ))}
                  {(selectedRoom.chats || []).map((c: any, idx: number) => (
                    <div key={`ch-${idx}`} className="text-slate-200">
                      <span className="text-amber-300">{c.senderName}:</span> {c.message}
                    </div>
                  ))}
                </div>
                {hasPerm(user, 'room:shout') && (
                  <form onSubmit={sendRoomChat} className="flex gap-2">
                    <input
                      value={roomChatMsg}
                      onChange={(e) => setRoomChatMsg(e.target.value)}
                      placeholder="发送裁判黄字消息..."
                      className="flex-1 px-2.5 py-1 rounded bg-slate-950 border border-slate-700 text-xs"
                    />
                    <button className="px-3 py-1 rounded bg-amber-500 text-slate-950 font-semibold text-xs cursor-pointer">发送</button>
                  </form>
                )}
              </div>
            </>
          ) : (
            <div className="text-xs text-slate-400 py-10 text-center">点击左侧任意房间卡片查看详情、召唤 AI 或执行裁判操作</div>
          )}
        </div>
      </div>

      <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-3">
        <h2 className="text-sm font-semibold flex items-center gap-2">
          <Video className="w-4 h-4 text-sky-400" /> 历史对局战报与云录像 (客户端输入 R#编号 即可直接回放)
        </h2>
        <div className="overflow-x-auto">
          <table className="w-full text-xs text-left">
            <thead className="text-slate-400 border-b border-slate-800">
              <tr>
                <th className="py-2">回放口令</th>
                <th>房间</th>
                <th>模式 / 随机</th>
                <th>参战席位</th>
                <th>回合数</th>
                <th>时间</th>
                <th>录像</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-800/60">
              {replays.map((r) => (
                <tr key={r.id}>
                  <td className="py-2 font-mono text-indigo-300 flex items-center gap-1">
                    {r.replayCode} <CopyButton text={r.replayCode} />
                  </td>
                  <td>{r.roomName}</td>
                  <td>
                    {r.mode === 2 ? '2v2 Tag' : r.mode === 1 ? 'Match' : 'Single'} · {r.randomDeckMode}
                  </td>
                  <td>
                    {(r.players || []).map((p: any) => `${p.playerName}${p.isWinner ? '(胜)' : ''}`).join(' vs ')}
                  </td>
                  <td>{r.turnCount}</td>
                  <td className="text-slate-400">{new Date(r.endedAt).toLocaleString()}</td>
                  <td>
                    {r.hasYrp && hasPerm(user, 'replay:download') && (
                      <a
                        href={`/api/v1/replays/${r.id}.yrp`}
                        className="text-sky-400 hover:underline"
                        download
                      >
                        下载 .yrp
                      </a>
                    )}
                  </td>
                </tr>
              ))}
              {replays.length === 0 && (
                <tr>
                  <td colSpan={7} className="py-4 text-center text-slate-500">
                    暂无历史战报记录
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

type StudioSubTab = 'card' | 'script' | 'image' | 'proposals';

const CARD_TYPE_FLAGS: { label: string; bit: number }[] = [
  { label: '怪兽', bit: 0x1 },
  { label: '魔法', bit: 0x2 },
  { label: '陷阱', bit: 0x4 },
  { label: '通常', bit: 0x10 },
  { label: '效果', bit: 0x20 },
  { label: '融合', bit: 0x40 },
  { label: '仪式', bit: 0x80 },
  { label: '调整', bit: 0x1000 },
  { label: '同调', bit: 0x2000 },
  { label: '速攻', bit: 0x10000 },
  { label: '永续', bit: 0x20000 },
  { label: '装备', bit: 0x40000 },
  { label: '场地', bit: 0x80000 },
  { label: '反击', bit: 0x100000 },
  { label: '超量', bit: 0x800000 },
  { label: '灵摆', bit: 0x1000000 },
  { label: '连接', bit: 0x4000000 }
];

function StudioView({ user, notify }: { user: AuthUser; notify: (t: string, k?: 'ok' | 'err') => void }) {
  const [packages, setPackages] = useState<any[]>([]);
  const [trash, setTrash] = useState<string[]>([]);
  const [conflicts, setConflicts] = useState<any>(null);
  const [selectedPkgId, setSelectedPkgId] = useState('');
  const [pkgCards, setPkgCards] = useState<any[]>([]);
  const [cardFilter, setCardFilter] = useState('');
  const [stringsConfText, setStringsConfText] = useState('');

  const [subTab, setSubTab] = useState<StudioSubTab>('card');
  const [editingCard, setEditingCard] = useState<any>({
    id: 99000001,
    ot: 4,
    alias: 0,
    setcodeHex: '0x0',
    type: 0x2,
    level: 0,
    lScale: 0,
    rScale: 0,
    race: 0,
    attribute: 0,
    attack: 0,
    defense: 0,
    linkMarker: 0,
    name: '星辉试作魔法',
    desc: '①：自己回复1000基本分。'
  });
  const [luaScript, setLuaScript] = useState(
    `-- DIY 卡脚本示例\nlocal s,id=GetID()\nfunction s.initial_effect(c)\n\tlocal e1=Effect.CreateEffect(c)\n\te1:SetType(EFFECT_TYPE_ACTIVATE)\n\te1:SetCode(EVENT_FREE_CHAIN)\n\te1:SetTarget(s.rectg)\n\te1:SetOperation(s.recop)\n\tc:RegisterEffect(e1)\nend\nfunction s.rectg(e,tp,eg,ep,ev,re,r,rp,chk)\n\tif chk==0 then return true end\n\tDuel.SetOperationInfo(0,CATEGORY_RECOVER,nil,0,tp,1000)\nend\nfunction s.recop(e,tp,eg,ep,ev,re,r,rp)\n\tDuel.Recover(tp,1000,REASON_EFFECT)\nend\n`
  );
  const [hasCardImage, setHasCardImage] = useState(false);
  const [imgCacheBust, setImgCacheBust] = useState(0);
  const [sandboxResult, setSandboxResult] = useState<any>(null);
  const [sandboxRunning, setSandboxRunning] = useState(false);

  const [proposals, setProposals] = useState<any[]>([]);
  const [reviewComment, setReviewComment] = useState('');

  const [poolDecks, setPoolDecks] = useState<any[]>([]);
  const [chaosPreview, setChaosPreview] = useState<any>(null);
  const [cardQuery, setCardQuery] = useState('');
  const [cards, setCards] = useState<any[]>([]);
  const [newPkgId, setNewPkgId] = useState('');
  const [newPkgName, setNewPkgName] = useState('');

  const loadPackageCards = useCallback(
    async (pkgId: string) => {
      if (!pkgId || !hasPerm(user, 'diy:view')) return;
      try {
        const res = await apiFetch(`/api/v1/diy/packages/${encodeURIComponent(pkgId)}/cards`);
        setPkgCards(res.cards || []);
        setStringsConfText(res.stringsConf?.rawContent || '');
      } catch (err) {
        notify(err instanceof Error ? err.message : '读取包内卡片失败', 'err');
      }
    },
    [user, notify]
  );

  const loadAll = useCallback(async () => {
    try {
      if (hasPerm(user, 'diy:view')) {
        const [d, cf, pr] = await Promise.all([
          apiFetch('/api/v1/diy/packages'),
          apiFetch('/api/v1/diy/conflicts'),
          apiFetch('/api/v1/diy/proposals?limit=50')
        ]);
        setPackages(d.packages || []);
        setTrash(d.trash || []);
        setConflicts(cf);
        setProposals(pr.proposals || []);
        const activePkg = selectedPkgId || (d.packages?.length ? d.packages[0].manifest.id : '');
        if (activePkg) {
          if (activePkg !== selectedPkgId) setSelectedPkgId(activePkg);
          await loadPackageCards(activePkg);
        }
      }
      if (hasPerm(user, 'deck_pool:view')) {
        const p = await apiFetch('/api/v1/decks/pool');
        setPoolDecks(p.decks || []);
      }
    } catch (err) {
      notify(err instanceof Error ? err.message : '加载资源失败', 'err');
    }
  }, [user, notify, selectedPkgId, loadPackageCards]);

  useEffect(() => {
    loadAll();
  }, [loadAll]);

  const selectCardForEdit = async (pkgId: string, cardId: number) => {
    try {
      const res = await apiFetch(`/api/v1/diy/packages/${encodeURIComponent(pkgId)}/cards/${cardId}`);
      const c = res.detail.card;
      setEditingCard({
        ...c,
        setcodeHex: '0x' + Number(c.setcode || 0).toString(16)
      });
      setLuaScript(res.detail.scriptContent || `-- c${cardId}.lua\nlocal s,id=GetID()\nfunction s.initial_effect(c)\n\nend\n`);
      setHasCardImage(Boolean(res.detail.hasImage));
      setSandboxResult(null);
    } catch (err) {
      notify(err instanceof Error ? err.message : '加载卡片详情失败', 'err');
    }
  };

  const startNewCardDraft = () => {
    const nextId = pkgCards.length > 0 ? Math.max(...pkgCards.map((x: any) => x.card.id)) + 1 : 99000001;
    setEditingCard({
      id: nextId,
      ot: 4,
      alias: 0,
      setcodeHex: '0x0',
      type: 0x21,
      level: 4,
      lScale: 0,
      rScale: 0,
      race: 1,
      attribute: 1,
      attack: 1800,
      defense: 1200,
      linkMarker: 0,
      name: `DIY 新卡 #${nextId}`,
      desc: '①：1回合1次，自己的主要阶段才能发动。自己回复1000基本分。'
    });
    setLuaScript(
      `-- c${nextId}.lua\nlocal s,id=GetID()\nfunction s.initial_effect(c)\n\tlocal e1=Effect.CreateEffect(c)\n\te1:SetType(EFFECT_TYPE_IGNITION)\n\te1:SetRange(LOCATION_MZONE)\n\te1:SetCountLimit(1)\n\te1:SetTarget(s.rectg)\n\te1:SetOperation(s.recop)\n\tc:RegisterEffect(e1)\nend\nfunction s.rectg(e,tp,eg,ep,ev,re,r,rp,chk)\n\tif chk==0 then return true end\n\tDuel.SetOperationInfo(0,CATEGORY_RECOVER,nil,0,tp,1000)\nend\nfunction s.recop(e,tp,eg,ep,ev,re,r,rp)\n\tDuel.Recover(tp,1000,REASON_EFFECT)\nend\n`
    );
    setHasCardImage(false);
    setSandboxResult(null);
    setSubTab('card');
  };

  const saveCardDirect = async (autoReload: boolean) => {
    if (!selectedPkgId) {
      notify('请先选择或新建一个受管 DIY 包', 'err');
      return;
    }
    try {
      const res = await apiFetch(`/api/v1/diy/packages/${encodeURIComponent(selectedPkgId)}/cards/${Number(editingCard.id)}`, {
        method: 'PUT',
        body: JSON.stringify({
          card: {
            ...editingCard,
            id: Number(editingCard.id)
          },
          luaScript,
          autoReload
        })
      });
      notify(
        res.reload
          ? `已保存 c${editingCard.id} 并热重载至 Catalog v${res.reload.catalogVersion}`
          : `已通过 Working Copy 原子保存 c${editingCard.id}`
      );
      await loadAll();
    } catch (err) {
      notify(err instanceof Error ? err.message : '保存失败', 'err');
    }
  };

  const submitAsProposal = async () => {
    if (!selectedPkgId) {
      notify('请先选择目标 DIY 包', 'err');
      return;
    }
    try {
      await apiFetch('/api/v1/diy/proposals', {
        method: 'POST',
        body: JSON.stringify({
          packageId: selectedPkgId,
          title: `新增/修改卡片 ${editingCard.name} (#${editingCard.id})`,
          payload: {
            cards: [{ card: { ...editingCard, id: Number(editingCard.id) }, luaScript }]
          }
        })
      });
      notify('已提交至待审提案队列 (Proposals)');
      setSubTab('proposals');
      await loadAll();
    } catch (err) {
      notify(err instanceof Error ? err.message : '提交提案失败', 'err');
    }
  };

  const deleteCurrentCard = async () => {
    if (!selectedPkgId || !editingCard.id) return;
    try {
      await apiFetch(`/api/v1/diy/packages/${encodeURIComponent(selectedPkgId)}/cards/${Number(editingCard.id)}`, {
        method: 'DELETE'
      });
      notify(`已从包 ${selectedPkgId} 移除卡片 #${editingCard.id}`);
      await loadAll();
    } catch (err) {
      notify(err instanceof Error ? err.message : '删除失败', 'err');
    }
  };

  const runSandbox = async () => {
    setSandboxRunning(true);
    try {
      const res = await apiFetch('/api/v1/diy/sandbox-test', {
        method: 'POST',
        body: JSON.stringify({
          card: { ...editingCard, id: Number(editingCard.id) },
          luaScript,
          testExecuteFirstActivation: true
        })
      });
      setSandboxResult(res.result);
      if (res.result.success) {
        notify(`沙盒预检通过 (${res.result.elapsedMs} ms)`);
      } else {
        notify('沙盒检测到脚本错误或安全违规，请查看诊断详情', 'err');
      }
    } catch (err) {
      notify(err instanceof Error ? err.message : '沙盒运行失败', 'err');
    } finally {
      setSandboxRunning(false);
    }
  };

  const uploadCardArtwork = async (files: FileList | null) => {
    if (!files?.length || !selectedPkgId || !editingCard.id) return;
    const fd = new FormData();
    fd.append('image', files[0]);
    try {
      await apiFetch(`/api/v1/diy/packages/${encodeURIComponent(selectedPkgId)}/cards/${Number(editingCard.id)}/image`, {
        method: 'POST',
        body: fd
      });
      setHasCardImage(true);
      setImgCacheBust((n) => n + 1);
      notify('卡图上传成功');
      await loadPackageCards(selectedPkgId);
    } catch (err) {
      notify(err instanceof Error ? err.message : '卡图上传失败', 'err');
    }
  };

  const saveStringsConf = async () => {
    if (!selectedPkgId) return;
    try {
      await apiFetch(`/api/v1/diy/packages/${encodeURIComponent(selectedPkgId)}/strings`, {
        method: 'PUT',
        body: JSON.stringify({ content: stringsConfText })
      });
      notify('strings.conf 已原子保存');
    } catch (err) {
      notify(err instanceof Error ? err.message : '保存 strings.conf 失败', 'err');
    }
  };

  const reviewProposal = async (proposalId: string, approve: boolean) => {
    try {
      const res = await apiFetch(`/api/v1/diy/proposals/${proposalId}/review`, {
        method: 'POST',
        body: JSON.stringify({ approve, comment: reviewComment })
      });
      setReviewComment('');
      notify(res.message || (approve ? '提案已批准并热重载' : '提案已驳回'));
      await loadAll();
    } catch (err) {
      notify(err instanceof Error ? err.message : '审批失败', 'err');
    }
  };

  const createPackage = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      await apiFetch('/api/v1/diy/packages', {
        method: 'POST',
        body: JSON.stringify({ id: newPkgId, name: newPkgName || newPkgId })
      });
      setSelectedPkgId(newPkgId);
      setNewPkgId('');
      setNewPkgName('');
      notify('DIY 包已创建');
      loadAll();
    } catch (err) {
      notify(err instanceof Error ? err.message : '创建失败', 'err');
    }
  };

  const uploadFiles = async (files: FileList | null) => {
    if (!files || files.length === 0 || !selectedPkgId) return;
    const fd = new FormData();
    Array.from(files).forEach((f) => fd.append('files', f));
    try {
      const res = await apiFetch(`/api/v1/diy/packages/${encodeURIComponent(selectedPkgId)}/upload`, {
        method: 'POST',
        body: fd
      });
      if (res.errors?.length) {
        notify(`部分文件被安全拦截: ${res.errors.join('; ')}`, 'err');
      } else {
        notify(`成功上传 ${res.savedFiles.length} 个文件至 ${selectedPkgId}`);
      }
      loadAll();
    } catch (err) {
      notify(err instanceof Error ? err.message : '上传失败', 'err');
    }
  };

  const runChaosPreview = async () => {
    try {
      const res = await apiFetch('/api/v1/decks/chaos-preview', {
        method: 'POST',
        body: JSON.stringify({ mainSize: 40, pureChaos: false })
      });
      setChaosPreview(res);
    } catch (err) {
      notify(err instanceof Error ? err.message : '试抽失败', 'err');
    }
  };

  const searchCards = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const res = await apiFetch(`/api/v1/cards/search?q=${encodeURIComponent(cardQuery)}&limit=30`);
      setCards(res.cards || []);
    } catch (err) {
      notify(err instanceof Error ? err.message : '查询失败', 'err');
    }
  };

  const toggleTypeBit = (bit: number) => {
    setEditingCard((prev: any) => ({
      ...prev,
      type: (prev.type & bit) !== 0 ? prev.type & ~bit : prev.type | bit
    }));
  };

  const filteredPkgCards = pkgCards.filter((x: any) => {
    if (!cardFilter.trim()) return true;
    const kw = cardFilter.trim().toLowerCase();
    return String(x.card.id).includes(kw) || (x.card.name || '').toLowerCase().includes(kw);
  });

  const pendingProposalCount = proposals.filter((p) => p.status === 'pending').length;

  return (
    <div className="space-y-6">
      {/* 顶部包选择、批量导入、导出 .ypk 与免重启热重载工具栏 */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-sm font-semibold flex items-center gap-1.5">
              <Layers className="w-4 h-4 text-indigo-400" /> 当前工作包:
            </span>
            <select
              value={selectedPkgId}
              onChange={(e) => {
                setSelectedPkgId(e.target.value);
                loadPackageCards(e.target.value);
              }}
              className="px-3 py-1.5 rounded-lg bg-slate-950 border border-slate-700 text-xs font-mono"
            >
              {packages.length === 0 && <option value="">-- 请先新建受管 DIY 包 --</option>}
              {packages.map((p) => (
                <option key={p.manifest.id} value={p.manifest.id}>
                  {p.manifest.name} ({p.manifest.id}) · {p.validation.cardCount} 张卡
                </option>
              ))}
            </select>

            {selectedPkgId && (
              <a
                href={`/api/v1/diy/packages/${encodeURIComponent(selectedPkgId)}/export.ypk`}
                download
                className="px-3 py-1.5 rounded-lg bg-sky-600/20 border border-sky-500/40 text-sky-300 text-xs hover:bg-sky-600/30"
              >
                导出客户端 .ypk
              </a>
            )}

            {selectedPkgId && hasPerm(user, 'diy:upload') && (
              <label className="px-3 py-1.5 rounded-lg bg-indigo-600/20 border border-indigo-500/40 text-indigo-300 text-xs cursor-pointer hover:bg-indigo-600/30">
                批量导入 .cdb/.lua/.ydk
                <input type="file" multiple className="hidden" onChange={(e) => uploadFiles(e.target.files)} />
              </label>
            )}

            {selectedPkgId && hasPerm(user, 'diy:edit') && (
              <button
                onClick={async () => {
                  await apiFetch(`/api/v1/diy/packages/${encodeURIComponent(selectedPkgId)}`, { method: 'DELETE' });
                  setSelectedPkgId('');
                  setPkgCards([]);
                  notify('包已移入 .trash 回收站');
                  loadAll();
                }}
                className="px-2.5 py-1.5 rounded-lg bg-rose-950/60 border border-rose-500/30 text-rose-300 text-xs cursor-pointer"
              >
                移入回收站
              </button>
            )}
          </div>

          <div className="flex items-center gap-2">
            {hasPerm(user, 'diy:edit') && (
              <form onSubmit={createPackage} className="flex items-center gap-1.5">
                <input
                  value={newPkgId}
                  onChange={(e) => setNewPkgId(e.target.value)}
                  placeholder="新包 ID (如 pack-01)"
                  className="w-36 px-2.5 py-1.5 rounded-lg bg-slate-950 border border-slate-700 text-xs"
                  required
                />
                <input
                  value={newPkgName}
                  onChange={(e) => setNewPkgName(e.target.value)}
                  placeholder="显示名称"
                  className="w-28 px-2.5 py-1.5 rounded-lg bg-slate-950 border border-slate-700 text-xs"
                />
                <button className="px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-xs cursor-pointer">
                  + 新建包
                </button>
              </form>
            )}

            {hasPerm(user, 'diy:reload') && (
              <button
                onClick={async () => {
                  const r = await apiFetch('/api/v1/diy/reload', { method: 'POST', body: '{}' });
                  notify(`已热重载至 Catalog v${r.reload.catalogVersion}（进行中对局零中断）`);
                  loadAll();
                }}
                className="px-3 py-1.5 rounded-lg bg-indigo-600 hover:bg-indigo-500 text-xs font-semibold flex items-center gap-1 cursor-pointer"
              >
                <RefreshCw className="w-3.5 h-3.5" /> 免重启热重载
              </button>
            )}
          </div>
        </div>

        {conflicts?.conflicts?.length > 0 && (
          <div className="px-3 py-2 rounded-lg bg-amber-950/50 border border-amber-500/30 text-xs text-amber-200">
            多库冲突巡检：检测到 {conflicts.conflicts.length} 个覆盖基础卡库或跨包重复定义的卡密（
            {conflicts.conflicts
              .slice(0, 5)
              .map((c: any) => `#${c.cardId} ${c.cardName}`)
              .join('、')}
            ）
          </div>
        )}

        {trash.length > 0 && (
          <div className="flex flex-wrap items-center gap-2 pt-2 border-t border-slate-800 text-xs">
            <span className="text-slate-400">.trash 回收站：</span>
            {trash.map((t) => (
              <span key={t} className="inline-flex items-center gap-1.5 bg-slate-950 px-2.5 py-1 rounded border border-slate-800">
                <span className="font-mono text-[11px]">{t}</span>
                <button
                  onClick={async () => {
                    await apiFetch(`/api/v1/diy/trash/${encodeURIComponent(t)}/restore`, { method: 'POST', body: '{}' });
                    notify('已从回收站恢复');
                    loadAll();
                  }}
                  className="text-emerald-400 hover:underline cursor-pointer"
                >
                  恢复
                </button>
              </span>
            ))}
          </div>
        )}
      </div>

      {/* 主工作区：左侧包内卡片筛选列表 + strings.conf | 右侧 Card / Script+Sandbox / Image / Proposals 四面板 */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-4">
        <div className="lg:col-span-4 bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-3 flex flex-col">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold text-slate-300">包内卡片列表 ({filteredPkgCards.length})</span>
            {hasPerm(user, 'diy:edit') && (
              <button
                onClick={startNewCardDraft}
                className="px-2.5 py-1 rounded bg-indigo-600 hover:bg-indigo-500 text-xs flex items-center gap-1 cursor-pointer"
              >
                <Plus className="w-3.5 h-3.5" /> 新建单卡
              </button>
            )}
          </div>

          <input
            value={cardFilter}
            onChange={(e) => setCardFilter(e.target.value)}
            placeholder="按卡密或卡名筛选..."
            className="w-full px-2.5 py-1.5 rounded bg-slate-950 border border-slate-700 text-xs"
          />

          <div className="space-y-1.5 max-h-96 overflow-y-auto flex-1">
            {filteredPkgCards.map((item: any) => (
              <div
                key={item.card.id}
                onClick={() => selectCardForEdit(selectedPkgId, item.card.id)}
                className={`p-2.5 rounded-lg border text-xs cursor-pointer transition ${
                  Number(editingCard.id) === item.card.id
                    ? 'bg-indigo-950/50 border-indigo-500'
                    : 'bg-slate-950 border-slate-800 hover:border-slate-700'
                }`}
              >
                <div className="flex items-center justify-between font-medium">
                  <span>{item.card.name || '<未命名>'}</span>
                  <span className="font-mono text-[11px] text-slate-400">#{item.card.id}</span>
                </div>
                <div className="flex items-center gap-1.5 mt-1 text-[10px]">
                  <span className={item.hasScript ? 'text-emerald-400' : 'text-amber-400'}>
                    {item.hasScript ? 'Lua 已就绪' : '无独立脚本'}
                  </span>
                  <span>·</span>
                  <span className={item.hasImage ? 'text-sky-400' : 'text-slate-500'}>
                    {item.hasImage ? '含卡图' : '无卡图'}
                  </span>
                </div>
              </div>
            ))}
            {filteredPkgCards.length === 0 && (
              <div className="text-xs text-slate-500 py-8 text-center">
                当前包暂无卡片，可点击右上角「新建单卡」或批量上传 .cdb
              </div>
            )}
          </div>

          {selectedPkgId && (
            <div className="pt-3 border-t border-slate-800 space-y-2">
              <div className="flex items-center justify-between text-xs">
                <span className="text-slate-400 font-medium">strings.conf (!setname / !counter)</span>
                {hasPerm(user, 'diy:edit') && (
                  <button onClick={saveStringsConf} className="text-indigo-400 hover:underline cursor-pointer">
                    保存字段配置
                  </button>
                )}
              </div>
              <textarea
                value={stringsConfText}
                onChange={(e) => setStringsConfText(e.target.value)}
                rows={3}
                placeholder="!setname 0xa100 星辉试作"
                className="w-full p-2 rounded bg-slate-950 border border-slate-800 font-mono text-[11px]"
              />
            </div>
          )}
        </div>

        <div className="lg:col-span-8 bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-800 pb-3">
            <div className="flex items-center gap-1.5">
              {(
                [
                  ['card', '1. Card 属性表单'],
                  ['script', '2. Lua 脚本 & 引擎沙盒'],
                  ['image', '3. 卡图预览'],
                  ['proposals', `4. 提案审批 (${pendingProposalCount})`]
                ] as [StudioSubTab, string][]
              ).map(([key, label]) => (
                <button
                  key={key}
                  onClick={() => setSubTab(key)}
                  className={`px-3 py-1.5 rounded-lg text-xs font-medium cursor-pointer ${
                    subTab === key ? 'bg-indigo-600 text-white' : 'bg-slate-950 text-slate-300 hover:bg-slate-800'
                  }`}
                >
                  {label}
                </button>
              ))}
            </div>

            {subTab !== 'proposals' && hasPerm(user, 'diy:edit') && (
              <div className="flex items-center gap-2">
                <button
                  onClick={submitAsProposal}
                  className="px-2.5 py-1.5 rounded-lg bg-amber-600/20 border border-amber-500/40 text-amber-300 text-xs cursor-pointer"
                >
                  提交为提案
                </button>
                <button
                  onClick={() => saveCardDirect(true)}
                  className="px-3 py-1.5 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-xs font-semibold cursor-pointer"
                >
                  原子保存并热重载
                </button>
              </div>
            )}
          </div>

          {subTab === 'card' && (
            <div className="space-y-3 text-xs">
              <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
                <div>
                  <label className="block text-slate-400 mb-1">卡密 (Code ID)</label>
                  <input
                    type="number"
                    value={editingCard.id}
                    onChange={(e) => setEditingCard({ ...editingCard, id: Number(e.target.value) })}
                    className="w-full px-2.5 py-1.5 rounded bg-slate-950 border border-slate-700 font-mono"
                  />
                </div>
                <div className="col-span-2">
                  <label className="block text-slate-400 mb-1">卡片名称</label>
                  <input
                    value={editingCard.name}
                    onChange={(e) => setEditingCard({ ...editingCard, name: e.target.value })}
                    className="w-full px-2.5 py-1.5 rounded bg-slate-950 border border-slate-700"
                  />
                </div>
                <div>
                  <label className="block text-slate-400 mb-1">OT 归属</label>
                  <select
                    value={editingCard.ot}
                    onChange={(e) => setEditingCard({ ...editingCard, ot: Number(e.target.value) })}
                    className="w-full px-2.5 py-1.5 rounded bg-slate-950 border border-slate-700"
                  >
                    <option value={4}>DIY 自定义 (0x4)</option>
                    <option value={7}>OCG+TCG+DIY (0x7)</option>
                    <option value={3}>OCG+TCG (0x3)</option>
                    <option value={1}>OCG (0x1)</option>
                    <option value={2}>TCG (0x2)</option>
                  </select>
                </div>
              </div>

              <div>
                <label className="block text-slate-400 mb-1">卡片种类位掩码 (Type: 0x{Number(editingCard.type || 0).toString(16)})</label>
                <div className="flex flex-wrap gap-1.5 bg-slate-950 p-2.5 rounded-lg border border-slate-800">
                  {CARD_TYPE_FLAGS.map((f) => {
                    const checked = (editingCard.type & f.bit) !== 0;
                    return (
                      <button
                        type="button"
                        key={f.bit}
                        onClick={() => toggleTypeBit(f.bit)}
                        className={`px-2 py-1 rounded text-[11px] border cursor-pointer ${
                          checked
                            ? 'bg-indigo-600/30 border-indigo-400 text-indigo-200'
                            : 'bg-slate-900 border-slate-800 text-slate-400'
                        }`}
                      >
                        {f.label}
                      </button>
                    );
                  })}
                </div>
              </div>

              <div className="grid grid-cols-2 md:grid-cols-6 gap-2.5">
                <div>
                  <label className="block text-slate-400 mb-1">星级/阶级/Link</label>
                  <input
                    type="number"
                    value={editingCard.level}
                    onChange={(e) => setEditingCard({ ...editingCard, level: Number(e.target.value) })}
                    className="w-full px-2 py-1 rounded bg-slate-950 border border-slate-700"
                  />
                </div>
                <div>
                  <label className="block text-slate-400 mb-1">ATK 攻击力</label>
                  <input
                    type="number"
                    value={editingCard.attack}
                    onChange={(e) => setEditingCard({ ...editingCard, attack: Number(e.target.value) })}
                    className="w-full px-2 py-1 rounded bg-slate-950 border border-slate-700"
                  />
                </div>
                <div>
                  <label className="block text-slate-400 mb-1">DEF 守备力</label>
                  <input
                    type="number"
                    value={editingCard.defense}
                    onChange={(e) => setEditingCard({ ...editingCard, defense: Number(e.target.value) })}
                    className="w-full px-2 py-1 rounded bg-slate-950 border border-slate-700"
                  />
                </div>
                <div>
                  <label className="block text-slate-400 mb-1">左刻度 / 右刻度</label>
                  <div className="flex gap-1">
                    <input
                      type="number"
                      value={editingCard.lScale}
                      onChange={(e) => setEditingCard({ ...editingCard, lScale: Number(e.target.value) })}
                      className="w-1/2 px-1.5 py-1 rounded bg-slate-950 border border-slate-700"
                    />
                    <input
                      type="number"
                      value={editingCard.rScale}
                      onChange={(e) => setEditingCard({ ...editingCard, rScale: Number(e.target.value) })}
                      className="w-1/2 px-1.5 py-1 rounded bg-slate-950 border border-slate-700"
                    />
                  </div>
                </div>
                <div>
                  <label className="block text-slate-400 mb-1">属性 (Attr)</label>
                  <input
                    type="number"
                    value={editingCard.attribute}
                    onChange={(e) => setEditingCard({ ...editingCard, attribute: Number(e.target.value) })}
                    className="w-full px-2 py-1 rounded bg-slate-950 border border-slate-700"
                  />
                </div>
                <div>
                  <label className="block text-slate-400 mb-1">种族 (Race)</label>
                  <input
                    type="number"
                    value={editingCard.race}
                    onChange={(e) => setEditingCard({ ...editingCard, race: Number(e.target.value) })}
                    className="w-full px-2 py-1 rounded bg-slate-950 border border-slate-700"
                  />
                </div>
              </div>

              <div className="grid grid-cols-2 md:grid-cols-3 gap-2.5">
                <div>
                  <label className="block text-slate-400 mb-1">字段码 Setcode (十六进制)</label>
                  <input
                    value={editingCard.setcodeHex || '0x0'}
                    onChange={(e) => setEditingCard({ ...editingCard, setcodeHex: e.target.value })}
                    className="w-full px-2.5 py-1 rounded bg-slate-950 border border-slate-700 font-mono"
                  />
                </div>
                <div>
                  <label className="block text-slate-400 mb-1">同名异画卡密 (Alias)</label>
                  <input
                    type="number"
                    value={editingCard.alias}
                    onChange={(e) => setEditingCard({ ...editingCard, alias: Number(e.target.value) })}
                    className="w-full px-2.5 py-1 rounded bg-slate-950 border border-slate-700 font-mono"
                  />
                </div>
                <div>
                  <label className="block text-slate-400 mb-1">Link 箭头掩码</label>
                  <input
                    type="number"
                    value={editingCard.linkMarker}
                    onChange={(e) => setEditingCard({ ...editingCard, linkMarker: Number(e.target.value) })}
                    className="w-full px-2.5 py-1 rounded bg-slate-950 border border-slate-700 font-mono"
                  />
                </div>
              </div>

              <div>
                <label className="block text-slate-400 mb-1">效果文本 / 灵摆文本</label>
                <textarea
                  rows={5}
                  value={editingCard.desc}
                  onChange={(e) => setEditingCard({ ...editingCard, desc: e.target.value })}
                  className="w-full p-2.5 rounded bg-slate-950 border border-slate-700 leading-relaxed"
                />
              </div>

              {selectedPkgId && hasPerm(user, 'diy:edit') && (
                <div className="flex justify-end">
                  <button onClick={deleteCurrentCard} className="text-rose-400 hover:underline cursor-pointer">
                    从当前包永久移除卡片 #{editingCard.id}
                  </button>
                </div>
              )}
            </div>
          )}

          {subTab === 'script' && (
            <div className="space-y-3 text-xs">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="font-mono text-slate-400">script/c{editingCard.id}.lua</span>
                <div className="flex items-center gap-2">
                  {hasPerm(user, 'diy:sandbox_test') && (
                    <button
                      onClick={runSandbox}
                      disabled={sandboxRunning}
                      className="px-3 py-1.5 rounded-lg bg-purple-600 hover:bg-purple-500 font-semibold cursor-pointer"
                    >
                      {sandboxRunning ? '正在运行 ocgcore 沙盒...' : '运行 ocgcore 单卡情景沙盒'}
                    </button>
                  )}
                </div>
              </div>

              <textarea
                rows={14}
                value={luaScript}
                onChange={(e) => setLuaScript(e.target.value)}
                spellCheck={false}
                className="w-full p-3 rounded-lg bg-slate-950 border border-slate-800 font-mono text-xs leading-relaxed text-emerald-200"
              />

              {sandboxResult && (
                <div
                  className={`p-3 rounded-lg border space-y-1.5 ${
                    sandboxResult.success
                      ? 'bg-emerald-950/40 border-emerald-500/40 text-emerald-200'
                      : 'bg-rose-950/50 border-rose-500/40 text-rose-200'
                  }`}
                >
                  <div className="font-semibold flex items-center justify-between">
                    <span>
                      {sandboxResult.success ? '✓ 沙盒预检通过' : '✕ 沙盒拦截：发现错误'} · 耗时 {sandboxResult.elapsedMs} ms
                    </span>
                    <span className="font-mono text-[11px]">
                      可通召:{sandboxResult.summonableCount} · 可发动:{sandboxResult.activatableCount} · P0 LP:{' '}
                      {sandboxResult.postPlayer0Lp}
                    </span>
                  </div>
                  {sandboxResult.staticAuditViolations?.length > 0 && (
                    <div className="font-mono text-[11px]">
                      静态安全违规：{sandboxResult.staticAuditViolations.join(' | ')}
                    </div>
                  )}
                  {sandboxResult.luaErrors?.length > 0 && (
                    <div className="font-mono text-[11px]">
                      ocgcore Lua 异常：{sandboxResult.luaErrors.join(' | ')}
                    </div>
                  )}
                  {sandboxResult.observedMessages?.length > 0 && (
                    <div className="text-[11px] text-slate-300">
                      引擎消息轨迹：{sandboxResult.observedMessages.join(' → ')}
                    </div>
                  )}
                </div>
              )}
            </div>
          )}

          {subTab === 'image' && (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4 items-center text-xs">
              <div className="bg-slate-950 border border-slate-800 rounded-xl p-4 flex flex-col items-center space-y-3">
                {selectedPkgId && hasCardImage ? (
                  <img
                    src={`/api/v1/diy/packages/${encodeURIComponent(selectedPkgId)}/cards/${Number(editingCard.id)}/image?v=${imgCacheBust}`}
                    alt={editingCard.name}
                    className="w-48 h-64 object-cover rounded-lg border border-slate-700 shadow-lg"
                  />
                ) : (
                  <div className="w-48 h-64 rounded-lg border border-dashed border-slate-700 flex items-center justify-center text-slate-500">
                    暂无卡图 ({editingCard.id})
                  </div>
                )}
                <div className="font-semibold text-sm">{editingCard.name}</div>
              </div>

              <div className="space-y-3">
                <p className="text-slate-400 leading-relaxed">
                  支持上传标准 PNG 或 JPG 卡图（自动校验二进制魔数头并限制 ≤ 8MB），保存至{' '}
                  <span className="font-mono text-slate-200">pics/{editingCard.id}.png/.jpg</span>，导出{' '}
                  <span className="font-mono text-sky-300">.ypk</span> 时自动随包打入供客户端直接显示。
                </p>
                {selectedPkgId && hasPerm(user, 'diy:edit') && (
                  <label className="inline-block px-4 py-2 rounded-lg bg-indigo-600 hover:bg-indigo-500 font-semibold cursor-pointer">
                    上传 / 替换卡图 (.png / .jpg)
                    <input type="file" accept="image/png,image/jpeg" className="hidden" onChange={(e) => uploadCardArtwork(e.target.files)} />
                  </label>
                )}
              </div>
            </div>
          )}

          {subTab === 'proposals' && (
            <div className="space-y-3 text-xs">
              <div className="flex items-center gap-2">
                <input
                  value={reviewComment}
                  onChange={(e) => setReviewComment(e.target.value)}
                  placeholder="可选审批备注（批准后将自动写入目标 DIY 包并触发热重载）..."
                  className="flex-1 px-3 py-1.5 rounded bg-slate-950 border border-slate-700"
                />
              </div>
              <div className="space-y-2 max-h-96 overflow-y-auto">
                {proposals.map((p) => (
                  <div key={p.id} className="bg-slate-950 border border-slate-800 rounded-lg p-3 space-y-2">
                    <div className="flex items-center justify-between">
                      <div>
                        <span className="font-semibold text-slate-200">{p.title}</span>{' '}
                        <span className="font-mono text-indigo-300">[{p.packageId}]</span>
                      </div>
                      <span
                        className={`px-2 py-0.5 rounded text-[11px] ${
                          p.status === 'approved'
                            ? 'bg-emerald-500/20 text-emerald-300'
                            : p.status === 'rejected'
                              ? 'bg-rose-500/20 text-rose-300'
                              : 'bg-amber-500/20 text-amber-300'
                        }`}
                      >
                        {p.status}
                      </span>
                    </div>
                    <pre className="p-2 rounded bg-slate-900 text-[11px] font-mono text-slate-300 max-h-32 overflow-auto">
                      {p.diffJson}
                    </pre>
                    {p.status === 'pending' && hasPerm(user, 'diy:review_proposal') && (
                      <div className="flex justify-end gap-2">
                        <button
                          onClick={() => reviewProposal(p.id, false)}
                          className="px-3 py-1 rounded bg-rose-600/80 hover:bg-rose-600 cursor-pointer"
                        >
                          驳回提案
                        </button>
                        <button
                          onClick={() => reviewProposal(p.id, true)}
                          className="px-3 py-1 rounded bg-emerald-600 hover:bg-emerald-500 font-semibold cursor-pointer"
                        >
                          批准并热重载生效
                        </button>
                      </div>
                    )}
                  </div>
                ))}
                {proposals.length === 0 && (
                  <div className="py-8 text-center text-slate-500">当前暂无待审或历史 DIY 提案</div>
                )}
              </div>
            </div>
          )}
        </div>
      </div>

      {/* 底部：服务端随机盲盒池、混沌试抽预览与版本化卡库搜索 */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-4">
          <div className="flex items-center justify-between">
            <h2 className="text-sm font-semibold flex items-center gap-2">
              <Sparkles className="w-4 h-4 text-purple-400" /> 随机盲盒池 ({poolDecks.length} 套) & 混沌组卡试抽
            </h2>
            <button onClick={runChaosPreview} className="px-3 py-1 rounded bg-purple-600 hover:bg-purple-500 text-xs font-medium cursor-pointer">
              试抽一套 40+15 混沌卡组
            </button>
          </div>

          {chaosPreview && (
            <div className="bg-slate-950 p-3 rounded-lg border border-purple-500/30 text-xs space-y-1.5">
              <div className="font-semibold text-purple-300">
                平衡配比结果：下级怪 {chaosPreview.breakdown.lowLevelMonsters} · 上级怪 {chaosPreview.breakdown.highLevelMonsters} · 魔法{' '}
                {chaosPreview.breakdown.spells} · 陷阱 {chaosPreview.breakdown.traps} · 额外 {chaosPreview.extraCount}
              </div>
              <div className="text-[11px] text-slate-400 line-clamp-2">
                主卡组抽样：{chaosPreview.sampleMainCards.map((c: any) => c.name).join('、')}
              </div>
            </div>
          )}

          <div className="max-h-48 overflow-y-auto space-y-1 text-xs">
            {poolDecks.map((d) => (
              <div key={d.id} className="flex items-center justify-between bg-slate-950 px-3 py-1.5 rounded">
                <span>
                  {d.name} {d.hasDiyCards && <span className="text-purple-400">[DIY]</span>}
                </span>
                <span className="text-slate-400 font-mono">
                  M:{d.mainCount} E:{d.extraCount} S:{d.sideCount}
                </span>
              </div>
            ))}
          </div>
        </div>

        <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-3">
          <form onSubmit={searchCards} className="flex gap-2">
            <input
              value={cardQuery}
              onChange={(e) => setCardQuery(e.target.value)}
              placeholder="输入卡密或卡名/效果关键字检索卡库（命中版本化 HybridCache）..."
              className="flex-1 px-3 py-1.5 rounded bg-slate-950 border border-slate-700 text-xs"
            />
            <button className="px-4 py-1.5 rounded bg-indigo-600 text-xs font-semibold cursor-pointer">检索卡库</button>
          </form>
          {cards.length > 0 && (
            <div className="grid grid-cols-1 gap-2 max-h-52 overflow-y-auto">
              {cards.map((c) => (
                <div key={c.id} className="bg-slate-950 p-2.5 rounded border border-slate-800 text-xs space-y-1">
                  <div className="flex items-center justify-between font-semibold">
                    <span>
                      {c.name} <span className="text-slate-400 font-mono">#{c.id}</span>
                    </span>
                    {c.isDiy && <span className="text-purple-400 text-[11px]">DIY (ot=4)</span>}
                  </div>
                  <div className="text-[11px] text-slate-400 line-clamp-2">{c.desc}</div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function McpHubView({ user, notify }: { user: AuthUser; notify: (t: string, k?: 'ok' | 'err') => void }) {
  const [tokens, setTokens] = useState<any[]>([]);
  const [allowedRoles, setAllowedRoles] = useState<string[]>([]);
  const [name, setName] = useState('');
  const [role, setRole] = useState('player');
  const [roomsText, setRoomsText] = useState('');
  const [newlyIssuedRaw, setNewlyIssuedRaw] = useState('');
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingNick, setEditingNick] = useState('');

  const load = useCallback(async () => {
    try {
      const res = await apiFetch('/api/v1/mcp/tokens');
      setTokens(res.tokens || []);
      setAllowedRoles(res.allowedRolesToIssue || []);
      if (res.allowedRolesToIssue?.length && !res.allowedRolesToIssue.includes(role)) {
        setRole(res.allowedRolesToIssue[0]);
      }
    } catch (err) {
      notify(err instanceof Error ? err.message : '加载 Token 失败', 'err');
    }
  }, [notify, role]);

  useEffect(() => {
    load();
  }, [load]);

  const issue = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const rooms = roomsText
        .split(',')
        .map((s) => s.trim())
        .filter(Boolean);
      const trimmedName = name.trim();
      const res = await apiFetch('/api/v1/mcp/tokens', {
        method: 'POST',
        body: JSON.stringify({
          name: trimmedName || undefined,
          preferredPlayerName: trimmedName || undefined,
          mcpRole: role,
          allowedRooms: rooms,
          maxSessions: 1,
          expiresInHours: 168
        })
      });
      setNewlyIssuedRaw(res.rawToken);
      setName('');
      notify(trimmedName ? `MCP Token 签发成功（昵称：${trimmedName}）` : 'MCP Token 签发成功（待 AI 首次进房自主取名）');
      load();
    } catch (err) {
      notify(err instanceof Error ? err.message : '签发被拒绝', 'err');
    }
  };

  const saveNickname = async (id: string, nextNick: string) => {
    try {
      const res = await apiFetch(`/api/v1/mcp/tokens/${id}`, {
        method: 'PATCH',
        body: JSON.stringify({ preferredPlayerName: nextNick.trim() || null })
      });
      setEditingId(null);
      notify(res.message || 'AI 昵称已保存');
      load();
    } catch (err) {
      notify(err instanceof Error ? err.message : '修改昵称失败', 'err');
    }
  };

  const mcpUrl = `${window.location.origin}/mcp`;
  const directMcpUrl = `${mcpUrl}?key=${newlyIssuedRaw ? encodeURIComponent(newlyIssuedRaw) : '<粘贴你签发的_ygp_mcp_Token>'}`;
  const sampleConfig = JSON.stringify(
    {
      mcpServers: {
        ygoprosharp: {
          type: 'streamable-http',
          url: directMcpUrl
        }
      }
    },
    null,
    2
  );

  return (
    <div className="space-y-6">
      <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-4">
        <h2 className="text-sm font-semibold">签发外部 AI MCP Token（严格执行权限不放大原则，仅存储单向 SHA-256）</h2>
        <form onSubmit={issue} className="grid grid-cols-1 md:grid-cols-4 gap-3 items-end">
          <div>
            <label className="block text-xs text-slate-400 mb-1">AI 决斗者昵称（选填；留空由 AI 首次进房自取名）</label>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              maxLength={18}
              placeholder="留空让 AI 自取，或填如 海马濑人"
              className="w-full px-3 py-1.5 rounded bg-slate-950 border border-slate-700 text-xs"
            />
          </div>
          <div>
            <label className="block text-xs text-slate-400 mb-1">授权 MCP 角色（上限受自身账号约束）</label>
            <select
              value={role}
              onChange={(e) => setRole(e.target.value)}
              className="w-full px-3 py-1.5 rounded bg-slate-950 border border-slate-700 text-xs"
            >
              {allowedRoles.map((r) => (
                <option key={r} value={r}>
                  {r}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="block text-xs text-slate-400 mb-1">允许房间白名单（逗号分隔，留空不限）</label>
            <input
              value={roomsText}
              onChange={(e) => setRoomsText(e.target.value)}
              placeholder="Cricket01,Tag01"
              className="w-full px-3 py-1.5 rounded bg-slate-950 border border-slate-700 text-xs"
            />
          </div>
          <button className="px-4 py-1.5 rounded bg-indigo-600 hover:bg-indigo-500 text-xs font-semibold cursor-pointer">
            签发新 Token
          </button>
        </form>

        {newlyIssuedRaw && (
          <div className="p-3 rounded-lg bg-emerald-950/70 border border-emerald-500/40 space-y-1.5 text-xs font-mono">
            <div className="flex items-center justify-between gap-2">
              <span className="break-all text-emerald-200">streamable-http 直连 URL：{directMcpUrl}</span>
              <CopyButton text={directMcpUrl} />
            </div>
            <div className="flex items-center justify-between gap-2 text-slate-300">
              <span className="break-all">纯明文 Token（仅展示一次）：{newlyIssuedRaw}</span>
              <CopyButton text={newlyIssuedRaw} />
            </div>
          </div>
        )}
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-3">
          <h3 className="text-sm font-semibold">已签发 Token 列表</h3>
          <div className="space-y-2 max-h-80 overflow-y-auto">
            {tokens.map((t) => (
              <div key={t.id} className="bg-slate-950 p-3 rounded border border-slate-800 space-y-2 text-xs">
                <div className="flex items-center justify-between gap-2">
                  <div>
                    <div className="font-semibold flex items-center gap-1.5 flex-wrap">
                      <span>{t.preferredPlayerName || t.name}</span>
                      <span className="text-indigo-400">[{t.mcpRole}]</span>
                      {t.mcpRole === 'player' && (
                        <span className="px-1.5 py-0.5 rounded text-[10px] bg-indigo-500/15 border border-indigo-500/30 text-indigo-200">
                          {t.preferredPlayerName ? `绑定昵称: ${t.preferredPlayerName}` : '待 AI 首次进房自取名'}
                        </span>
                      )}
                      {t.revokedAt && <span className="text-rose-400">(已吊销)</span>}
                    </div>
                    <div className="text-[11px] text-slate-400 font-mono mt-0.5">
                      前缀 {t.tokenPrefix}... · 归属 {t.ownerUsername}
                    </div>
                  </div>
                  {!t.revokedAt && (
                    <div className="flex items-center gap-2 shrink-0">
                      <button
                        type="button"
                        onClick={() => {
                          setEditingId(editingId === t.id ? null : t.id);
                          setEditingNick(t.preferredPlayerName || '');
                        }}
                        className="text-indigo-300 hover:underline cursor-pointer"
                      >
                        {t.preferredPlayerName ? '改名' : '指定昵称'}
                      </button>
                      <button
                        type="button"
                        onClick={async () => {
                          await apiFetch(`/api/v1/mcp/tokens/${t.id}`, { method: 'DELETE' });
                          notify('Token 已吊销');
                          load();
                        }}
                        className="text-rose-400 hover:underline cursor-pointer"
                      >
                        吊销
                      </button>
                    </div>
                  )}
                </div>

                {editingId === t.id && (
                  <div className="flex items-center gap-1.5 pt-1 border-t border-slate-800/80">
                    <input
                      value={editingNick}
                      onChange={(e) => setEditingNick(e.target.value)}
                      maxLength={18}
                      placeholder="输入新昵称，或留空交回给 AI 下次自取名"
                      className="flex-1 px-2.5 py-1 rounded bg-slate-900 border border-slate-700 text-xs"
                    />
                    <button
                      type="button"
                      onClick={() => saveNickname(t.id, editingNick)}
                      className="px-2.5 py-1 rounded bg-indigo-600 hover:bg-indigo-500 text-white font-semibold cursor-pointer"
                    >
                      保存
                    </button>
                    <button
                      type="button"
                      onClick={() => setEditingId(null)}
                      className="px-2 py-1 rounded bg-slate-800 hover:bg-slate-700 text-slate-300 cursor-pointer"
                    >
                      取消
                    </button>
                  </div>
                )}
              </div>
            ))}
          </div>
        </div>

        <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-2">
          <div className="flex items-center justify-between">
            <h3 className="text-sm font-semibold">一键复制 Claude / Gemini CLI / Cursor / ChatGPT MCP 配置</h3>
            <CopyButton text={sampleConfig} />
          </div>
          <pre className="bg-slate-950 p-3 rounded-lg border border-slate-800 text-[11px] font-mono overflow-x-auto text-slate-300">
            {sampleConfig}
          </pre>
        </div>
      </div>
    </div>
  );
}

function SecurityView({
  user,
  notify,
  onRefreshMe
}: {
  user: AuthUser;
  notify: (t: string, k?: 'ok' | 'err') => void;
  onRefreshMe: () => void;
}) {
  const [users, setUsers] = useState<any[]>([]);
  const [roles, setRoles] = useState<any[]>([]);
  const [bans, setBans] = useState<any[]>([]);
  const [audits, setAudits] = useState<any[]>([]);
  const [sessions, setSessions] = useState<any[]>([]);

  const [newUsername, setNewUsername] = useState('');
  const [newInitialPassword, setNewInitialPassword] = useState('');
  const [newRole, setNewRole] = useState('ai_operator');
  const [oneTimePwBanner, setOneTimePwBanner] = useState('');

  const [editingUser, setEditingUser] = useState<any | null>(null);
  const [editUsername, setEditUsername] = useState('');
  const [editCustomPw, setEditCustomPw] = useState('');

  const [banType, setBanType] = useState('ip');
  const [banVal, setBanVal] = useState('');
  const [banReason, setBanReason] = useState('');

  const [curPw, setCurPw] = useState('');
  const [myNewUsername, setMyNewUsername] = useState('');
  const [nextPw, setNextPw] = useState('');

  const load = useCallback(async () => {
    try {
      const s = await apiFetch('/api/v1/auth/sessions');
      setSessions(s.sessions || []);
      const r = await apiFetch('/api/v1/roles');
      setRoles(r.roles || []);

      if (hasPerm(user, 'user:manage')) {
        const u = await apiFetch('/api/v1/users');
        setUsers(u.users || []);
      }
      if (hasPerm(user, 'ban:manage')) {
        const b = await apiFetch('/api/v1/bans');
        setBans(b.bans || []);
      }
      if (hasPerm(user, 'audit:view_all')) {
        const a = await apiFetch('/api/v1/audit-logs?limit=80');
        setAudits(a.logs || []);
      }
    } catch (err) {
      notify(err instanceof Error ? err.message : '加载安全中心失败', 'err');
    }
  }, [user, notify]);

  useEffect(() => {
    load();
  }, [load]);

  const createUser = async (e: React.FormEvent) => {
    e.preventDefault();
    if (newInitialPassword && newInitialPassword.length < 6) {
      notify('自定义密码长度至少需要 6 位字符', 'err');
      return;
    }
    try {
      const customPw = newInitialPassword.trim();
      const res = await apiFetch('/api/v1/users', {
        method: 'POST',
        body: JSON.stringify({
          username: newUsername.trim() || undefined,
          displayName: newUsername.trim() || undefined,
          initialPassword: customPw || undefined,
          roleCodes: [newRole],
          mustChangePassword: !customPw
        })
      });
      const createdName = res.user?.username || newUsername || '新账号';
      if (res.generatedPassword) {
        setOneTimePwBanner(`账号 ${createdName} 创建成功，默认初始密码：${res.generatedPassword}`);
      } else {
        setOneTimePwBanner('');
        notify(`账号 ${createdName} 创建成功（已应用自定义密码，可直接登录）`);
      }
      setNewUsername('');
      setNewInitialPassword('');
      load();
    } catch (err) {
      notify(err instanceof Error ? err.message : '创建用户失败', 'err');
    }
  };

  const saveEditedUser = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingUser) return;
    if (editCustomPw && editCustomPw.length < 6) {
      notify('自定义密码长度至少需要 6 位字符', 'err');
      return;
    }
    try {
      const trimmedName = editUsername.trim();
      const trimmedPw = editCustomPw.trim();
      if (!trimmedName && !trimmedPw) {
        notify('未填写新用户名或密码，已保持原默认设置不变');
        setEditingUser(null);
        return;
      }
      if (trimmedName && trimmedName !== editingUser.username) {
        await apiFetch(`/api/v1/users/${editingUser.id}`, {
          method: 'PATCH',
          body: JSON.stringify({ username: trimmedName })
        });
      }
      if (trimmedPw) {
        await apiFetch(`/api/v1/users/${editingUser.id}/reset-password`, {
          method: 'POST',
          body: JSON.stringify({ customPassword: trimmedPw, mustChangePassword: false })
        });
      }
      notify('管理员自定义账号/密码已更新');
      if (editingUser.id === user.userId) {
        onRefreshMe();
      }
      setEditingUser(null);
      setEditUsername('');
      setEditCustomPw('');
      load();
    } catch (err) {
      notify(err instanceof Error ? err.message : '保存用户设置失败', 'err');
    }
  };

  const addBan = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      await apiFetch('/api/v1/bans', {
        method: 'POST',
        body: JSON.stringify({ targetType: banType, targetValue: banVal, reason: banReason, durationMinutes: 1440 })
      });
      setBanVal('');
      setBanReason('');
      notify('封禁规则已立即生效');
      load();
    } catch (err) {
      notify(err instanceof Error ? err.message : '新增封禁失败', 'err');
    }
  };

  const changeMyPw = async (e: React.FormEvent) => {
    e.preventDefault();
    if (nextPw && nextPw.length < 6) {
      notify('新密码长度至少需要 6 位字符', 'err');
      return;
    }
    try {
      const res = await apiFetch<{ message: string }>('/api/v1/auth/change-password', {
        method: 'POST',
        body: JSON.stringify({
          currentPassword: curPw,
          newUsername: myNewUsername.trim() || undefined,
          newPassword: nextPw || undefined
        })
      });
      setCurPw('');
      setMyNewUsername('');
      setNextPw('');
      notify(res.message);
      onRefreshMe();
      load();
    } catch (err) {
      notify(err instanceof Error ? err.message : '修改失败', 'err');
    }
  };

  return (
    <div className="space-y-6">
      {hasPerm(user, 'user:manage') && (
        <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-4">
          <h2 className="text-sm font-semibold flex items-center gap-2">
            <Users className="w-4 h-4 text-indigo-400" /> 面板用户与管理员自定义账号 / 密码（未填项自动采用默认值）
          </h2>
          <form onSubmit={createUser} className="flex flex-wrap gap-2">
            <input
              value={newUsername}
              onChange={(e) => setNewUsername(e.target.value)}
              placeholder="自定义用户名（留空自动生成默认名）"
              className="px-3 py-1.5 rounded bg-slate-950 border border-slate-700 text-xs min-w-52"
            />
            <input
              type="password"
              minLength={6}
              value={newInitialPassword}
              onChange={(e) => setNewInitialPassword(e.target.value)}
              placeholder="自定义密码 ≥6位（留空生成默认随机密码）"
              className="px-3 py-1.5 rounded bg-slate-950 border border-slate-700 text-xs min-w-60"
            />
            <select
              value={newRole}
              onChange={(e) => setNewRole(e.target.value)}
              className="px-3 py-1.5 rounded bg-slate-950 border border-slate-700 text-xs"
            >
              {roles.map((r) => (
                <option key={r.code} value={r.code}>
                  {r.name} ({r.code})
                </option>
              ))}
            </select>
            <button className="px-4 py-1.5 rounded bg-indigo-600 hover:bg-indigo-500 text-xs font-semibold cursor-pointer">
              创建账号
            </button>
          </form>

          {oneTimePwBanner && (
            <div className="p-3 rounded bg-amber-950/70 border border-amber-500/40 text-xs font-mono flex items-center justify-between">
              <span>{oneTimePwBanner}</span>
              <CopyButton text={oneTimePwBanner} />
            </div>
          )}

          {editingUser && (
            <form onSubmit={saveEditedUser} className="p-3 rounded-lg bg-slate-950 border border-indigo-500/40 flex flex-wrap items-center gap-2 text-xs">
              <span className="text-indigo-300 font-semibold">编辑账号 [{editingUser.username}]：</span>
              <input
                value={editUsername}
                onChange={(e) => setEditUsername(e.target.value)}
                placeholder={`新用户名（留空保持 ${editingUser.username}）`}
                className="px-2.5 py-1.5 rounded bg-slate-900 border border-slate-700 text-xs min-w-48"
              />
              <input
                type="password"
                minLength={6}
                value={editCustomPw}
                onChange={(e) => setEditCustomPw(e.target.value)}
                placeholder="自定义新密码 ≥6位（留空保持原密码）"
                className="px-2.5 py-1.5 rounded bg-slate-900 border border-slate-700 text-xs min-w-52"
              />
              <button className="px-3 py-1.5 rounded bg-indigo-600 hover:bg-indigo-500 font-semibold cursor-pointer">
                保存自定义设置
              </button>
              <button
                type="button"
                onClick={() => {
                  setEditingUser(null);
                  setEditUsername('');
                  setEditCustomPw('');
                }}
                className="px-2.5 py-1.5 rounded bg-slate-800 hover:bg-slate-700 cursor-pointer"
              >
                取消
              </button>
            </form>
          )}

          <div className="overflow-x-auto">
            <table className="w-full text-xs text-left">
              <thead className="text-slate-400 border-b border-slate-800">
                <tr>
                  <th className="py-2">用户名</th>
                  <th>角色</th>
                  <th>状态</th>
                  <th>最近登录</th>
                  <th>操作</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/60">
                {users.map((u) => (
                  <tr key={u.id}>
                    <td className="py-2 font-medium">{u.username}</td>
                    <td>{(u.roles || []).join(', ')}</td>
                    <td>
                      {!u.enabled ? (
                        <span className="text-rose-400">已停用</span>
                      ) : u.mustChangePassword ? (
                        <span className="text-amber-400">待首次确认</span>
                      ) : (
                        <span className="text-emerald-400">正常</span>
                      )}
                    </td>
                    <td className="text-slate-400">{u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString() : '-'}</td>
                    <td className="space-x-2">
                      <button
                        onClick={() => {
                          setEditingUser(u);
                          setEditUsername('');
                          setEditCustomPw('');
                        }}
                        className="text-indigo-400 hover:underline cursor-pointer"
                      >
                        自定义用户名/密码
                      </button>
                      <button
                        onClick={async () => {
                          const r = await apiFetch(`/api/v1/users/${u.id}/reset-password`, { method: 'POST', body: '{}' });
                          setOneTimePwBanner(`已重置 ${u.username} 的默认随机密码：${r.oneTimePassword}`);
                          load();
                        }}
                        className="text-amber-400 hover:underline cursor-pointer"
                      >
                        重置随机密码
                      </button>
                      {u.id !== user.userId && (
                        <button
                          onClick={async () => {
                            await apiFetch(`/api/v1/users/${u.id}`, {
                              method: 'PATCH',
                              body: JSON.stringify({ enabled: !u.enabled, unlockAccount: true })
                            });
                            load();
                          }}
                          className="text-sky-400 hover:underline cursor-pointer"
                        >
                          {u.enabled ? '停用' : '启用/解锁'}
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {hasPerm(user, 'ban:manage') && (
        <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-3">
          <h2 className="text-sm font-semibold flex items-center gap-2">
            <ShieldAlert className="w-4 h-4 text-rose-400" /> 游戏客户端封禁表 (IP / CIDR / 昵称)
          </h2>
          <form onSubmit={addBan} className="flex flex-wrap gap-2">
            <select value={banType} onChange={(e) => setBanType(e.target.value)} className="px-2.5 py-1.5 rounded bg-slate-950 border border-slate-700 text-xs">
              <option value="ip">IP 地址</option>
              <option value="cidr">CIDR 网段</option>
              <option value="name">玩家昵称</option>
            </select>
            <input
              value={banVal}
              onChange={(e) => setBanVal(e.target.value)}
              placeholder="如 192.168.1.50 或 BadPlayer"
              className="px-3 py-1.5 rounded bg-slate-950 border border-slate-700 text-xs"
              required
            />
            <input
              value={banReason}
              onChange={(e) => setBanReason(e.target.value)}
              placeholder="封禁原因"
              className="flex-1 px-3 py-1.5 rounded bg-slate-950 border border-slate-700 text-xs"
            />
            <button className="px-4 py-1.5 rounded bg-rose-600 text-xs font-semibold cursor-pointer">添加封禁 (24h)</button>
          </form>
          <div className="space-y-1 text-xs">
            {bans.map((b) => (
              <div key={b.id} className="flex items-center justify-between bg-slate-950 px-3 py-1.5 rounded">
                <span>
                  [{b.targetType}] <span className="font-mono text-rose-300">{b.targetValue}</span> — {b.reason}
                </span>
                <button
                  onClick={async () => {
                    await apiFetch(`/api/v1/bans/${b.id}`, { method: 'DELETE' });
                    load();
                  }}
                  className="text-emerald-400 hover:underline cursor-pointer"
                >
                  解封
                </button>
              </div>
            ))}
          </div>
        </div>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-3">
          <h3 className="text-sm font-semibold">个人账号自定义用户名与密码修改（当前：{user.username}）</h3>
          <form onSubmit={changeMyPw} className="flex flex-wrap gap-2">
            <input
              type="password"
              value={curPw}
              onChange={(e) => setCurPw(e.target.value)}
              placeholder="当前密码（必填校验）"
              className="px-2.5 py-1.5 rounded bg-slate-950 border border-slate-700 text-xs"
              required
            />
            <input
              value={myNewUsername}
              onChange={(e) => setMyNewUsername(e.target.value)}
              placeholder={`新用户名（留空保持 ${user.username}）`}
              className="px-2.5 py-1.5 rounded bg-slate-950 border border-slate-700 text-xs"
            />
            <input
              type="password"
              minLength={6}
              value={nextPw}
              onChange={(e) => setNextPw(e.target.value)}
              placeholder="新密码 ≥6位（留空保持原密码）"
              className="px-2.5 py-1.5 rounded bg-slate-950 border border-slate-700 text-xs"
            />
            <button className="px-3 py-1.5 rounded bg-indigo-600 hover:bg-indigo-500 text-xs font-semibold cursor-pointer">保存设置</button>
          </form>
          <div className="space-y-1 text-xs">
            {sessions.map((s) => (
              <div key={s.id} className="flex items-center justify-between bg-slate-950 px-2.5 py-1.5 rounded">
                <span className="font-mono">
                  {s.tokenPrefix}... {s.ip ? `(${s.ip})` : ''} {s.isCurrent && <span className="text-emerald-400">[本机]</span>}
                </span>
                {!s.isCurrent && (
                  <button
                    onClick={async () => {
                      await apiFetch(`/api/v1/auth/sessions/${s.id}`, { method: 'DELETE' });
                      load();
                    }}
                    className="text-rose-400 hover:underline cursor-pointer"
                  >
                    踢下线
                  </button>
                )}
              </div>
            ))}
          </div>
        </div>

        {hasPerm(user, 'audit:view_all') && (
          <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-2">
            <h3 className="text-sm font-semibold">全链路安全审计日志</h3>
            <div className="max-h-56 overflow-y-auto space-y-1 text-[11px] font-mono bg-slate-950 p-2.5 rounded border border-slate-800">
              {audits.map((a) => (
                <div key={a.id}>
                  <span className="text-slate-500">{new Date(a.createdAt).toLocaleTimeString()}</span>{' '}
                  <span className={a.result === 'ok' ? 'text-emerald-400' : 'text-rose-400'}>[{a.result}]</span>{' '}
                  <span className="text-indigo-300">{a.actorName}</span> → {a.action} {a.target ? `(${a.target})` : ''}
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

function MetricCard({ title, value, sub, accent }: { title: string; value: string; sub: string; accent: string }) {
  return (
    <div className="bg-slate-900 border border-slate-800 rounded-xl p-4">
      <div className="text-xs text-slate-400">{title}</div>
      <div className={`text-lg font-bold mt-1 ${accent}`}>{value}</div>
      <div className="text-[11px] text-slate-400 mt-1">{sub}</div>
    </div>
  );
}

function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <button
      type="button"
      onClick={(e) => {
        e.stopPropagation();
        navigator.clipboard?.writeText(text);
        setCopied(true);
        setTimeout(() => setCopied(false), 1500);
      }}
      className="p-1 text-slate-400 hover:text-white cursor-pointer"
      title="复制"
    >
      {copied ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
    </button>
  );
}
