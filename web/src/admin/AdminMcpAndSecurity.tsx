import React, { useEffect, useState, useCallback } from 'react';
import { KeyRound, ShieldAlert, Users } from 'lucide-react';
import { apiFetch, AuthUser, hasPerm } from '../api';
import { CopyButton } from './AdminDashboardAndRooms';

export function McpHubView({ user, notify }: { user: AuthUser; notify: (t: string, k?: 'ok' | 'err') => void }) {
  const [tokens, setTokens] = useState<any[]>([]);
  const [allowedRoles, setAllowedRoles] = useState<string[]>([]);
  const [name, setName] = useState('');
  const [personaStyle, setPersonaStyle] = useState('');
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
    <div className="space-y-5 animate-fade-in">
      <div className="glass-card rounded-2xl p-4 sm:p-5 space-y-4">
        <h2 className="text-sm font-bold flex items-center gap-2">
          <KeyRound className="w-4 h-4 text-indigo-400 shrink-0" />
          <span>签发外部 AI MCP Token（严格执行权限不放大原则，仅存 SHA-256）</span>
        </h2>
        <form onSubmit={issue} className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-3 items-end">
          <div>
            <label className="block text-xs text-slate-400 mb-1">AI 决斗者昵称（选填；留空由 AI 自取）</label>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              maxLength={18}
              placeholder="留空自取，或填 海马濑人"
              className="w-full px-3 py-2 text-xs input-modern input-modern-indigo"
            />
          </div>
          <div>
            <label className="block text-xs text-slate-400 mb-1">性格与吐槽人设（选填）</label>
            <input
              value={personaStyle}
              onChange={(e) => setPersonaStyle(e.target.value)}
              maxLength={100}
              placeholder="如：毒舌技术流 / 热血决斗王"
              className="w-full px-3 py-2 text-xs input-modern input-modern-indigo"
            />
          </div>
          <div>
            <label className="block text-xs text-slate-400 mb-1">授权 MCP 角色（受自身账号约束）</label>
            <select
              value={role}
              onChange={(e) => setRole(e.target.value)}
              className="w-full px-3 py-2 text-xs input-modern input-modern-indigo"
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
              className="w-full px-3 py-2 text-xs input-modern input-modern-indigo"
            />
          </div>
          <button className="min-h-[40px] px-4 py-2 rounded-xl bg-gradient-to-r from-indigo-600 to-purple-600 hover:from-indigo-500 hover:to-purple-500 text-white text-xs font-semibold shadow-lg shadow-indigo-950/50 cursor-pointer">
            签发新 Token
          </button>
        </form>

        {newlyIssuedRaw && (
          <div className="p-3.5 rounded-xl bg-emerald-950/70 border border-emerald-500/40 space-y-2 text-xs font-mono">
            <div className="flex items-center justify-between gap-2">
              <span className="break-all text-emerald-200">streamable-http 直连 URL：{directMcpUrl}</span>
              <CopyButton text={directMcpUrl} label="复制" />
            </div>
            <div className="flex items-center justify-between gap-2 text-slate-300 pt-1.5 border-t border-emerald-500/20">
              <span className="break-all">纯明文 Token（仅展示一次）：{newlyIssuedRaw}</span>
              <CopyButton text={newlyIssuedRaw} label="复制" />
            </div>
          </div>
        )}
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <div className="glass-card rounded-2xl p-4 sm:p-5 space-y-3">
          <h3 className="text-sm font-bold">已签发 Token 列表 ({tokens.length})</h3>
          <div className="space-y-2.5 max-h-96 overflow-y-auto pr-0.5">
            {tokens.map((t) => (
              <div key={t.id} className="glass-subcard p-3.5 rounded-xl space-y-2 text-xs">
                <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
                  <div className="min-w-0">
                    <div className="font-semibold flex items-center gap-1.5 flex-wrap">
                      <span>{t.preferredPlayerName || t.name}</span>
                      <span className="text-indigo-400">[{t.mcpRole}]</span>
                      {t.mcpRole === 'player' && (
                        <span className="px-2 py-0.5 rounded-full text-[10px] bg-indigo-500/15 border border-indigo-500/30 text-indigo-200">
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
                        className="px-2.5 py-1 rounded-lg bg-indigo-600/20 border border-indigo-500/30 text-indigo-200 cursor-pointer"
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
                        className="px-2.5 py-1 rounded-lg bg-rose-950/60 border border-rose-500/30 text-rose-300 cursor-pointer"
                      >
                        吊销
                      </button>
                    </div>
                  )}
                </div>

                {editingId === t.id && (
                  <div className="flex flex-col sm:flex-row items-stretch sm:items-center gap-2 pt-2 border-t border-slate-800/80">
                    <input
                      value={editingNick}
                      onChange={(e) => setEditingNick(e.target.value)}
                      maxLength={18}
                      placeholder="输入新昵称，或留空交回给 AI 下次自取名"
                      className="flex-1 px-3 py-1.5 text-xs input-modern input-modern-indigo"
                    />
                    <div className="flex gap-1.5 shrink-0">
                      <button
                        type="button"
                        onClick={() => saveNickname(t.id, editingNick)}
                        className="flex-1 sm:flex-none px-3 py-1.5 rounded-lg bg-indigo-600 hover:bg-indigo-500 text-white font-semibold cursor-pointer"
                      >
                        保存
                      </button>
                      <button
                        type="button"
                        onClick={() => setEditingId(null)}
                        className="px-2.5 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 cursor-pointer"
                      >
                        取消
                      </button>
                    </div>
                  </div>
                )}
              </div>
            ))}
            {tokens.length === 0 && <div className="py-8 text-center text-xs text-slate-500">暂无已签发 Token</div>}
          </div>
        </div>

        <div className="glass-card rounded-2xl p-4 sm:p-5 space-y-2.5">
          <div className="flex items-center justify-between gap-2">
            <h3 className="text-sm font-bold">一键复制 Claude / Gemini CLI / Cursor / ChatGPT MCP 配置</h3>
            <CopyButton text={sampleConfig} label="复制 JSON" />
          </div>
          <pre className="glass-subcard p-3.5 rounded-xl text-[11px] font-mono overflow-x-auto text-slate-300">
            {sampleConfig}
          </pre>
        </div>
      </div>
    </div>
  );
}

