import React, { useEffect, useState, useCallback, useMemo } from 'react';
import {
  Alert,
  Button,
  Card,
  Chip,
  Input,
  Label,
  SearchField,
  Spinner,
  Surface,
  Tabs,
  TextField
} from '@heroui/react';
import { FileText, KeyRound, Lock, Plus, RefreshCw, ShieldAlert, Terminal, Users } from 'lucide-react';
import { apiFetch, AuthUser, hasPerm } from '../api';
import { QuickCopyPill } from '../RoomConfigurator';

export function McpHubView({ user: _user, notify }: { user: AuthUser; notify: (t: string, k?: 'ok' | 'err') => void }) {
  const [tokens, setTokens] = useState<any[]>([]);
  const [allowedRoles, setAllowedRoles] = useState<string[]>([]);
  const [tokenSearch, setTokenSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<'active' | 'all'>('active');
  const [snippetTab, setSnippetTab] = useState<'json' | 'cli' | 'matrix'>('json');

  const [name, setName] = useState('');
  const [personaStyle, setPersonaStyle] = useState('');
  const [role, setRole] = useState('player');
  const [roomsText, setRoomsText] = useState('');
  const [issuing, setIssuing] = useState(false);
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
    setIssuing(true);
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
          personaStyle: personaStyle.trim() || undefined,
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
    } finally {
      setIssuing(false);
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

  const visibleTokens = useMemo(() => {
    return tokens.filter((t) => {
      if (statusFilter === 'active' && t.revokedAt) return false;
      if (tokenSearch.trim()) {
        const kw = tokenSearch.trim().toLowerCase();
        return (
          String(t.preferredPlayerName || t.name || '').toLowerCase().includes(kw) ||
          String(t.ownerUsername || '').toLowerCase().includes(kw) ||
          String(t.mcpRole || '').toLowerCase().includes(kw) ||
          String(t.tokenPrefix || '').toLowerCase().includes(kw)
        );
      }
      return true;
    });
  }, [tokens, statusFilter, tokenSearch]);

  const mcpUrl = `${window.location.origin}/mcp`;
  const directMcpUrl = `${mcpUrl}?key=${newlyIssuedRaw ? encodeURIComponent(newlyIssuedRaw) : '<粘贴签发的_ygp_mcp_Token>'}`;
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
  const cliSnippet = `claude mcp add --transport http ygoprosharp "${directMcpUrl}"`;

  return (
    <div className="grid grid-cols-1 lg:grid-cols-12 gap-4 items-start text-xs animate-fade-in">
      {/* 左侧 7 列：Token 签发 + 全服 Token 管控表 */}
      <div className="lg:col-span-7 space-y-4">
        <Card className="rounded-xl p-4 sm:p-5 space-y-4">
          <Card.Header className="p-0 flex-row items-center justify-between gap-2">
            <div>
              <Card.Title className="text-sm font-bold flex items-center gap-2">
                <KeyRound className="w-4 h-4 text-indigo-400 shrink-0" />
                <span>签发外部 AI MCP Token</span>
              </Card.Title>
              <Card.Description className="text-[11px] mt-0.5">
                严格执行权限不放大原则，服务端仅落盘 SHA-256 哈希并以常量时间比对校验
              </Card.Description>
            </div>
            <Chip size="sm" variant="soft" color="accent" className="font-mono text-[10px] shrink-0">
              SHA-256 Only
            </Chip>
          </Card.Header>

          <form onSubmit={issue} className="space-y-3">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <TextField fullWidth value={name} onChange={setName}>
                <Label className="text-xs">AI 决斗者昵称（留空自取）</Label>
                <Input variant="secondary" maxLength={18} placeholder="留空自取，或填 海马濑人" />
              </TextField>

              <TextField fullWidth value={personaStyle} onChange={setPersonaStyle}>
                <Label className="text-xs">性格与吐槽人设（选填）</Label>
                <Input variant="secondary" maxLength={100} placeholder="如：毒舌技术流 / 热血决斗王" />
              </TextField>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 items-end">
              <div className="space-y-1">
                <label className="block text-xs font-medium text-foreground">授权 MCP 角色</label>
                <select
                  value={role}
                  onChange={(e) => setRole(e.target.value)}
                  className="w-full h-9 px-3 text-xs input-modern input-modern-indigo"
                >
                  {allowedRoles.map((r) => (
                    <option key={r} value={r}>
                      {r}
                    </option>
                  ))}
                </select>
              </div>

              <TextField fullWidth value={roomsText} onChange={setRoomsText}>
                <Label className="text-xs">房间白名单（选填）</Label>
                <Input variant="secondary" placeholder="Cricket01,Tag01" />
              </TextField>

              <Button type="submit" variant="primary" isPending={issuing} className="w-full">
                {issuing ? <Spinner color="current" size="sm" /> : <Plus className="w-3.5 h-3.5" />}
                <span>签发新 Token</span>
              </Button>
            </div>
          </form>

          {newlyIssuedRaw && (
            <Alert status="success" className="animate-fade-in">
              <Alert.Indicator />
              <Alert.Content className="space-y-2">
                <Alert.Title className="text-xs font-semibold">明文 Token 仅展示一次，请立即复制保存</Alert.Title>
                <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 p-2.5 rounded-lg bg-background border border-border font-mono text-[11px]">
                  <span className="break-all text-emerald-400">{directMcpUrl}</span>
                  <QuickCopyPill text={directMcpUrl} label="复制直连 URL" onCopied={() => notify('已复制直连 URL')} />
                </div>
                <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 font-mono text-[11px] text-muted">
                  <span className="break-all">纯 Token: {newlyIssuedRaw}</span>
                  <QuickCopyPill text={newlyIssuedRaw} label="复制纯 Token" onCopied={() => notify('已复制纯 Token')} />
                </div>
              </Alert.Content>
            </Alert>
          )}
        </Card>

        <Card className="rounded-xl p-4 sm:p-5 space-y-3.5">
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2.5">
            <div className="flex items-center gap-2">
              <h3 className="text-sm font-bold">已签发 Token 列表 ({visibleTokens.length})</h3>
              <Button isIconOnly size="sm" variant="ghost" aria-label="刷新令牌列表" className="h-7 w-7 min-w-0" onPress={load}>
                <RefreshCw className="w-3.5 h-3.5" />
              </Button>
            </div>

            <div className="flex items-center gap-2">
              <Tabs
                selectedKey={statusFilter}
                onSelectionChange={(k) => setStatusFilter(String(k) as 'active' | 'all')}
              >
                <Tabs.ListContainer>
                  <Tabs.List aria-label="Token 状态过滤">
                    <Tabs.Tab id="active" className="text-xs px-2.5">
                      有效
                      <Tabs.Indicator />
                    </Tabs.Tab>
                    <Tabs.Tab id="all" className="text-xs px-2.5">
                      全部 ({tokens.length})
                      <Tabs.Indicator />
                    </Tabs.Tab>
                  </Tabs.List>
                </Tabs.ListContainer>
              </Tabs>

              <SearchField
                aria-label="筛选 Token"
                variant="secondary"
                value={tokenSearch}
                onChange={setTokenSearch}
                className="w-40 sm:w-48"
              >
                <SearchField.Group>
                  <SearchField.SearchIcon />
                  <SearchField.Input placeholder="搜昵称/归属/前缀..." className="text-xs" />
                  <SearchField.ClearButton />
                </SearchField.Group>
              </SearchField>
            </div>
          </div>

          <div className="space-y-2 max-h-[480px] overflow-y-auto pr-0.5">
            {visibleTokens.map((t) => (
              <Surface key={t.id} variant="secondary" className="p-3.5 rounded-xl border border-border space-y-2">
                <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
                  <div className="min-w-0 space-y-1">
                    <div className="font-semibold flex items-center gap-1.5 flex-wrap">
                      <span className="text-foreground text-sm">{t.preferredPlayerName || t.name}</span>
                      <Chip size="sm" variant="soft" color="accent" className="h-5 px-1.5 font-mono text-[10px]">
                        {t.mcpRole}
                      </Chip>
                      {t.mcpRole === 'player' && (
                        <Chip
                          size="sm"
                          variant="soft"
                          color={t.preferredPlayerName ? 'success' : 'warning'}
                          className="h-5 px-2 text-[10px]"
                        >
                          {t.preferredPlayerName ? `绑定: ${t.preferredPlayerName}` : '待进房自取名'}
                        </Chip>
                      )}
                      {t.revokedAt && (
                        <Chip size="sm" variant="soft" color="danger" className="h-5 px-2 text-[10px]">
                          已吊销
                        </Chip>
                      )}
                    </div>
                    <div className="text-[11px] text-muted font-mono">
                      前缀 {t.tokenPrefix}... · 归属 @{t.ownerUsername} · 房间 {(t.allowedRooms || []).join(', ') || '不限'}
                    </div>
                  </div>

                  {!t.revokedAt && (
                    <div className="flex items-center gap-1.5 shrink-0">
                      <Button
                        size="sm"
                        variant="secondary"
                        className="h-7 px-2.5 text-[11px]"
                        onPress={() => {
                          setEditingId(editingId === t.id ? null : t.id);
                          setEditingNick(t.preferredPlayerName || '');
                        }}
                      >
                        {t.preferredPlayerName ? '改名' : '指定昵称'}
                      </Button>
                      <Button
                        size="sm"
                        variant="danger-soft"
                        className="h-7 px-2.5 text-[11px]"
                        onPress={async () => {
                          await apiFetch(`/api/v1/mcp/tokens/${t.id}`, { method: 'DELETE' });
                          notify('Token 已吊销');
                          load();
                        }}
                      >
                        吊销
                      </Button>
                    </div>
                  )}
                </div>

                {editingId === t.id && (
                  <div className="flex flex-col sm:flex-row items-stretch sm:items-end gap-2 pt-2 border-t border-separator animate-fade-in">
                    <TextField fullWidth value={editingNick} onChange={setEditingNick} className="flex-1">
                      <Label className="text-[11px]">更新 AI 昵称</Label>
                      <Input variant="secondary" maxLength={18} placeholder="留空交回给 AI 下次自取名" />
                    </TextField>
                    <div className="flex gap-1.5 shrink-0">
                      <Button size="sm" variant="tertiary" onPress={() => setEditingId(null)}>
                        取消
                      </Button>
                      <Button size="sm" variant="primary" onPress={() => saveNickname(t.id, editingNick)}>
                        保存
                      </Button>
                    </div>
                  </div>
                )}
              </Surface>
            ))}

            {visibleTokens.length === 0 && (
              <div className="py-10 text-center text-muted bg-surface-secondary rounded-xl border border-border">
                没有匹配的 MCP Token 记录
              </div>
            )}
          </div>
        </Card>
      </div>

      {/* 右侧 5 列：MCP 客户端配置片段 & 角色隔离说明 */}
      <Card className="lg:col-span-5 lg:sticky lg:top-4 rounded-xl p-4 sm:p-5 space-y-3.5">
        <div className="flex items-center justify-between gap-2">
          <div className="font-bold text-sm flex items-center gap-1.5">
            <Terminal className="w-4 h-4 text-indigo-400" /> MCP 客户端挂载配置与权限边界
          </div>
          <QuickCopyPill text={sampleConfig} label="复制 JSON" onCopied={() => notify('已复制 MCP JSON')} />
        </div>

        <Tabs
          selectedKey={snippetTab}
          onSelectionChange={(k) => setSnippetTab(String(k) as 'json' | 'cli' | 'matrix')}
          className="w-full"
        >
          <Tabs.ListContainer>
            <Tabs.List aria-label="MCP 配置与角色隔离说明" className="w-full">
              <Tabs.Tab id="json" className="flex-1 text-xs">
                JSON 配置
                <Tabs.Indicator />
              </Tabs.Tab>
              <Tabs.Tab id="cli" className="flex-1 text-xs">
                CLI 命令
                <Tabs.Indicator />
              </Tabs.Tab>
              <Tabs.Tab id="matrix" className="flex-1 text-xs">
                战争迷雾矩阵
                <Tabs.Indicator />
              </Tabs.Tab>
            </Tabs.List>
          </Tabs.ListContainer>
        </Tabs>

        {snippetTab === 'json' && (
          <pre className="p-3.5 rounded-lg bg-background border border-border text-[11px] font-mono overflow-x-auto text-foreground/90 animate-fade-in">
            {sampleConfig}
          </pre>
        )}

        {snippetTab === 'cli' && (
          <div className="space-y-2.5 animate-fade-in">
            <pre className="p-3.5 rounded-lg bg-background border border-border text-[11px] font-mono overflow-x-auto text-indigo-300">
              {cliSnippet}
            </pre>
            <div className="flex justify-end">
              <QuickCopyPill text={cliSnippet} label="复制命令" onCopied={() => notify('已复制 CLI 命令')} />
            </div>
          </div>
        )}

        {snippetTab === 'matrix' && (
          <div className="space-y-2 text-[11px] animate-fade-in">
            {[
              { role: 'player', desc: '仅限受控协议座位视角：可建娱乐房、进房出牌、发搭档战术信号，严禁透视对手暗牌或牌堆顺序。' },
              { role: 'spectator', desc: '公开观战与赛后全视角复盘（duel_get_post_game_review / 写入复盘评分），对局进行中受战争迷雾限制。' },
              { role: 'deck_coach', desc: '卡库检索、卡组构筑与六维雷达打分、提交 DIY 新卡提案（需后台审批后生效）。' },
              { role: 'admin', desc: '房间巡检、召唤内置 WindBot、维护公告广播；同样不可透视进行中对局暗牌。' }
            ].map((item) => (
              <Surface key={item.role} variant="secondary" className="p-3 rounded-lg border border-border space-y-1">
                <Chip size="sm" variant="soft" color="accent" className="h-5 px-2 font-mono text-[10px]">
                  {item.role}
                </Chip>
                <p className="text-muted leading-relaxed">{item.desc}</p>
              </Surface>
            ))}
          </div>
        )}
      </Card>
    </div>
  );
}

type SecuritySubTab = 'users' | 'bans' | 'account' | 'audits';

export function SecurityView({
  user,
  notify,
  onRefreshMe
}: {
  user: AuthUser;
  notify: (t: string, k?: 'ok' | 'err') => void;
  onRefreshMe: () => void;
}) {
  const canUsers = hasPerm(user, 'user:manage');
  const canBans = hasPerm(user, 'ban:manage');
  const canAudits = hasPerm(user, 'audit:view_all');

  const [subTab, setSubTab] = useState<SecuritySubTab>(() =>
    canUsers ? 'users' : canBans ? 'bans' : 'account'
  );

  const [users, setUsers] = useState<any[]>([]);
  const [roles, setRoles] = useState<any[]>([]);
  const [bans, setBans] = useState<any[]>([]);
  const [audits, setAudits] = useState<any[]>([]);
  const [sessions, setSessions] = useState<any[]>([]);
  const [auditSearch, setAuditSearch] = useState('');

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

      if (canUsers) {
        const u = await apiFetch('/api/v1/users');
        setUsers(u.users || []);
      }
      if (canBans) {
        const b = await apiFetch('/api/v1/bans');
        setBans(b.bans || []);
      }
      if (canAudits) {
        const a = await apiFetch('/api/v1/audit-logs?limit=120');
        setAudits(a.logs || []);
      }
    } catch (err) {
      notify(err instanceof Error ? err.message : '加载安全中心失败', 'err');
    }
  }, [canAudits, canBans, canUsers, notify]);

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
        setOneTimePwBanner(`账号 ${createdName} 创建成功，一次性初始密码：${res.generatedPassword}`);
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
        notify('未填写新用户名或密码，已保持原设置不变');
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
      notify('账号/密码设置已更新');
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

  const filteredAudits = useMemo(() => {
    if (!auditSearch.trim()) return audits;
    const kw = auditSearch.trim().toLowerCase();
    return audits.filter(
      (a) =>
        String(a.actorName || '').toLowerCase().includes(kw) ||
        String(a.action || '').toLowerCase().includes(kw) ||
        String(a.target || '').toLowerCase().includes(kw)
    );
  }, [audits, auditSearch]);

  return (
    <div className="space-y-4 text-xs animate-fade-in">
      {/* 顶部安全模块二级导航 Tabs (避免移动端超长纵向滚动) */}
      <div className="flex items-center justify-between gap-2 flex-wrap">
        <Tabs
          selectedKey={subTab}
          onSelectionChange={(k) => setSubTab(String(k) as SecuritySubTab)}
          className="max-w-full"
        >
          <Tabs.ListContainer>
            <Tabs.List aria-label="安全与审计子视图">
              {canUsers && (
                <Tabs.Tab id="users" className="text-xs px-3 gap-1.5">
                  <Users className="w-3.5 h-3.5" />
                  <span>面板用户 ({users.length})</span>
                  <Tabs.Indicator />
                </Tabs.Tab>
              )}
              {canBans && (
                <Tabs.Tab id="bans" className="text-xs px-3 gap-1.5">
                  <ShieldAlert className="w-3.5 h-3.5" />
                  <span>客户端封禁 ({bans.length})</span>
                  <Tabs.Indicator />
                </Tabs.Tab>
              )}
              <Tabs.Tab id="account" className="text-xs px-3 gap-1.5">
                <Lock className="w-3.5 h-3.5" />
                <span>个人密码 & 会话 ({sessions.length})</span>
                <Tabs.Indicator />
              </Tabs.Tab>
              {canAudits && (
                <Tabs.Tab id="audits" className="text-xs px-3 gap-1.5">
                  <FileText className="w-3.5 h-3.5" />
                  <span>审计日志 ({audits.length})</span>
                  <Tabs.Indicator />
                </Tabs.Tab>
              )}
            </Tabs.List>
          </Tabs.ListContainer>
        </Tabs>

        <Button size="sm" variant="secondary" onPress={load}>
          <RefreshCw className="w-3.5 h-3.5" /> 刷新
        </Button>
      </div>

      {/* 子视图 1：面板用户与角色管理 */}
      {subTab === 'users' && canUsers && (
        <Card className="rounded-xl p-4 sm:p-5 space-y-4 animate-fade-in">
          <div>
            <h2 className="text-sm font-bold flex items-center gap-2">
              <Users className="w-4 h-4 text-indigo-400" /> 面板成员账号与 RBAC 角色分配
            </h2>
            <p className="text-[11px] text-muted mt-0.5">
              密码留空时将生成一次性高强度初始密码并要求首次登录改密；所有密码采用 Argon2id 慢哈希存储
            </p>
          </div>

          <form onSubmit={createUser} className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 items-end">
            <TextField fullWidth value={newUsername} onChange={setNewUsername}>
              <Label className="text-xs">用户名（留空自动生成）</Label>
              <Input variant="secondary" placeholder="如 judge_01" />
            </TextField>

            <TextField fullWidth type="password" value={newInitialPassword} onChange={setNewInitialPassword}>
              <Label className="text-xs">初始密码（留空随机生成）</Label>
              <Input variant="secondary" placeholder="≥ 6 位字符" />
            </TextField>

            <div className="space-y-1">
              <label className="block text-xs font-medium text-foreground">分配角色</label>
              <select
                value={newRole}
                onChange={(e) => setNewRole(e.target.value)}
                className="w-full h-9 px-3 text-xs input-modern input-modern-indigo"
              >
                {roles.map((r) => (
                  <option key={r.code} value={r.code}>
                    {r.name} ({r.code})
                  </option>
                ))}
              </select>
            </div>

            <Button type="submit" variant="primary" className="w-full">
              <Plus className="w-3.5 h-3.5" /> 创建面板账号
            </Button>
          </form>

          {oneTimePwBanner && (
            <Alert status="warning">
              <Alert.Indicator />
              <Alert.Content className="flex-col sm:flex-row sm:items-center justify-between gap-2">
                <Alert.Description className="font-mono text-xs break-all text-foreground">
                  {oneTimePwBanner}
                </Alert.Description>
                <QuickCopyPill text={oneTimePwBanner} label="复制" onCopied={() => notify('已复制初始密码信息')} />
              </Alert.Content>
            </Alert>
          )}

          {editingUser && (
            <Surface variant="secondary" className="p-3.5 rounded-xl border border-accent/50 animate-fade-in">
              <form onSubmit={saveEditedUser} className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 items-end">
                <div className="sm:col-span-2 lg:col-span-1">
                  <div className="text-[11px] text-muted">正在修改成员账号</div>
                  <div className="font-bold text-sm text-indigo-300 font-mono truncate">{editingUser.username}</div>
                </div>
                <TextField fullWidth value={editUsername} onChange={setEditUsername}>
                  <Label className="text-xs">新用户名（选填）</Label>
                  <Input variant="secondary" placeholder={`留空保持 ${editingUser.username}`} />
                </TextField>
                <TextField fullWidth type="password" value={editCustomPw} onChange={setEditCustomPw}>
                  <Label className="text-xs">直接设定新密码（选填）</Label>
                  <Input variant="secondary" placeholder="≥ 6 位，留空保持原密码" />
                </TextField>
                <div className="flex gap-2">
                  <Button type="submit" size="sm" variant="primary" className="flex-1">
                    保存设置
                  </Button>
                  <Button
                    size="sm"
                    variant="tertiary"
                    onPress={() => {
                      setEditingUser(null);
                      setEditUsername('');
                      setEditCustomPw('');
                    }}
                  >
                    取消
                  </Button>
                </div>
              </form>
            </Surface>
          )}

          {/* 移动端卡片列表 */}
          <div className="md:hidden space-y-2.5">
            {users.map((u) => (
              <Surface key={u.id} variant="secondary" className="rounded-xl p-3.5 space-y-2 border border-border">
                <div className="flex items-center justify-between gap-2">
                  <div className="font-semibold text-foreground flex items-center gap-1.5 flex-wrap">
                    <span>{u.username}</span>
                    <Chip size="sm" variant="soft" color="accent" className="h-5 px-1.5 font-mono text-[10px]">
                      {(u.roles || []).join(', ')}
                    </Chip>
                  </div>
                  <Chip
                    size="sm"
                    variant="soft"
                    color={!u.enabled ? 'danger' : u.mustChangePassword ? 'warning' : 'success'}
                    className="h-5 px-2 text-[10px]"
                  >
                    {!u.enabled ? '已停用' : u.mustChangePassword ? '待改密' : '正常'}
                  </Chip>
                </div>
                <div className="text-[11px] text-muted">
                  最近登录：{u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString() : '-'}
                </div>
                <div className="flex flex-wrap gap-1.5 pt-1">
                  <Button
                    size="sm"
                    variant="secondary"
                    className="h-7 px-2.5 text-[11px]"
                    onPress={() => {
                      setEditingUser(u);
                      setEditUsername('');
                      setEditCustomPw('');
                    }}
                  >
                    编辑账号/密码
                  </Button>
                  <Button
                    size="sm"
                    variant="secondary"
                    className="h-7 px-2.5 text-[11px]"
                    onPress={async () => {
                      const r = await apiFetch(`/api/v1/users/${u.id}/reset-password`, { method: 'POST', body: '{}' });
                      setOneTimePwBanner(`已重置 ${u.username} 的随机密码：${r.oneTimePassword}`);
                      load();
                    }}
                  >
                    重置随机密码
                  </Button>
                  {u.id !== user.userId && (
                    <Button
                      size="sm"
                      variant={u.enabled ? 'danger-soft' : 'primary'}
                      className="h-7 px-2.5 text-[11px]"
                      onPress={async () => {
                        await apiFetch(`/api/v1/users/${u.id}`, {
                          method: 'PATCH',
                          body: JSON.stringify({ enabled: !u.enabled, unlockAccount: true })
                        });
                        load();
                      }}
                    >
                      {u.enabled ? '停用' : '启用/解锁'}
                    </Button>
                  )}
                </div>
              </Surface>
            ))}
          </div>

          {/* 桌面端精致数据表 */}
          <div className="hidden md:block overflow-x-auto">
            <table className="w-full text-xs text-left">
              <thead className="text-muted border-b border-separator">
                <tr>
                  <th className="py-2.5">用户名</th>
                  <th>RBAC 角色</th>
                  <th>状态</th>
                  <th>最近登录</th>
                  <th className="text-right">操作</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-separator">
                {users.map((u) => (
                  <tr key={u.id} className="hover:bg-surface-secondary/50 transition">
                    <td className="py-2.5 font-semibold text-foreground">{u.username}</td>
                    <td>
                      <Chip size="sm" variant="soft" color="accent" className="h-5 px-2 font-mono text-[10px]">
                        {(u.roles || []).join(', ')}
                      </Chip>
                    </td>
                    <td>
                      <Chip
                        size="sm"
                        variant="soft"
                        color={!u.enabled ? 'danger' : u.mustChangePassword ? 'warning' : 'success'}
                        className="h-5 px-2 text-[10px]"
                      >
                        {!u.enabled ? '已停用' : u.mustChangePassword ? '待改密' : '正常'}
                      </Chip>
                    </td>
                    <td className="text-muted">{u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString() : '-'}</td>
                    <td className="text-right space-x-1.5">
                      <Button
                        size="sm"
                        variant="secondary"
                        className="h-7 px-2.5 text-[11px]"
                        onPress={() => {
                          setEditingUser(u);
                          setEditUsername('');
                          setEditCustomPw('');
                        }}
                      >
                        改名/改密
                      </Button>
                      <Button
                        size="sm"
                        variant="tertiary"
                        className="h-7 px-2.5 text-[11px]"
                        onPress={async () => {
                          const r = await apiFetch(`/api/v1/users/${u.id}/reset-password`, { method: 'POST', body: '{}' });
                          setOneTimePwBanner(`已重置 ${u.username} 的随机密码：${r.oneTimePassword}`);
                          load();
                        }}
                      >
                        随机重置
                      </Button>
                      {u.id !== user.userId && (
                        <Button
                          size="sm"
                          variant={u.enabled ? 'danger-soft' : 'primary'}
                          className="h-7 px-2.5 text-[11px]"
                          onPress={async () => {
                            await apiFetch(`/api/v1/users/${u.id}`, {
                              method: 'PATCH',
                              body: JSON.stringify({ enabled: !u.enabled, unlockAccount: true })
                            });
                            load();
                          }}
                        >
                          {u.enabled ? '停用' : '启用'}
                        </Button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      )}

      {/* 子视图 2：游戏客户端封禁表 */}
      {subTab === 'bans' && canBans && (
        <Card className="rounded-xl p-4 sm:p-5 space-y-4 animate-fade-in">
          <div>
            <h2 className="text-sm font-bold flex items-center gap-2">
              <ShieldAlert className="w-4 h-4 text-rose-400" /> 游戏客户端连接封禁表 (IP / CIDR / 昵称)
            </h2>
            <p className="text-[11px] text-muted mt-0.5">封禁规则添加后立即作用于 TCP 7911 联机网关与建房请求</p>
          </div>

          <form onSubmit={addBan} className="grid grid-cols-1 sm:grid-cols-4 gap-3 items-end">
            <div className="space-y-1">
              <label className="block text-xs font-medium text-foreground">封禁维度</label>
              <select
                value={banType}
                onChange={(e) => setBanType(e.target.value)}
                className="w-full h-9 px-3 text-xs input-modern input-modern-indigo"
              >
                <option value="ip">IP 地址</option>
                <option value="cidr">CIDR 网段</option>
                <option value="name">玩家昵称</option>
              </select>
            </div>

            <TextField fullWidth isRequired value={banVal} onChange={setBanVal}>
              <Label className="text-xs">封禁目标值</Label>
              <Input variant="secondary" placeholder="如 192.168.1.50 或 BadPlayer" />
            </TextField>

            <TextField fullWidth value={banReason} onChange={setBanReason}>
              <Label className="text-xs">备注原因</Label>
              <Input variant="secondary" placeholder="恶意烧绳 / 刷屏" />
            </TextField>

            <Button type="submit" variant="danger" className="w-full">
              添加封禁 (24h)
            </Button>
          </form>

          <div className="space-y-2">
            {bans.map((b) => (
              <Surface
                key={b.id}
                variant="secondary"
                className="flex items-center justify-between gap-2 px-3.5 py-2.5 rounded-lg border border-border"
              >
                <div className="flex items-center gap-2 flex-wrap min-w-0">
                  <Chip size="sm" variant="soft" color="danger" className="h-5 px-2 font-mono text-[10px]">
                    {b.targetType}
                  </Chip>
                  <span className="font-mono font-semibold text-foreground break-all">{b.targetValue}</span>
                  {b.reason && <span className="text-muted">— {b.reason}</span>}
                </div>
                <Button
                  size="sm"
                  variant="secondary"
                  className="shrink-0 h-7 px-2.5 text-[11px]"
                  onPress={async () => {
                    await apiFetch(`/api/v1/bans/${b.id}`, { method: 'DELETE' });
                    notify('已解除封禁');
                    load();
                  }}
                >
                  解封
                </Button>
              </Surface>
            ))}
            {bans.length === 0 && (
              <div className="py-10 text-center text-muted bg-surface-secondary rounded-xl border border-border">
                当前无生效中的封禁记录
              </div>
            )}
          </div>
        </Card>
      )}

      {/* 子视图 3：个人账号修改与活跃登录会话 */}
      {subTab === 'account' && (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4 animate-fade-in">
          <Card className="rounded-xl p-4 sm:p-5 space-y-3.5">
            <h3 className="text-sm font-bold">修改当前账号名与登录密码（@{user.username}）</h3>
            <form onSubmit={changeMyPw} className="space-y-3">
              <TextField fullWidth isRequired type="password" value={curPw} onChange={setCurPw}>
                <Label className="text-xs">当前原密码（必填校验）</Label>
                <Input variant="secondary" placeholder="输入当前登录密码" />
              </TextField>

              <TextField fullWidth value={myNewUsername} onChange={setMyNewUsername}>
                <Label className="text-xs">自定义新用户名（选填）</Label>
                <Input variant="secondary" placeholder={`留空保持 ${user.username}`} />
              </TextField>

              <TextField fullWidth type="password" value={nextPw} onChange={setNextPw}>
                <Label className="text-xs">新密码（≥ 6 位，留空保持原密码）</Label>
                <Input variant="secondary" placeholder="至少 6 位字符" />
              </TextField>

              <Button type="submit" variant="primary" fullWidth>
                保存个人安全设置
              </Button>
            </form>
          </Card>

          <Card className="rounded-xl p-4 sm:p-5 space-y-3.5">
            <h3 className="text-sm font-bold">当前账号活跃会话 ({sessions.length})</h3>
            <div className="space-y-2 max-h-80 overflow-y-auto pr-0.5">
              {sessions.map((s) => (
                <Surface
                  key={s.id}
                  variant="secondary"
                  className="flex items-center justify-between gap-2 px-3.5 py-2.5 rounded-lg border border-border"
                >
                  <div className="flex items-center gap-2 min-w-0">
                    <span className="font-mono text-foreground truncate">
                      {s.tokenPrefix}... {s.ip ? `(${s.ip})` : ''}
                    </span>
                    {s.isCurrent && (
                      <Chip size="sm" variant="soft" color="success" className="h-5 px-2 text-[10px]">
                        当前设备
                      </Chip>
                    )}
                  </div>
                  {!s.isCurrent && (
                    <Button
                      size="sm"
                      variant="danger-soft"
                      className="h-7 px-2.5 text-[11px] shrink-0"
                      onPress={async () => {
                        await apiFetch(`/api/v1/auth/sessions/${s.id}`, { method: 'DELETE' });
                        load();
                      }}
                    >
                      踢下线
                    </Button>
                  )}
                </Surface>
              ))}
            </div>
          </Card>
        </div>
      )}

      {/* 子视图 4：全链路安全审计日志 (T3 Code 终端日志风格) */}
      {subTab === 'audits' && canAudits && (
        <Card className="rounded-xl p-4 sm:p-5 space-y-3.5 animate-fade-in">
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2.5">
            <div>
              <h3 className="text-sm font-bold">全链路安全与管服审计日志 ({filteredAudits.length})</h3>
              <p className="text-[11px] text-muted mt-0.5">记录面板操作、MCP Token 鉴权、DIY 审批与房间裁决轨迹</p>
            </div>

            <SearchField
              aria-label="检索审计日志"
              variant="secondary"
              value={auditSearch}
              onChange={setAuditSearch}
              className="sm:w-64"
            >
              <SearchField.Group>
                <SearchField.SearchIcon />
                <SearchField.Input placeholder="按操作人、动作或目标筛选..." className="text-xs" />
                <SearchField.ClearButton />
              </SearchField.Group>
            </SearchField>
          </div>

          <div className="max-h-[500px] overflow-y-auto space-y-1 text-[11px] font-mono bg-[var(--console-bg)] p-3.5 rounded-xl border border-border">
            {filteredAudits.map((a) => (
              <div key={a.id} className="flex items-baseline gap-2 break-all leading-relaxed py-0.5">
                <span className="text-muted shrink-0">{new Date(a.createdAt).toLocaleString('zh-CN', { hour12: false })}</span>
                <span className={a.result === 'ok' ? 'text-emerald-400 shrink-0' : 'text-rose-400 shrink-0'}>
                  [{a.result}]
                </span>
                <span className="text-indigo-400 font-semibold shrink-0">{a.actorName}</span>
                <span className="text-muted">→</span>
                <span className="text-foreground">{a.action}</span>
                {a.target && <span className="text-muted">({a.target})</span>}
              </div>
            ))}
            {filteredAudits.length === 0 && (
              <div className="py-10 text-center text-muted font-sans">没有匹配的审计日志记录</div>
            )}
          </div>
        </Card>
      )}
    </div>
  );
}
