import React, { useCallback, useEffect, useState } from 'react';
import { KeyRound, Sparkles, Trash2 } from 'lucide-react';
import { apiFetch } from '../api';
import { QuickCopyPill } from '../RoomConfigurator';
import type { PortalUser } from './portalTypes';

const PERSONA_PRESETS = [
  '中二热血决斗王',
  '傲岸青眼社长',
  '毒舌技术流解说',
  '温柔可靠双打学姐',
  '冷静学院派战术家',
  '相声整活大师'
];

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
      <div className="glass-card rounded-2xl p-8 sm:p-12 text-center space-y-4 animate-fade-in">
        <div className="w-14 h-14 rounded-2xl bg-emerald-500/15 border border-emerald-500/30 flex items-center justify-center mx-auto">
          <KeyRound className="w-7 h-7 text-emerald-400" />
        </div>
        <h2 className="text-base sm:text-lg font-bold">解锁你的专属外部 AI 决斗搭档与解说令牌</h2>
        <p className="text-xs sm:text-sm text-slate-400 max-w-lg mx-auto leading-relaxed">
          免费注册后可自助签发受限视角 <code className="text-emerald-300">player</code>（上桌打牌/双排搭档）或{' '}
          <code className="text-sky-300">spectator</code>（赛后复盘教练/解说）令牌，零配置接入 Claude、Gemini、DeepSeek、ChatGPT、Cursor 等任意 MCP 客户端。
        </p>
        <button
          onClick={onRequireAuth}
          className="min-h-[44px] px-6 py-2.5 rounded-xl bg-gradient-to-r from-emerald-600 to-teal-500 hover:from-emerald-500 hover:to-teal-400 text-sm font-semibold text-white shadow-lg shadow-emerald-950/50 cursor-pointer"
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
      notify(trimmedName ? `AI Token 创建成功（绑定昵称：${trimmedName}）！` : 'AI Token 创建成功（首次进房时将由 AI 自主取名）！');
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
    <div className="space-y-5 text-xs animate-fade-in">
      <div className="glass-card rounded-2xl p-4 sm:p-5 space-y-4">
        <div className="flex items-start justify-between gap-3 flex-wrap">
          <div>
            <h2 className="text-sm sm:text-base font-bold flex items-center gap-2 text-slate-100">
              <KeyRound className="w-4 h-4 text-emerald-400" /> 创建外部 AI 决斗 / 复盘令牌
            </h2>
            <p className="text-[11px] text-slate-400 mt-0.5">
              严格执行 1v1 / 2v2 战争迷雾与单向 SHA-256 哈希存储，支持绑定专属昵称、性格吐槽人设与跨局生涯记忆
            </p>
          </div>
        </div>

        <form onSubmit={issueToken} className="space-y-3">
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-3">
            <div>
              <label className="block text-slate-400 mb-1">AI 决斗者昵称（留空自取名）</label>
              <input
                value={name}
                onChange={(e) => setName(e.target.value)}
                maxLength={18}
                placeholder="留空由 AI 自取，或填 海马濑人"
                className="w-full px-3 py-2 input-modern"
              />
            </div>
            <div className="sm:col-span-1 lg:col-span-2">
              <label className="block text-slate-400 mb-1">性格与台词人设（可选，点选下方标签快速填入）</label>
              <input
                value={personaStyle}
                onChange={(e) => setPersonaStyle(e.target.value)}
                maxLength={80}
                placeholder="如：孤高青眼社长，出牌霸气但尊重搭档"
                className="w-full px-3 py-2 input-modern"
              />
            </div>
            <div>
              <label className="block text-slate-400 mb-1">角色权限</label>
              <select value={role} onChange={(e) => setRole(e.target.value)} className="w-full px-3 py-2 input-modern">
                <option value="player">对战选手 AI (player)</option>
                <option value="spectator">观战解说/复盘 AI (spectator)</option>
              </select>
            </div>
            <div className="grid grid-cols-2 gap-2">
              <div>
                <label className="block text-slate-400 mb-1">房间白名单</label>
                <input
                  value={roomsText}
                  onChange={(e) => setRoomsText(e.target.value)}
                  placeholder="留空不限"
                  className="w-full px-2.5 py-2 input-modern"
                />
              </div>
              <div>
                <label className="block text-slate-400 mb-1">有效期</label>
                <select
                  value={expiresHours}
                  onChange={(e) => setExpiresHours(Number(e.target.value))}
                  className="w-full px-2 py-2 input-modern"
                >
                  <option value={12}>12 小时</option>
                  <option value={48}>2 天</option>
                  <option value={168}>7 天</option>
                  <option value={720}>30 天</option>
                </select>
              </div>
            </div>
          </div>

          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pt-1">
            <div className="flex items-center gap-1.5 flex-wrap">
              <span className="text-[11px] text-slate-400 flex items-center gap-1">
                <Sparkles className="w-3 h-3 text-amber-400" /> 人设灵感：
              </span>
              {PERSONA_PRESETS.map((preset) => (
                <button
                  key={preset}
                  type="button"
                  onClick={() => setPersonaStyle(preset)}
                  className={`px-2.5 py-1 rounded-full border text-[11px] transition cursor-pointer ${
                    personaStyle === preset
                      ? 'bg-emerald-500/20 border-emerald-400/50 text-emerald-200'
                      : 'bg-slate-950/80 border-slate-800 text-slate-300 hover:border-slate-700'
                  }`}
                >
                  {preset}
                </button>
              ))}
            </div>

            <button className="w-full sm:w-auto min-h-[40px] px-5 py-2 rounded-xl bg-gradient-to-r from-emerald-600 to-teal-500 hover:from-emerald-500 hover:to-teal-400 font-semibold text-white shadow-lg shadow-emerald-950/50 shrink-0 cursor-pointer">
              + 生成 AI Token
            </button>
          </div>
        </form>

        {newlyIssuedRaw && (
          <div className="p-4 rounded-2xl bg-emerald-950/60 border border-emerald-500/40 space-y-2.5 font-mono animate-fade-in">
            <div className="text-xs font-sans font-semibold text-emerald-300">
              ✓ 令牌签发成功（明文仅展示一次，请立即复制保存）：
            </div>
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 bg-slate-950/90 p-2.5 rounded-xl border border-slate-800">
              <span className="break-all text-emerald-200 text-[11px]">{directMcpUrl}</span>
              <QuickCopyPill text={directMcpUrl} label="复制 MCP 直连 URL" onCopied={() => notify('已复制 MCP 直连 URL')} />
            </div>
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 text-slate-300 text-[11px]">
              <span className="break-all">纯 Token：{newlyIssuedRaw}</span>
              <QuickCopyPill text={newlyIssuedRaw} label="复制纯 Token" onCopied={() => notify('已复制 AI Token')} />
            </div>
          </div>
        )}
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <div className="glass-card rounded-2xl p-4 sm:p-5 space-y-3">
          <div className="flex items-center justify-between">
            <h3 className="text-sm font-bold">我的 AI 令牌列表 ({tokens.length})</h3>
          </div>
          <div className="space-y-2.5 max-h-96 overflow-y-auto pr-0.5">
            {tokens.map((t) => (
              <div key={t.id} className="glass-subcard p-3.5 rounded-xl space-y-2.5">
                <div className="flex items-start justify-between gap-2">
                  <div className="min-w-0 space-y-1">
                    <div className="font-semibold flex items-center gap-1.5 flex-wrap">
                      <span className="text-sm text-slate-100">{t.preferredPlayerName || t.name}</span>
                      <span className="px-1.5 py-0.5 rounded bg-emerald-500/15 text-emerald-300 text-[10px] font-mono">
                        {t.mcpRole}
                      </span>
                      {t.mcpRole === 'player' && (
                        <span
                          className={`px-2 py-0.5 rounded-full text-[10px] border ${
                            t.preferredPlayerName
                              ? 'bg-emerald-500/15 border-emerald-500/30 text-emerald-200'
                              : 'bg-amber-500/15 border-amber-500/30 text-amber-200'
                          }`}
                        >
                          {t.preferredPlayerName ? '已固定昵称' : '进房自取名'}
                        </span>
                      )}
                      {t.revokedAt && <span className="text-rose-400 text-[11px]">(已吊销)</span>}
                    </div>
                    <div className="text-[11px] text-slate-400 font-mono">
                      {t.tokenPrefix}... · 房间: {(t.allowedRooms || []).join(', ') || '全部公开房'}
                    </div>
                    {t.personaStyle && (
                      <div className="text-[11px] text-amber-200/90">人设：「{t.personaStyle}」</div>
                    )}
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
                        className="px-2.5 py-1 rounded-lg bg-slate-800 hover:bg-slate-700 text-emerald-300 cursor-pointer"
                      >
                        编辑档案
                      </button>
                      <button
                        type="button"
                        onClick={async () => {
                          await apiFetch(`/api/portal/mcp/tokens/${t.id}`, { method: 'DELETE' });
                          notify('Token 已吊销');
                          loadTokens();
                        }}
                        className="p-1.5 rounded-lg bg-rose-950/50 hover:bg-rose-900/60 text-rose-300 cursor-pointer"
                        title="吊销该令牌"
                      >
                        <Trash2 className="w-3.5 h-3.5" />
                      </button>
                    </div>
                  )}
                </div>

                {editingTokenId === t.id && (
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 pt-2 border-t border-slate-800/80 animate-fade-in">
                    <input
                      value={editingNickname}
                      onChange={(e) => setEditingNickname(e.target.value)}
                      maxLength={18}
                      placeholder="新昵称（留空交由 AI 自取名）"
                      className="px-2.5 py-1.5 input-modern"
                    />
                    <input
                      value={editingPersona}
                      onChange={(e) => setEditingPersona(e.target.value)}
                      maxLength={80}
                      placeholder="性格台词人设（如 冷静学院派）"
                      className="px-2.5 py-1.5 input-modern"
                    />
                    <div className="sm:col-span-2 flex justify-end gap-2">
                      <button
                        type="button"
                        onClick={() => setEditingTokenId(null)}
                        className="px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 cursor-pointer"
                      >
                        取消
                      </button>
                      <button
                        type="button"
                        onClick={() => saveTokenNickname(t.id, editingNickname, editingPersona)}
                        className="px-3.5 py-1.5 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white font-semibold cursor-pointer"
                      >
                        保存档案
                      </button>
                    </div>
                  </div>
                )}
              </div>
            ))}
            {tokens.length === 0 && (
              <div className="text-slate-500 py-10 text-center glass-subcard rounded-xl">
                暂未创建任何 AI Token，可在上方一键生成
              </div>
            )}
          </div>
        </div>

        <div className="glass-card rounded-2xl p-4 sm:p-5 space-y-3">
          <div className="flex items-center justify-between gap-2 flex-wrap">
            <h3 className="text-sm font-bold">3 步接入任意外部 AI 客户端</h3>
            <div className="flex items-center gap-1.5">
              <QuickCopyPill text={directMcpUrl} label="复制 URL" onCopied={() => notify('已复制 streamable-http URL')} />
              <QuickCopyPill text={mcpConfigJson} label="复制 JSON" onCopied={() => notify('已复制 MCP 配置 JSON')} />
            </div>
          </div>
          <div className="glass-subcard p-3 rounded-xl text-[11px] text-slate-300 space-y-1.5 leading-relaxed">
            <div>
              <span className="inline-block w-5 text-emerald-400 font-bold">①</span> 在上方生成一枚 Token，复制右侧{' '}
              <span className="text-emerald-300 font-medium">MCP 直连 URL</span>；
            </div>
            <div>
              <span className="inline-block w-5 text-emerald-400 font-bold">②</span> 打开 Claude Desktop / Cursor / Cherry Studio / Gemini CLI 添加 MCP Server（类型选 <code className="text-emerald-300">streamable-http</code>）；
            </div>
            <div>
              <span className="inline-block w-5 text-emerald-400 font-bold">③</span> 对 AI 说：<span className="text-amber-200">「加入房间 Room01 陪我打一局！」</span> 即可自动上桌对决！
            </div>
          </div>
          <pre className="glass-subcard p-3.5 rounded-xl text-[11px] font-mono overflow-x-auto text-slate-300">
            {mcpConfigJson}
          </pre>
        </div>
      </div>
    </div>
  );
}
