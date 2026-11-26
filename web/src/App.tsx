import React, { useEffect, useState, useCallback } from 'react';
import {
  Activity,
  EyeOff,
  Gamepad2,
  Home,
  KeyRound,
  Layers,
  Lock,
  LogOut,
  Shield
} from 'lucide-react';
import { apiFetch, AuthUser, hasPerm, setCsrfToken } from './api';
import { SurfaceInfo, UserPortalApp } from './PortalView';
import { DashboardView, RoomsView } from './admin/AdminDashboardAndRooms';
import { StudioView } from './admin/AdminStudioView';
import { McpHubView, SecurityView } from './admin/AdminMcpAndSecurity';

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
    window.scrollTo({ top: 0, behavior: 'smooth' });
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
      <div className="min-h-screen flex items-center justify-center bg-slate-950 text-slate-400 text-sm">
        正在加载页面...
      </div>
    );
  }

  if (shouldRenderPortal && surface) {
    return (
      <UserPortalApp
        surfaceInfo={surface}
        onNavigateAdmin={surface.unifiedPortal !== false ? () => navigateTo('/admin') : undefined}
      />
    );
  }

  if (!adminAuthChecked) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-slate-950 admin-ambient-bg text-slate-400 text-sm">
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

  const tabs: { key: TabKey; label: string; shortLabel: string; icon: React.ReactNode; show: boolean }[] = [
    {
      key: 'dashboard',
      label: '总览与缓存',
      shortLabel: '总览',
      icon: <Activity className="w-4 h-4" />,
      show: hasPerm(user, 'dashboard:view')
    },
    {
      key: 'rooms',
      label: '房间 / 裁判 / 录像',
      shortLabel: '房间',
      icon: <Gamepad2 className="w-4 h-4" />,
      show: hasPerm(user, 'room:view')
    },
    {
      key: 'studio',
      label: '卡组池 & DIY 工作台',
      shortLabel: '工作台',
      icon: <Layers className="w-4 h-4" />,
      show: hasPerm(user, 'deck_pool:view') || hasPerm(user, 'diy:view') || hasPerm(user, 'card:search')
    },
    {
      key: 'mcp',
      label: 'MCP 接入中心',
      shortLabel: 'MCP',
      icon: <KeyRound className="w-4 h-4" />,
      show: hasPerm(user, 'mcp_token:issue_self') || hasPerm(user, 'mcp_token:manage_all')
    },
    {
      key: 'security',
      label: '用户 / 封禁 / 审计',
      shortLabel: '安全',
      icon: <Shield className="w-4 h-4" />,
      show: true
    }
  ];

  const visibleTabs = tabs.filter((t) => t.show);

  return (
    <div className="min-h-screen bg-slate-950 admin-ambient-bg text-slate-100 flex flex-col">
      {/* 顶部毛玻璃导航栏 */}
      <header className="border-b border-white/[0.08] bg-slate-950/80 backdrop-blur-xl sticky top-0 z-30">
        <div className="max-w-7xl mx-auto px-3 sm:px-4 h-14 sm:h-16 flex items-center justify-between gap-2 sm:gap-4">
          <div className="flex items-center gap-2.5 min-w-0">
            <div className="w-9 h-9 rounded-xl bg-gradient-to-br from-indigo-500 to-purple-600 flex items-center justify-center font-black text-white shadow-lg shadow-indigo-950/60 shrink-0">
              Y
            </div>
            <div className="min-w-0">
              <div className="font-bold text-sm sm:text-base flex items-center gap-1.5 sm:gap-2">
                <span className="truncate">管理后台</span>
                <span className="px-1.5 py-0.5 rounded-md text-[10px] font-mono bg-indigo-500/20 text-indigo-300 border border-indigo-500/30 shrink-0">
                  /admin
                </span>
                {user.isStreamerOnly && (
                  <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] bg-amber-500/20 text-amber-300 border border-amber-500/30 shrink-0">
                    <EyeOff className="w-3 h-3" /> 直播脱敏
                  </span>
                )}
              </div>
              <div className="text-[11px] text-slate-400 truncate hidden sm:block">
                游戏服 :{surface?.gamePort ?? 7911} · 控制面 :{window.location.port || surface?.adminPort || 7912}
              </div>
            </div>
          </div>

          {/* 桌面端胶囊导航 */}
          <nav className="hidden md:flex items-center gap-1 p-1 rounded-2xl bg-slate-900/90 border border-white/[0.06]">
            {visibleTabs.map((t) => (
              <button
                key={t.key}
                onClick={() => setActiveTab(t.key)}
                className={`px-3 py-1.5 rounded-xl text-xs font-semibold flex items-center gap-1.5 transition cursor-pointer ${
                  activeTab === t.key
                    ? 'bg-indigo-600 text-white shadow-md shadow-indigo-950/60'
                    : 'text-slate-300 hover:bg-slate-800/70 hover:text-white'
                }`}
              >
                {t.icon}
                <span>{t.label}</span>
              </button>
            ))}
          </nav>

          {/* 右侧返回玩家大厅 + 账号退出 */}
          <div className="flex items-center gap-1.5 sm:gap-2.5 shrink-0">
            {surface?.unifiedPortal !== false ? (
              <button
                type="button"
                onClick={() => navigateTo('/')}
                className="inline-flex items-center gap-1 px-2.5 py-1.5 rounded-xl bg-emerald-500/15 hover:bg-emerald-500/25 border border-emerald-500/30 text-emerald-300 text-xs font-medium cursor-pointer"
                title="返回前台玩家大厅 (/)"
              >
                <Home className="w-3.5 h-3.5" />
                <span className="hidden sm:inline">玩家大厅</span>
              </button>
            ) : (
              surface?.portalPort && (
                <a
                  href={`${window.location.protocol}//${window.location.hostname}:${surface.portalPort}`}
                  target="_blank"
                  rel="noreferrer"
                  className="inline-flex items-center gap-1 px-2.5 py-1.5 rounded-xl bg-emerald-500/15 border border-emerald-500/30 text-emerald-300 text-xs font-medium"
                >
                  玩家大厅 ↗
                </a>
              )
            )}

            <div className="text-right hidden lg:block">
              <div className="text-xs font-medium text-slate-200">{user.displayName}</div>
              <div className="text-[11px] text-slate-400">{user.roleCodes.join(', ')}</div>
            </div>
            <button
              onClick={handleLogout}
              className="p-2 rounded-xl text-slate-400 hover:text-rose-400 hover:bg-slate-800 transition cursor-pointer"
              title="退出登录"
            >
              <LogOut className="w-4 h-4" />
            </button>
          </div>
        </div>
      </header>

      {/* 悬浮提示条（移动端避让底部导航栏） */}
      {toast && (
        <div
          className={`fixed bottom-20 md:bottom-6 right-4 left-4 sm:left-auto z-50 px-4 py-3 rounded-2xl shadow-2xl border text-xs sm:text-sm font-medium backdrop-blur-xl animate-slide-up ${
            toast.kind === 'ok'
              ? 'bg-emerald-950/90 border-emerald-500/45 text-emerald-100'
              : 'bg-rose-950/90 border-rose-500/45 text-rose-100'
          }`}
        >
          {toast.text}
        </div>
      )}

      <main className="max-w-7xl w-full mx-auto px-3 sm:px-4 pt-4 sm:pt-6 pb-mobile-nav flex-1">
        {activeTab === 'dashboard' && <DashboardView user={user} notify={notify} />}
        {activeTab === 'rooms' && <RoomsView user={user} notify={notify} />}
        {activeTab === 'studio' && <StudioView user={user} notify={notify} />}
        {activeTab === 'mcp' && <McpHubView user={user} notify={notify} />}
        {activeTab === 'security' && <SecurityView user={user} notify={notify} onRefreshMe={refreshMe} />}
      </main>

      {/* 手机端底部固定 Tab 导航栏 (md:hidden) */}
      <nav className="md:hidden fixed bottom-0 inset-x-0 z-40 bg-slate-950/92 backdrop-blur-xl border-t border-white/[0.08] pb-safe">
        <div
          className="grid h-14"
          style={{ gridTemplateColumns: `repeat(${Math.max(1, visibleTabs.length)}, minmax(0, 1fr))` }}
        >
          {visibleTabs.map((t) => {
            const active = activeTab === t.key;
            return (
              <button
                key={t.key}
                type="button"
                onClick={() => {
                  setActiveTab(t.key);
                  window.scrollTo({ top: 0, behavior: 'smooth' });
                }}
                className={`relative flex flex-col items-center justify-center gap-0.5 text-[10px] font-semibold transition cursor-pointer ${
                  active ? 'text-indigo-300' : 'text-slate-400 active:text-slate-200'
                }`}
              >
                {active && (
                  <span className="absolute top-0 w-7 h-0.5 rounded-full bg-indigo-400 shadow-[0_0_8px_rgba(129,140,248,0.9)]" />
                )}
                {t.icon}
                <span>{t.shortLabel}</span>
              </button>
            );
          })}
        </div>
      </nav>
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
    <div className="min-h-screen flex items-center justify-center bg-slate-950 admin-ambient-bg p-4">
      <form
        onSubmit={submit}
        className="w-full max-w-md glass-card rounded-3xl p-6 shadow-2xl space-y-5 animate-slide-up"
      >
        <div className="flex items-center justify-between gap-3">
          <div className="flex items-center gap-3 min-w-0">
            <div className="w-10 h-10 rounded-2xl bg-gradient-to-br from-indigo-500 to-purple-600 flex items-center justify-center font-black text-lg text-white shrink-0 shadow-lg shadow-indigo-950/60">
              Y
            </div>
            <div className="min-w-0">
              <h1 className="text-base sm:text-lg font-bold truncate">管理控制台 (/admin)</h1>
              <p className="text-[11px] text-slate-400 truncate">Argon2id 认证 · 细粒度 RBAC · 零外发大模型密钥</p>
            </div>
          </div>
          {onBackToPortal && (
            <button
              type="button"
              onClick={onBackToPortal}
              className="px-3 py-1.5 rounded-xl bg-slate-800/90 hover:bg-slate-700 text-xs font-medium text-emerald-300 cursor-pointer shrink-0"
            >
              返回玩家页
            </button>
          )}
        </div>

        {error && (
          <div className="p-3 rounded-xl bg-rose-950/70 border border-rose-500/40 text-xs text-rose-200">{error}</div>
        )}

        <div className="space-y-3.5">
          <div>
            <label className="block text-xs text-slate-400 mb-1">管理员 / 成员账号</label>
            <input
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              className="w-full px-3.5 py-2.5 text-sm input-modern input-modern-indigo"
              required
            />
          </div>
          <div>
            <label className="block text-xs text-slate-400 mb-1">
              登录密码（首次启动请查看服务端启动日志中的一次性密码）
            </label>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="w-full px-3.5 py-2.5 text-sm input-modern input-modern-indigo"
              required
            />
          </div>
        </div>

        <button
          disabled={loading}
          className="w-full min-h-[44px] py-2.5 rounded-xl bg-gradient-to-r from-indigo-600 to-purple-600 hover:from-indigo-500 hover:to-purple-500 disabled:opacity-50 text-sm font-semibold text-white shadow-lg shadow-indigo-950/60 transition cursor-pointer"
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
    <div className="min-h-screen flex items-center justify-center bg-slate-950 admin-ambient-bg p-4">
      <form
        onSubmit={submit}
        className="w-full max-w-md glass-card border border-amber-500/40 rounded-3xl p-6 shadow-2xl space-y-4 animate-slide-up"
      >
        <div className="flex items-center gap-2 text-amber-300 font-bold text-sm sm:text-base">
          <Lock className="w-5 h-5 shrink-0" /> 首次安全引导：管理员账号与密码设置
        </div>
        <p className="text-xs text-slate-400 leading-relaxed">
          当前默认账号为 <span className="text-slate-200 font-mono">{user.username}</span>
          。可自定义管理员用户名与新密码（最少 6 位字符）；未填写的项将自动保留默认设置。
        </p>
        {error && (
          <div className="p-3 rounded-xl bg-rose-950/70 border border-rose-500/40 text-xs text-rose-200">{error}</div>
        )}
        <div>
          <label className="block text-xs text-slate-400 mb-1">当前初始/临时密码（必填校验）</label>
          <input
            type="password"
            placeholder="输入当前登录使用的初始密码"
            value={currentPassword}
            onChange={(e) => setCurrentPassword(e.target.value)}
            className="w-full px-3.5 py-2.5 text-sm input-modern input-modern-indigo"
            required
          />
        </div>
        <div>
          <label className="block text-xs text-slate-400 mb-1">
            自定义管理员用户名（选填，留空保持 {user.username}）
          </label>
          <input
            placeholder={`留空默认使用 ${user.username}`}
            value={customUsername}
            onChange={(e) => setCustomUsername(e.target.value)}
            className="w-full px-3.5 py-2.5 text-sm input-modern input-modern-indigo"
          />
        </div>
        <div>
          <label className="block text-xs text-slate-400 mb-1">
            自定义新密码（最少 6 位；选填，留空保留当前密码）
          </label>
          <input
            type="password"
            minLength={6}
            placeholder="至少 6 位字符（不填则保持当前默认密码）"
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
            className="w-full px-3.5 py-2.5 text-sm input-modern input-modern-indigo"
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
              className="w-full px-3.5 py-2.5 text-sm input-modern input-modern-indigo"
              required
            />
          </div>
        )}
        <div className="flex gap-2.5 pt-1">
          <button
            type="button"
            onClick={onLogout}
            className="min-h-[42px] px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-xs font-medium cursor-pointer"
          >
            退出
          </button>
          <button
            disabled={loading}
            className="flex-1 min-h-[42px] py-2 rounded-xl bg-amber-500 hover:bg-amber-400 text-slate-950 font-bold text-sm cursor-pointer"
          >
            {loading ? '正在保存...' : '保存设置并进入控制台'}
          </button>
        </div>
      </form>
    </div>
  );
}