export function SecurityView({
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
    <div className="space-y-5 animate-fade-in">
      {hasPerm(user, 'user:manage') && (
        <div className="glass-card rounded-2xl p-4 sm:p-5 space-y-4">
          <h2 className="text-sm font-bold flex items-center gap-2">
            <Users className="w-4 h-4 text-indigo-400 shrink-0" />
            <span>面板用户与管理员自定义账号 / 密码（未填项自动采用默认值）</span>
          </h2>
          <form onSubmit={createUser} className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-2.5">
            <input
              value={newUsername}
              onChange={(e) => setNewUsername(e.target.value)}
              placeholder="自定义用户名（留空生成默认名）"
              className="w-full px-3 py-2 text-xs input-modern input-modern-indigo"
            />
            <input
              type="password"
              minLength={6}
              value={newInitialPassword}
              onChange={(e) => setNewInitialPassword(e.target.value)}
              placeholder="自定义密码 ≥6位（留空生成随机密码）"
              className="w-full px-3 py-2 text-xs input-modern input-modern-indigo"
            />
            <select
              value={newRole}
              onChange={(e) => setNewRole(e.target.value)}
              className="w-full px-3 py-2 text-xs input-modern input-modern-indigo"
            >
              {roles.map((r) => (
                <option key={r.code} value={r.code}>
                  {r.name} ({r.code})
                </option>
              ))}
            </select>
            <button className="min-h-[38px] px-4 py-2 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold cursor-pointer">
              创建账号
            </button>
          </form>

          {oneTimePwBanner && (
            <div className="p-3 rounded-xl bg-amber-950/70 border border-amber-500/40 text-xs font-mono flex items-center justify-between gap-2">
              <span className="break-all">{oneTimePwBanner}</span>
              <CopyButton text={oneTimePwBanner} label="复制" />
            </div>
          )}

          {editingUser && (
            <form
              onSubmit={saveEditedUser}
              className="p-3.5 rounded-xl glass-subcard border border-indigo-500/40 grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-2 items-center text-xs"
            >
              <span className="text-indigo-300 font-semibold sm:col-span-2 lg:col-span-1">
                编辑账号 [{editingUser.username}]：
              </span>
              <input
                value={editUsername}
                onChange={(e) => setEditUsername(e.target.value)}
                placeholder={`新用户名（留空保持 ${editingUser.username}）`}
                className="w-full px-3 py-1.5 text-xs input-modern input-modern-indigo"
              />
              <input
                type="password"
                minLength={6}
                value={editCustomPw}
                onChange={(e) => setEditCustomPw(e.target.value)}
                placeholder="自定义新密码 ≥6位（留空保持原密码）"
                className="w-full px-3 py-1.5 text-xs input-modern input-modern-indigo"
              />
              <div className="flex gap-2">
                <button className="flex-1 min-h-[34px] px-3 py-1.5 rounded-lg bg-indigo-600 hover:bg-indigo-500 text-white font-semibold cursor-pointer">
                  保存设置
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setEditingUser(null);
                    setEditUsername('');
                    setEditCustomPw('');
                  }}
                  className="px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 cursor-pointer"
                >
                  取消
                </button>
              </div>
            </form>
          )}

          {/* 手机端用户卡片列表 */}
          <div className="md:hidden space-y-2.5">
            {users.map((u) => (
              <div key={u.id} className="glass-subcard rounded-xl p-3.5 space-y-2 text-xs">
                <div className="flex items-center justify-between gap-2">
                  <div className="font-semibold text-slate-100">
                    {u.username} <span className="text-indigo-300 font-normal">[{(u.roles || []).join(', ')}]</span>
                  </div>
                  {!u.enabled ? (
                    <span className="text-rose-400">已停用</span>
                  ) : u.mustChangePassword ? (
                    <span className="text-amber-400">待首次确认</span>
                  ) : (
                    <span className="text-emerald-400">正常</span>
                  )}
                </div>
                <div className="text-[11px] text-slate-400">
                  最近登录：{u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString() : '-'}
                </div>
                <div className="flex flex-wrap gap-2 pt-1">
                  <button
                    onClick={() => {
                      setEditingUser(u);
                      setEditUsername('');
                      setEditCustomPw('');
                    }}
                    className="px-2.5 py-1 rounded-lg bg-indigo-600/20 border border-indigo-500/30 text-indigo-200 cursor-pointer"
                  >
                    自定义用户名/密码
                  </button>
                  <button
                    onClick={async () => {
                      const r = await apiFetch(`/api/v1/users/${u.id}/reset-password`, { method: 'POST', body: '{}' });
                      setOneTimePwBanner(`已重置 ${u.username} 的默认随机密码：${r.oneTimePassword}`);
                      load();
                    }}
                    className="px-2.5 py-1 rounded-lg bg-amber-500/15 border border-amber-500/30 text-amber-300 cursor-pointer"
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
                      className="px-2.5 py-1 rounded-lg bg-sky-500/15 border border-sky-500/30 text-sky-300 cursor-pointer"
                    >
                      {u.enabled ? '停用' : '启用/解锁'}
                    </button>
                  )}
                </div>
              </div>
            ))}
          </div>

          {/* 桌面端用户表格 */}
          <div className="hidden md:block overflow-x-auto">
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
                    <td className="py-2.5 font-medium">{u.username}</td>
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
        <div className="glass-card rounded-2xl p-4 sm:p-5 space-y-3.5">
          <h2 className="text-sm font-bold flex items-center gap-2">
            <ShieldAlert className="w-4 h-4 text-rose-400" /> 游戏客户端封禁表 (IP / CIDR / 昵称)
          </h2>
          <form onSubmit={addBan} className="grid grid-cols-1 sm:grid-cols-4 gap-2.5">
            <select
              value={banType}
              onChange={(e) => setBanType(e.target.value)}
              className="px-3 py-2 text-xs input-modern input-modern-indigo"
            >
              <option value="ip">IP 地址</option>
              <option value="cidr">CIDR 网段</option>
              <option value="name">玩家昵称</option>
            </select>
            <input
              value={banVal}
              onChange={(e) => setBanVal(e.target.value)}
              placeholder="如 192.168.1.50 或 BadPlayer"
              className="px-3 py-2 text-xs input-modern input-modern-indigo"
              required
            />
            <input
              value={banReason}
              onChange={(e) => setBanReason(e.target.value)}
              placeholder="封禁原因"
              className="px-3 py-2 text-xs input-modern input-modern-indigo"
            />
            <button className="min-h-[38px] px-4 py-2 rounded-xl bg-rose-600 hover:bg-rose-500 text-white text-xs font-semibold cursor-pointer">
              添加封禁 (24h)
            </button>
          </form>
          <div className="space-y-1.5 text-xs">
            {bans.map((b) => (
              <div key={b.id} className="flex items-center justify-between gap-2 glass-subcard px-3 py-2 rounded-xl">
                <span className="break-all">
                  [{b.targetType}] <span className="font-mono text-rose-300">{b.targetValue}</span> — {b.reason}
                </span>
                <button
                  onClick={async () => {
                    await apiFetch(`/api/v1/bans/${b.id}`, { method: 'DELETE' });
                    load();
                  }}
                  className="text-emerald-400 hover:underline shrink-0 cursor-pointer"
                >
                  解封
                </button>
              </div>
            ))}
          </div>
        </div>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <div className="glass-card rounded-2xl p-4 sm:p-5 space-y-3.5">
          <h3 className="text-sm font-bold">个人账号自定义用户名与密码修改（当前：{user.username}）</h3>
          <form onSubmit={changeMyPw} className="grid grid-cols-1 sm:grid-cols-2 gap-2.5">
            <input
              type="password"
              value={curPw}
              onChange={(e) => setCurPw(e.target.value)}
              placeholder="当前密码（必填校验）"
              className="px-3 py-2 text-xs input-modern input-modern-indigo"
              required
            />
            <input
              value={myNewUsername}
              onChange={(e) => setMyNewUsername(e.target.value)}
              placeholder={`新用户名（留空保持 ${user.username}）`}
              className="px-3 py-2 text-xs input-modern input-modern-indigo"
            />
            <input
              type="password"
              minLength={6}
              value={nextPw}
              onChange={(e) => setNextPw(e.target.value)}
              placeholder="新密码 ≥6位（留空保持原密码）"
              className="px-3 py-2 text-xs input-modern input-modern-indigo"
            />
            <button className="min-h-[38px] px-4 py-2 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold cursor-pointer">
              保存设置
            </button>
          </form>
          <div className="space-y-1.5 text-xs">
            {sessions.map((s) => (
              <div key={s.id} className="flex items-center justify-between gap-2 glass-subcard px-3 py-2 rounded-xl">
                <span className="font-mono truncate">
                  {s.tokenPrefix}... {s.ip ? `(${s.ip})` : ''}{' '}
                  {s.isCurrent && <span className="text-emerald-400">[本机]</span>}
                </span>
                {!s.isCurrent && (
                  <button
                    onClick={async () => {
                      await apiFetch(`/api/v1/auth/sessions/${s.id}`, { method: 'DELETE' });
                      load();
                    }}
                    className="text-rose-400 hover:underline shrink-0 cursor-pointer"
                  >
                    踢下线
                  </button>
                )}
              </div>
            ))}
          </div>
        </div>

        {hasPerm(user, 'audit:view_all') && (
          <div className="glass-card rounded-2xl p-4 sm:p-5 space-y-2.5">
            <h3 className="text-sm font-bold">全链路安全审计日志</h3>
            <div className="max-h-56 overflow-y-auto space-y-1 text-[11px] font-mono glass-subcard p-3 rounded-xl">
              {audits.map((a) => (
                <div key={a.id} className="break-all leading-relaxed">
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
