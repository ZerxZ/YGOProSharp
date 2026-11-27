import React, { useEffect, useState, useCallback, useRef } from 'react';
import {
  Alert,
  Button,
  Card,
  Chip,
  Description,
  Input,
  Label,
  Spinner,
  TextField,
  Tooltip
} from '@heroui/react';
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
import { ConsoleLogEntry, WorkspaceShell } from './components/WorkspaceShell';

type TabKey = 'dashboard' | 'rooms' | 'studio' | 'mcp' | 'security';

export default function App() {
  const [pathname, setPathname] = useState<string>(() => window.location.pathname || '/');
  const [surface, setSurface] = useState<SurfaceInfo | null>(null);
  const [surfaceChecked, setSurfaceChecked] = useState(false);
  const [user, setUser] = useState<AuthUser | null>(null);
  const [adminAuthChecked, setAdminAuthChecked] = useState(false);
  const [activeTab, setActiveTab] = useState<TabKey>('dashboard');
  const [toast, setToast] = useState<{ text: string; kind: 'ok' | 'err' } | null>(null);
  const [adminLogs, setAdminLogs] = useState<ConsoleLogEntry[]>([]);
  const wsRef = useRef<WebSocket | null>(null);

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

  useEffect(() => {
    if (shouldRenderPortal || !user || user.mustChangePassword) return;
    const proto = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const ws = new WebSocket(`${proto}//${window.location.host}/ws/events`);
    ws.onmessage = (ev) => {
      try {
        const msg = JSON.parse(ev.data);
        const now = new Date().toLocaleTimeString('zh-CN', { hour12: false });
        setAdminLogs((prev) =>
          [
            {
              id: `${Date.now()}-${Math.random()}`,
              time: now,
              level: 'info' as const,
              source: String(msg.type || 'event'),
              message: JSON.stringify(msg.payload ?? msg).slice(0, 180)
            },
            ...prev
          ].slice(0, 50)
        );
      } catch {
        // ignore malformed frames
      }
    };
    wsRef.current = ws;
    return () => {
      wsRef.current?.close();
    };
  }, [shouldRenderPortal, user]);

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
      <div className="min-h-screen flex items-center justify-center gap-2.5 bg-background text-muted text-xs">
        <Spinner size="sm" />
        <span>正在初始化工作台...</span>
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
      <div className="admin-theme min-h-screen flex items-center justify-center gap-2.5 bg-background admin-ambient-bg text-muted text-xs">
        <Spinner size="sm" />
        <span>正在验证管理控制面会话...</span>
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
      icon: <Activity className="w-3.5 h-3.5" />,
      show: hasPerm(user, 'dashboard:view')
    },
    {
      key: 'rooms',
      label: '房间 / 裁判 / 录像',
      shortLabel: '房间',
      icon: <Gamepad2 className="w-3.5 h-3.5" />,
      show: hasPerm(user, 'room:view')
    },
    {
      key: 'studio',
      label: '卡组池 & DIY 工作台',
      shortLabel: '工作台',
      icon: <Layers className="w-3.5 h-3.5" />,
      show: hasPerm(user, 'deck_pool:view') || hasPerm(user, 'diy:view') || hasPerm(user, 'card:search')
    },
    {
      key: 'mcp',
      label: 'MCP 接入中心',
      shortLabel: 'MCP',
      icon: <KeyRound className="w-3.5 h-3.5" />,
      show: hasPerm(user, 'mcp_token:issue_self') || hasPerm(user, 'mcp_token:manage_all')
    },
    {
      key: 'security',
      label: '用户 / 封禁 / 审计',
      shortLabel: '安全',
      icon: <Shield className="w-3.5 h-3.5" />,
      show: true
    }
  ];

  const visibleTabs = tabs.filter((t) => t.show);
  const currentTabObj = visibleTabs.find((t) => t.key === activeTab) || visibleTabs[0];

  return (
    <WorkspaceShell
      themeClass="admin-theme"
      ambientClass="admin-ambient-bg"
      monogram="YP"
      monogramClass="bg-indigo-500/20 text-indigo-300 border-indigo-500/35"
      workspaceName="YGOProSharp"
      workspaceBranchTag="/admin"
      breadcrumbCurrent={currentTabObj?.label || '管理控制台'}
      statusBadge={
        user.isStreamerOnly ? (
          <Chip size="sm" variant="soft" color="warning" className="h-5 px-2 text-[10px]">
            <EyeOff className="w-3 h-3" />
            <Chip.Label>直播脱敏</Chip.Label>
          </Chip>
        ) : (
          <Chip size="sm" variant="soft" color="accent" className="h-5 px-2 text-[10px] font-mono">
            RBAC: {user.roleCodes.join(',')}
          </Chip>
        )
      }
      navItems={visibleTabs.map((t) => ({
        key: t.key,
        label: t.label,
        shortLabel: t.shortLabel,
        icon: t.icon
      }))}
      activeNavKey={activeTab}
      onSelectNav={(k) => {
        setActiveTab(k as TabKey);
        window.scrollTo({ top: 0, behavior: 'smooth' });
      }}
      sidebarFooterTop={
        <div className="flex items-center justify-between gap-2 px-1.5 py-1 rounded-lg bg-surface border border-border/70 text-xs">
          <div className="min-w-0">
            <div className="font-semibold text-foreground truncate">{user.displayName}</div>
            <div className="text-[10px] text-muted font-mono truncate">{user.roleCodes.join(', ')}</div>
          </div>
          <Tooltip delay={200}>
            <Button
              isIconOnly
              size="sm"
              variant="ghost"
              aria-label="退出登录"
              className="h-6 w-6 min-w-0 text-muted data-[hovered=true]:text-danger"
              onPress={handleLogout}
            >
              <LogOut className="w-3.5 h-3.5" />
            </Button>
            <Tooltip.Content>
              <p className="text-xs">退出管理控制台</p>
            </Tooltip.Content>
          </Tooltip>
        </div>
      }
      sidebarUtilityLeft={
        <span className="text-[11px] font-mono text-muted truncate">
          Port :{surface?.gamePort ?? 7911} / :{window.location.port || surface?.adminPort || 7912}
        </span>
      }
      sidebarUtilityRight={
        surface?.unifiedPortal !== false ? (
          <Tooltip delay={200}>
            <Button
              isIconOnly
              size="sm"
              variant="ghost"
              aria-label="返回玩家大厅"
              className="h-7 w-7 min-w-0 text-emerald-400"
              onPress={() => navigateTo('/')}
            >
              <Home className="w-3.5 h-3.5" />
            </Button>
            <Tooltip.Content>
              <p className="text-xs">返回前台玩家大厅 (/)</p>
            </Tooltip.Content>
          </Tooltip>
        ) : undefined
      }
      headerRightActions={
        surface?.unifiedPortal !== false ? (
          <Button size="sm" variant="secondary" className="h-7 px-2.5 text-xs" onPress={() => navigateTo('/')}>
            <Home className="w-3.5 h-3.5 text-emerald-400" />
            <span className="hidden sm:inline">玩家大厅</span>
          </Button>
        ) : undefined
      }
      consoleLogs={adminLogs}
      consoleStatusText={`GameServer :${surface?.gamePort ?? 7911} · ControlPlane :${window.location.port || 7912}`}
      toast={
        toast ? (
          <div className="fixed bottom-16 md:bottom-10 right-4 left-4 sm:left-auto sm:max-w-md z-50 animate-slide-up">
            <Alert status={toast.kind === 'ok' ? 'success' : 'danger'} className="shadow-2xl">
              <Alert.Indicator />
              <Alert.Content>
                <Alert.Title className="text-xs">{toast.text}</Alert.Title>
              </Alert.Content>
            </Alert>
          </div>
        ) : null
      }
    >
      {activeTab === 'dashboard' && <DashboardView user={user} notify={notify} />}
      {activeTab === 'rooms' && <RoomsView user={user} notify={notify} />}
      {activeTab === 'studio' && <StudioView user={user} notify={notify} />}
      {activeTab === 'mcp' && <McpHubView user={user} notify={notify} />}
      {activeTab === 'security' && <SecurityView user={user} notify={notify} onRefreshMe={refreshMe} />}
    </WorkspaceShell>
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
    <div className="admin-theme min-h-screen flex items-center justify-center bg-background admin-ambient-bg p-4">
      <Card className="w-full max-w-md rounded-2xl p-6 shadow-2xl animate-slide-up">
        <Card.Header className="p-0 flex-row items-center justify-between gap-3">
          <div className="flex items-center gap-2.5 min-w-0">
            <span className="w-8 h-8 rounded-lg bg-indigo-500/20 border border-indigo-500/35 text-indigo-300 flex items-center justify-center font-extrabold text-xs shrink-0">
              YP
            </span>
            <div className="min-w-0">
              <Card.Title className="text-base font-bold truncate">管理控制台 (/admin)</Card.Title>
              <Card.Description className="text-[11px] truncate">
                Argon2id 认证 · 细粒度 RBAC · 零外发大模型密钥
              </Card.Description>
            </div>
          </div>
          {onBackToPortal && (
            <Button size="sm" variant="secondary" onPress={onBackToPortal} className="shrink-0 text-xs">
              返回玩家页
            </Button>
          )}
        </Card.Header>

        <Card.Content className="p-0 pt-5 space-y-4">
          {error && (
            <Alert status="danger">
              <Alert.Indicator />
              <Alert.Content>
                <Alert.Description className="text-xs">{error}</Alert.Description>
              </Alert.Content>
            </Alert>
          )}

          <form id="admin-login-form" onSubmit={submit} className="space-y-3.5">
            <TextField fullWidth isRequired value={username} onChange={setUsername}>
              <Label className="text-xs">管理员 / 成员账号</Label>
              <Input variant="secondary" placeholder="admin" />
            </TextField>

            <TextField fullWidth isRequired type="password" value={password} onChange={setPassword}>
              <Label className="text-xs">登录密码</Label>
              <Input variant="secondary" placeholder="输入管理控制台密码" />
              <Description className="text-[11px]">首次启动请查看服务端启动日志中的一次性初始密码</Description>
            </TextField>
          </form>
        </Card.Content>

        <Card.Footer className="p-0 pt-5">
          <Button type="submit" form="admin-login-form" variant="primary" fullWidth isPending={loading}>
            {loading && <Spinner color="current" size="sm" />}
            {loading ? '正在验证...' : '安全登录'}
          </Button>
        </Card.Footer>
      </Card>
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
    <div className="admin-theme min-h-screen flex items-center justify-center bg-background admin-ambient-bg p-4">
      <Card className="w-full max-w-md border border-warning/40 rounded-2xl p-6 shadow-2xl animate-slide-up">
        <Card.Header className="p-0 space-y-1.5">
          <Card.Title className="flex items-center gap-2 text-warning text-sm sm:text-base font-bold">
            <Lock className="w-4 h-4 shrink-0" /> 首次安全引导：管理员账号与密码设置
          </Card.Title>
          <Card.Description className="text-xs leading-relaxed">
            当前默认账号为 <span className="text-foreground font-mono">{user.username}</span>
            。可自定义管理员用户名与新密码（最少 6 位字符）；未填写的项将自动保留默认设置。
          </Card.Description>
        </Card.Header>

        <Card.Content className="p-0 pt-4 space-y-3.5">
          {error && (
            <Alert status="danger">
              <Alert.Indicator />
              <Alert.Content>
                <Alert.Description className="text-xs">{error}</Alert.Description>
              </Alert.Content>
            </Alert>
          )}

          <form id="force-pw-form" onSubmit={submit} className="space-y-3.5">
            <TextField
              fullWidth
              isRequired
              type="password"
              value={currentPassword}
              onChange={setCurrentPassword}
            >
              <Label className="text-xs">当前初始/临时密码（必填校验）</Label>
              <Input variant="secondary" placeholder="输入当前登录使用的初始密码" />
            </TextField>

            <TextField fullWidth value={customUsername} onChange={setCustomUsername}>
              <Label className="text-xs">自定义管理员用户名（选填）</Label>
              <Input variant="secondary" placeholder={`留空默认使用 ${user.username}`} />
            </TextField>

            <TextField fullWidth type="password" value={newPassword} onChange={setNewPassword}>
              <Label className="text-xs">自定义新密码（最少 6 位；选填）</Label>
              <Input variant="secondary" placeholder="不填则保持当前默认密码" />
            </TextField>

            {newPassword && (
              <TextField
                fullWidth
                isRequired
                type="password"
                value={confirmPassword}
                onChange={setConfirmPassword}
              >
                <Label className="text-xs">确认自定义新密码</Label>
                <Input variant="secondary" placeholder="再次输入新密码" />
              </TextField>
            )}
          </form>
        </Card.Content>

        <Card.Footer className="p-0 pt-5 gap-2.5">
          <Button variant="tertiary" onPress={onLogout}>
            退出
          </Button>
          <Button type="submit" form="force-pw-form" variant="primary" isPending={loading} className="flex-1">
            {loading && <Spinner color="current" size="sm" />}
            {loading ? '正在保存...' : '保存设置并进入控制台'}
          </Button>
        </Card.Footer>
      </Card>
    </div>
  );
}
