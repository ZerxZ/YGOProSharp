import React, { useCallback, useEffect, useState } from 'react';
import { KeyRound, Trash2 } from 'lucide-react';
import { apiFetch } from '../api';
import { QuickCopyPill } from '../RoomConfigurator';
import type { PortalUser } from './portalTypes';

export function PortalAiTokenCenter({
  user,
  mcpPortHint,
  onRequireAuth,
  notify
}: {
  user: PortalUser | null;
  mcpPortHint?: number;
  onRequireAuth: () => void;
  notify: (t: string, k?: 'ok' | 'err') => void;
}) {
  const [tokens, setTokens] = useState<any[]>([]);
  const [name, setName] = useState('');
  const [personaStyle, setPersonaStyle] = useState('');
  const [role, setRole] = useState('player');
  const [roomsText, setRoomsText] = useState('');
  const [expiresHours, setExpiresHours] = useState(72);
  const [newlyIssuedRaw, setNewlyIssuedRaw] = useState('');
  const [editingTokenId, setEditingTokenId] = useState<string | null>(null);
  const [editingNickname, setEditingNickname] = useState('');
  const [editingPersona, setEditingPersona] = useState('');
  const [, setServerMcpPort] = useState<number | undefined>(mcpPortHint);

  const loadTokens = useCallback(async () => {
    if (!user) return;
    try {
      const res = await apiFetch('/api/portal/mcp/tokens');
      setTokens(res.tokens || []);
      if (res.mcpPortHint) setServerMcpPort(res.mcpPortHint);
    } catch (err) {
      notify(err instanceof Error ? err.message : '加载 AI Token 失败', 'err');
    }
  }, [user, notify]);

  useEffect(() => {
    loadTokens();
  }, [loadTokens]);

  if (!user) {
    return (
      <div className="bg-slate-900 border border-slate-800 rounded-xl p-10 text-center space-y-4">
        <KeyRound className="w-10 h-10 text-emerald-400 mx-auto" />
        <h2 className="text-base font-bold">注册或登录账号，免费创建你的专属外部 AI 对战 Token</h2>
        <p className="text-xs text-slate-400 max-w-lg mx-auto">
          注册账号后可自助签发受限视角 <code className="text-emerald-300">player</code>（上桌打牌 AI）或{' '}
          <code className="text-sky-300">spectator</code>（观战解说 AI）令牌，让 Claude、Gemini、DeepSeek、ChatGPT 等外部客户端陪你双打或斗蛐蛐。
        </p>
        <button
          onClick={onRequireAuth}
          className="px-5 py-2 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-sm font-semibold cursor-pointer"
        >
          立即注册 / 登录账号
        </button>
      </div>
    );
  }

  const issueToken = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const rooms = roomsText
        .split(',')
        .map((s) => s.trim())
        .filter(Boolean);
      const trimmedName = name.trim();
      const trimmedPersona = personaStyle.trim();
      const res = await apiFetch('/api/portal/mcp/tokens', {
        method: 'POST',
        body: JSON.stringify({
          name: trimmedName || undefined,
          preferredPlayerName: trimmedName || undefined,
          personaStyle: trimmedPersona || undefined,
          mcpRole: role,
          allowedRooms: rooms,
          maxSessions: 1,
          expiresInHours: Number(expiresHours)
        })
      });
      setNewlyIssuedRaw(res.rawToken);
      setName('');
      setPersonaStyle('');
      notify(trimmedName ? `AI Token 创建成功（已绑定昵称：${trimmedName}）！` : 'AI Token 创建成功（首次进房时将由 AI 自主取名）！');
      loadTokens();
    } catch (err) {
      notify(err instanceof Error ? err.message : '创建 AI Token 失败', 'err');
    }
  };

  const saveTokenNickname = async (tokenId: string, nextNickname: string, nextPersona: string) => {
    try {
      const res = await apiFetch(`/api/portal/mcp/tokens/${tokenId}`, {
        method: 'PATCH',
        body: JSON.stringify({
          preferredPlayerName: nextNickname.trim() || null,
          personaStyle: nextPersona.trim()
        })
      });
      setEditingTokenId(null);
      notify(res.message || 'AI 档案已更新');
      loadTokens();
    } catch (err) {
      notify(err instanceof Error ? err.message : '修改 AI 档案失败', 'err');
    }
  };

  const mcpUrl = `${window.location.origin}/mcp`;
  const directMcpUrl = `${mcpUrl}?key=${newlyIssuedRaw ? encodeURIComponent(newlyIssuedRaw) : '<粘贴你刚生成的_ygp_mcp_Token>'}`;
  const mcpConfigJson = JSON.stringify(
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
    <div className="space-y-6 text-xs">
      <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-4">
        <h2 className="text-sm font-semibold flex items-center gap-2">
          <KeyRound className="w-4 h-4 text-emerald-400" /> 创建我的外部 AI Token（严格遵守战争迷雾，禁止透视暗牌）
        </h2>

        <form onSubmit={issueToken} className="grid grid-cols-1 md:grid-cols-6 gap-3 items-end">
          <div>
            <label className="block text-slate-400 mb-1">AI 决斗者昵称（留空自取名）</label>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              maxLength={18}
              placeholder="留空由 AI 自取，或填如 海马濑人"
              className="w-full px-3 py-1.5 rounded bg-slate-950 border border-slate-700"
            />
          </div>
          <div>
            <label className="block text-slate-400 mb-1">性格与台词人设（可选）</label>
            <input
              value={personaStyle}
              onChange={(e) => setPersonaStyle(e.target.value)}
              maxLength={80}
              placeholder="如 孤高青眼社长 / 热血搭档"
              className="w-full px-3 py-1.5 rounded bg-slate-950 border border-slate-700"
            />
          </div>
          <div>
            <label className="block text-slate-400 mb-1">AI 角色类型</label>
            <select
              value={role}
              onChange={(e) => setRole(e.target.value)}
              className="w-full px-3 py-1.5 rounded bg-slate-950 border border-slate-700"
            >
              <option value="player">对战选手 AI (player)</option>
              <option value="spectator">只读观战解说 AI (spectator)</option>
            </select>
          </div>
          <div>
            <label className="block text-slate-400 mb-1">限定房间白名单（逗号分隔，留空不限）</label>
            <input
              value={roomsText}
              onChange={(e) => setRoomsText(e.target.value)}
              placeholder="如 Room01,Tag02"
              className="w-full px-3 py-1.5 rounded bg-slate-950 border border-slate-700"
            />
          </div>
          <div>
            <label className="block text-slate-400 mb-1">有效期</label>
            <select
              value={expiresHours}
              onChange={(e) => setExpiresHours(Number(e.target.value))}
              className="w-full px-3 py-1.5 rounded bg-slate-950 border border-slate-700"
            >
              <option value={12}>12 小时</option>
              <option value={48}>2 天 (48h)</option>
              <option value={168}>7 天 (168h)</option>
              <option value={720}>30 天 (720h)</option>
            </select>
          </div>
          <button className="px-4 py-1.5 rounded-lg bg-emerald-600 hover:bg-emerald-500 font-semibold text-white cursor-pointer">
            生成 AI Token
          </button>
        </form>

        {newlyIssuedRaw && (
          <div className="p-3 rounded-lg bg-emerald-950/70 border border-emerald-500/40 space-y-2 font-mono">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <span className="break-all text-emerald-200">
                streamable-http 直连 URL（直接粘贴到客户端 url 框）：{directMcpUrl}
              </span>
              <QuickCopyPill text={directMcpUrl} label="复制 MCP 直连 URL" onCopied={() => notify('已复制 MCP 直连 URL')} />
            </div>
            <div className="flex flex-wrap items-center justify-between gap-2 text-slate-300 text-[11px]">
              <span className="break-all">纯明文 Token（仅显示一次）：{newlyIssuedRaw}</span>
              <QuickCopyPill text={newlyIssuedRaw} label="复制纯 Token" onCopied={() => notify('已复制 AI Token')} />
            </div>
          </div>
        )}
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-3">
          <h3 className="text-sm font-semibold">我已创建的 AI Token 列表</h3>
          <div className="space-y-2 max-h-80 overflow-y-auto">
            {tokens.map((t) => (
              <div key={t.id} className="bg-slate-950 p-3 rounded border border-slate-800 space-y-2">
                <div className="flex items-center justify-between gap-2">
                  <div>
                    <div className="font-semibold flex items-center gap-1.5 flex-wrap">
                      <span>{t.preferredPlayerName || t.name}</span>
                      <span className="text-emerald-400">[{t.mcpRole}]</span>
                      {t.mcpRole === 'player' && (
                        <span
                          className={`px-1.5 py-0.5 rounded text-[10px] border ${
                            t.preferredPlayerName
                              ? 'bg-emerald-500/15 border-emerald-500/30 text-emerald-200'
                              : 'bg-amber-500/15 border-amber-500/30 text-amber-200'
                          }`}
                        >
                          {t.preferredPlayerName ? `绑定昵称: ${t.preferredPlayerName}` : '待 AI 首次进房自取名'}
                        </span>
                      )}
                      {t.revokedAt && <span className="text-rose-400">(已吊销)</span>}
                    </div>
                    <div className="text-[11px] text-slate-400 font-mono mt-0.5">
                      前缀 {t.tokenPrefix}... · 房间限制: {(t.allowedRooms || []).join(', ') || '不限'}
                      {t.personaStyle ? ` · 人设: ${t.personaStyle}` : ''}
                    </div>
                  </div>
                  {!t.revokedAt && (
                    <div className="flex items-center gap-2 shrink-0">
                      <button
                        type="button"
                        onClick={() => {
                          setEditingTokenId(editingTokenId === t.id ? null : t.id);
                          setEditingNickname(t.preferredPlayerName || '');
                          setEditingPersona(t.personaStyle || '');
                        }}
                        className="text-emerald-400 hover:underline cursor-pointer"
                      >
                        {t.preferredPlayerName ? '改名/人设' : '设昵称/人设'}
                      </button>
                      <button
                        type="button"
                        onClick={async () => {
                          await apiFetch(`/api/portal/mcp/tokens/${t.id}`, { method: 'DELETE' });
                          notify('Token 已吊销');
                          loadTokens();
                        }}
                        className="text-rose-400 hover:underline flex items-center gap-1 cursor-pointer"
                      >
                        <Trash2 className="w-3.5 h-3.5" /> 吊销
                      </button>
                    </div>
                  )}
                </div>

                {editingTokenId === t.id && (
                  <div className="flex flex-wrap items-center gap-1.5 pt-1 border-t border-slate-800/80">
                    <input
                      value={editingNickname}
                      onChange={(e) => setEditingNickname(e.target.value)}
                      maxLength={18}
                      placeholder="输入新昵称，或留空交回给 AI 下次自取名"
                      className="flex-1 min-w-36 px-2.5 py-1 rounded bg-slate-900 border border-slate-700 text-xs"
                    />
                    <input
                      value={editingPersona}
                      onChange={(e) => setEditingPersona(e.target.value)}
                      maxLength={80}
                      placeholder="性格台词人设（如 冷静学院派）"
                      className="flex-1 min-w-36 px-2.5 py-1 rounded bg-slate-900 border border-slate-700 text-xs"
                    />
                    <button
                      type="button"
                      onClick={() => saveTokenNickname(t.id, editingNickname, editingPersona)}
                      className="px-2.5 py-1 rounded bg-emerald-600 hover:bg-emerald-500 text-white font-semibold cursor-pointer"
                    >
                      保存
                    </button>
                    <button
                      type="button"
                      onClick={() => setEditingTokenId(null)}
                      className="px-2 py-1 rounded bg-slate-800 hover:bg-slate-700 text-slate-300 cursor-pointer"
                    >
                      取消
                    </button>
                  </div>
                )}
              </div>
            ))}
            {tokens.length === 0 && <div className="text-slate-500 py-6 text-center">暂未创建任何 AI Token</div>}
          </div>
        </div>

        <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-2.5">
          <div className="flex items-center justify-between gap-2 flex-wrap">
            <h3 className="text-sm font-semibold">外部 AI 客户端接入填法（streamable-http / JSON）</h3>
            <div className="flex items-center gap-1.5">
              <QuickCopyPill text={directMcpUrl} label="复制 URL" onCopied={() => notify('已复制 streamable-http URL')} />
              <QuickCopyPill text={mcpConfigJson} label="复制 JSON" onCopied={() => notify('已复制 MCP 配置 JSON')} />
            </div>
          </div>
          <div className="bg-slate-950 p-2.5 rounded-lg border border-slate-800 text-[11px] text-slate-300 space-y-1">
            <div>1. <span className="text-slate-400">serverName：</span><code className="text-emerald-300">ygoprosharp</code></div>
            <div>2. <span className="text-slate-400">transport：</span><code className="text-emerald-300">streamable-http</code></div>
            <div className="break-all">3. <span className="text-slate-400">url：</span><code className="text-emerald-300">{directMcpUrl}</code></div>
          </div>
          <pre className="bg-slate-950 p-3 rounded-lg border border-slate-800 text-[11px] font-mono overflow-x-auto text-slate-300">
            {mcpConfigJson}
          </pre>
        </div>
      </div>
    </div>
  );
}
