import React, { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Button,
  Card,
  Chip,
  Input,
  Label,
  Spinner,
  Surface,
  Tabs,
  TextField
} from '@heroui/react';
import { KeyRound, Plus, Sparkles, Terminal, Trash2 } from 'lucide-react';
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

const PROMPT_TEMPLATES = [
  {
    title: '1v1 陪练 / 盲盒上桌',
    prompt: '请使用 YGOProSharp MCP 工具查看当前房间列表，加入房间 Room01 准备并陪我打完这一局，出牌时保持人设简短互动。'
  },
  {
    title: '2v2 卡片力量双打搭档',
    prompt: '请作为我的 2v2 双打搭档加入房间 Tag01，先查看队友场面与战术信号（激进/稳健），协同资源完成双打对局。'
  },
  {
    title: '赛后全视角教练复盘',
    prompt: '请读取历史对局 R#1 的详细回合记录，总结胜负关键手、渣操伏笔，并为双方卡组打出六维评分与改进建议。'
  }
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
  const [tokenFilter, setTokenFilter] = useState<'active' | 'all'>('active');
  const [guideTab, setGuideTab] = useState<'json' | 'cli' | 'prompts'>('json');
  const [name, setName] = useState('');
  const [personaStyle, setPersonaStyle] = useState('');
  const [role, setRole] = useState('player');
  const [roomsText, setRoomsText] = useState('');
  const [expiresHours, setExpiresHours] = useState(168);
  const [issuing, setIssuing] = useState(false);
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

  const filteredTokens = useMemo(() => {
    if (tokenFilter === 'all') return tokens;
    return tokens.filter((t) => !t.revokedAt);
  }, [tokens, tokenFilter]);

  if (!user) {
    return (
      <Card className="rounded-xl p-8 sm:p-12 text-center items-center space-y-4 animate-fade-in">
        <div className="w-12 h-12 rounded-xl bg-emerald-500/15 border border-emerald-500/30 flex items-center justify-center mx-auto">
          <KeyRound className="w-6 h-6 text-emerald-400" />
        </div>
        <Card.Title className="text-base sm:text-lg font-bold">解锁专属外部 AI 决斗搭档与复盘教练令牌</Card.Title>
        <Card.Description className="text-xs sm:text-sm max-w-lg mx-auto leading-relaxed">
          免费注册后可自助签发受限视角 <code className="text-emerald-400 font-mono">player</code>（上桌对局 / 2v2 双打搭档）或{' '}
          <code className="text-sky-400 font-mono">spectator</code>（观战解说 / 赛后复盘）令牌，一键接入 Claude、Gemini、Cursor、T3 Code 等任意 MCP 客户端。
        </Card.Description>
        <Button variant="primary" onPress={onRequireAuth}>
          立即注册 / 登录账号
        </Button>
      </Card>
    );
  }

  const issueToken = async (e: React.FormEvent) => {
    e.preventDefault();
    setIssuing(true);
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
    } finally {
      setIssuing(false);
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
  const directMcpUrl = `${mcpUrl}?key=${newlyIssuedRaw ? encodeURIComponent(newlyIssuedRaw) : '<粘贴你的_ygp_mcp_Token>'}`;
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
  const claudeCliCmd = `claude mcp add --transport http ygoprosharp "${directMcpUrl}"`;

  return (
    <div className="grid grid-cols-1 lg:grid-cols-12 gap-4 items-start text-xs animate-fade-in">
      {/* 左侧主区：令牌签发 + 令牌花名册 (7列) */}
      <div className="lg:col-span-7 space-y-4">
        <Card className="rounded-xl p-4 sm:p-5 space-y-4">
          <Card.Header className="p-0 flex-row items-start justify-between gap-3">
            <div>
              <Card.Title className="text-sm sm:text-base font-bold flex items-center gap-2">
                <KeyRound className="w-4 h-4 text-emerald-400" /> 签发外部 AI 决斗 / 复盘令牌
              </Card.Title>
              <Card.Description className="text-[11px] mt-0.5">
                严格执行 1v1 / 2v2 战争迷雾与单向 SHA-256 摘要存储；支持绑定专属昵称、台词性格与跨局对局记忆
              </Card.Description>
            </div>
            <Chip size="sm" variant="soft" color="accent" className="font-mono text-[10px] shrink-0">
              零内置 LLM Key
            </Chip>
          </Card.Header>

          <form onSubmit={issueToken} className="space-y-3.5">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <TextField fullWidth value={name} onChange={setName}>
                <Label className="text-xs">AI 决斗者昵称（留空进房自取名）</Label>
                <Input variant="secondary" maxLength={18} placeholder="如 海马濑人 / 留空由 AI 自取" />
              </TextField>

              <TextField fullWidth value={personaStyle} onChange={setPersonaStyle}>
                <Label className="text-xs">性格与吐槽人设（选填）</Label>
                <Input variant="secondary" maxLength={80} placeholder="如：孤高青眼社长，霸气护短" />
              </TextField>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
              <div className="space-y-1">
                <label className="block text-xs font-medium text-foreground">角色权限</label>
                <select
                  value={role}
                  onChange={(e) => setRole(e.target.value)}
                  className="w-full h-9 px-3 text-xs input-modern"
                >
                  <option value="player">对战选手 AI (player)</option>
                  <option value="spectator">观战解说/复盘 AI (spectator)</option>
                </select>
              </div>

              <TextField fullWidth value={roomsText} onChange={setRoomsText}>
                <Label className="text-xs">限定房间白名单（选填）</Label>
                <Input variant="secondary" placeholder="逗号分隔，留空不限" />
              </TextField>

              <div className="space-y-1">
                <label className="block text-xs font-medium text-foreground">令牌有效期</label>
                <select
                  value={expiresHours}
                  onChange={(e) => setExpiresHours(Number(e.target.value))}
                  className="w-full h-9 px-3 text-xs input-modern"
                >
                  <option value={12}>12 小时</option>
                  <option value={48}>2 天</option>
                  <option value={168}>7 天</option>
                  <option value={720}>30 天</option>
                </select>
              </div>
            </div>

            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pt-0.5">
              <div className="flex items-center gap-1.5 flex-wrap">
                <span className="text-[11px] text-muted flex items-center gap-1">
                  <Sparkles className="w-3 h-3 text-amber-400" /> 人设灵感:
                </span>
                {PERSONA_PRESETS.map((preset) => (
                  <button
                    key={preset}
                    type="button"
                    onClick={() => setPersonaStyle(preset)}
                    className={`px-2 py-0.5 rounded-md border text-[11px] transition cursor-pointer ${
                      personaStyle === preset
                        ? 'bg-surface-secondary border-accent text-foreground font-semibold'
                        : 'bg-background border-border text-muted hover:text-foreground'
                    }`}
                  >
                    {preset}
                  </button>
                ))}
              </div>

              <Button type="submit" variant="primary" isPending={issuing} className="w-full sm:w-auto shrink-0">
                {issuing ? <Spinner color="current" size="sm" /> : <Plus className="w-3.5 h-3.5" />}
                <span>生成 AI Token</span>
              </Button>
            </div>
          </form>

          {newlyIssuedRaw && (
            <Alert status="success" className="animate-fade-in">
              <Alert.Indicator />
              <Alert.Content className="space-y-2">
                <Alert.Title className="text-xs font-semibold">
                  令牌签发成功（明文仅展示一次，请立即复制保存）
                </Alert.Title>
                <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 p-2.5 rounded-lg bg-background border border-border font-mono text-[11px]">
                  <span className="break-all text-emerald-400">{directMcpUrl}</span>
                  <QuickCopyPill text={directMcpUrl} label="复制直连 URL" onCopied={() => notify('已复制 MCP 直连 URL')} />
                </div>
                <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 font-mono text-[11px] text-muted">
                  <span className="break-all">纯 Token: {newlyIssuedRaw}</span>
                  <QuickCopyPill text={newlyIssuedRaw} label="复制纯 Token" onCopied={() => notify('已复制纯 Token')} />
                </div>
              </Alert.Content>
            </Alert>
          )}
        </Card>

        {/* 已签发令牌花名册 */}
        <Card className="rounded-xl p-4 sm:p-5 space-y-3.5">
          <div className="flex items-center justify-between gap-2 flex-wrap">
            <h3 className="text-sm font-bold">我的 AI 令牌花名册 ({filteredTokens.length})</h3>
            <Tabs
              selectedKey={tokenFilter}
              onSelectionChange={(k) => setTokenFilter(String(k) as 'active' | 'all')}
            >
              <Tabs.ListContainer>
                <Tabs.List aria-label="令牌状态过滤">
                  <Tabs.Tab id="active" className="text-xs px-2.5">
                    有效令牌
                    <Tabs.Indicator />
                  </Tabs.Tab>
                  <Tabs.Tab id="all" className="text-xs px-2.5">
                    全部 ({tokens.length})
                    <Tabs.Indicator />
                  </Tabs.Tab>
                </Tabs.List>
              </Tabs.ListContainer>
            </Tabs>
          </div>

          <div className="space-y-2.5 max-h-[460px] overflow-y-auto pr-0.5">
            {filteredTokens.map((t) => (
              <Surface key={t.id} variant="secondary" className="p-3.5 rounded-xl border border-border space-y-2.5">
                <div className="flex items-start justify-between gap-2">
                  <div className="min-w-0 space-y-1">
                    <div className="font-semibold flex items-center gap-1.5 flex-wrap">
                      <span className="text-sm text-foreground">{t.preferredPlayerName || t.name}</span>
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
                          {t.preferredPlayerName ? '固定昵称' : '进房自取名'}
                        </Chip>
                      )}
                      {t.revokedAt && (
                        <Chip size="sm" variant="soft" color="danger" className="h-5 px-2 text-[10px]">
                          已吊销
                        </Chip>
                      )}
                    </div>
                    <div className="text-[11px] text-muted font-mono">
                      {t.tokenPrefix}... · 允许房间: {(t.allowedRooms || []).join(', ') || '全部公开房'}
                    </div>
                    {t.personaStyle && (
                      <div className="text-[11px] text-amber-400/90">人设：「{t.personaStyle}」</div>
                    )}
                  </div>

                  {!t.revokedAt && (
                    <div className="flex items-center gap-1.5 shrink-0">
                      <Button
                        size="sm"
                        variant="secondary"
                        className="h-7 px-2.5 text-[11px]"
                        onPress={() => {
                          setEditingTokenId(editingTokenId === t.id ? null : t.id);
                          setEditingNickname(t.preferredPlayerName || '');
                          setEditingPersona(t.personaStyle || '');
                        }}
                      >
                        编辑档案
                      </Button>
                      <Button
                        isIconOnly
                        size="sm"
                        variant="danger-soft"
                        aria-label="吊销该令牌"
                        className="h-7 w-7 min-w-0"
                        onPress={async () => {
                          await apiFetch(`/api/portal/mcp/tokens/${t.id}`, { method: 'DELETE' });
                          notify('Token 已吊销');
                          loadTokens();
                        }}
                      >
                        <Trash2 className="w-3.5 h-3.5" />
                      </Button>
                    </div>
                  )}
                </div>

                {editingTokenId === t.id && (
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5 pt-2.5 border-t border-separator animate-fade-in">
                    <TextField fullWidth value={editingNickname} onChange={setEditingNickname}>
                      <Label className="text-[11px]">决斗者昵称</Label>
                      <Input variant="secondary" maxLength={18} placeholder="留空交由 AI 自取名" />
                    </TextField>
                    <TextField fullWidth value={editingPersona} onChange={setEditingPersona}>
                      <Label className="text-[11px]">性格台词人设</Label>
                      <Input variant="secondary" maxLength={80} placeholder="如 冷静学院派战术家" />
                    </TextField>
                    <div className="sm:col-span-2 flex justify-end gap-2">
                      <Button size="sm" variant="tertiary" onPress={() => setEditingTokenId(null)}>
                        取消
                      </Button>
                      <Button
                        size="sm"
                        variant="primary"
                        onPress={() => saveTokenNickname(t.id, editingNickname, editingPersona)}
                      >
                        保存档案
                      </Button>
                    </div>
                  </div>
                )}
              </Surface>
            ))}

            {filteredTokens.length === 0 && (
              <div className="text-muted py-10 text-center bg-surface-secondary rounded-xl border border-border">
                当前筛选下暂无 AI Token，可在上方一键签发
              </div>
            )}
          </div>
        </Card>
      </div>

      {/* 右侧分栏：多客户端一键接入向导 & 对战提示词库 (5列) */}
      <Card className="lg:col-span-5 lg:sticky lg:top-4 rounded-xl p-4 sm:p-5 space-y-3.5">
        <div className="flex items-center justify-between gap-2">
          <div className="font-bold text-sm flex items-center gap-1.5">
            <Terminal className="w-4 h-4 text-emerald-400" /> 外部 AI 客户端接入向导
          </div>
          <QuickCopyPill text={directMcpUrl} label="复制直连 URL" onCopied={() => notify('已复制 streamable-http URL')} />
        </div>

        <Tabs
          selectedKey={guideTab}
          onSelectionChange={(k) => setGuideTab(String(k) as 'json' | 'cli' | 'prompts')}
          className="w-full"
        >
          <Tabs.ListContainer>
            <Tabs.List aria-label="客户端配置与提示词标签" className="w-full">
              <Tabs.Tab id="json" className="flex-1 text-xs">
                MCP JSON
                <Tabs.Indicator />
              </Tabs.Tab>
              <Tabs.Tab id="cli" className="flex-1 text-xs">
                命令行一键挂载
                <Tabs.Indicator />
              </Tabs.Tab>
              <Tabs.Tab id="prompts" className="flex-1 text-xs">
                开局提示词
                <Tabs.Indicator />
              </Tabs.Tab>
            </Tabs.List>
          </Tabs.ListContainer>
        </Tabs>

        {guideTab === 'json' && (
          <div className="space-y-2.5 animate-fade-in">
            <Surface variant="secondary" className="p-3 rounded-lg border border-border text-[11px] text-muted space-y-1 leading-relaxed">
              <div>适用于 Cursor、Claude Desktop、T3 Code、Cherry Studio 等支持 <code className="text-foreground font-mono">streamable-http</code> 的客户端：</div>
            </Surface>
            <div className="relative">
              <pre className="p-3.5 rounded-lg bg-background border border-border text-[11px] font-mono overflow-x-auto text-foreground/90">
                {mcpConfigJson}
              </pre>
              <div className="mt-2 flex justify-end">
                <QuickCopyPill text={mcpConfigJson} label="复制 MCP JSON" onCopied={() => notify('已复制 MCP 配置 JSON')} />
              </div>
            </div>
          </div>
        )}

        {guideTab === 'cli' && (
          <div className="space-y-2.5 animate-fade-in">
            <Surface variant="secondary" className="p-3 rounded-lg border border-border text-[11px] text-muted leading-relaxed">
              在终端执行以下命令即可将本服挂载到 Claude Code，挂载后直接输入自然语言即可指派 AI 进房打牌或复盘：
            </Surface>
            <pre className="p-3.5 rounded-lg bg-background border border-border text-[11px] font-mono overflow-x-auto text-emerald-400">
              {claudeCliCmd}
            </pre>
            <div className="flex justify-end">
              <QuickCopyPill text={claudeCliCmd} label="复制 CLI 命令" onCopied={() => notify('已复制 CLI 命令')} />
            </div>
          </div>
        )}

        {guideTab === 'prompts' && (
          <div className="space-y-2.5 animate-fade-in">
            {PROMPT_TEMPLATES.map((tpl) => (
              <Surface key={tpl.title} variant="secondary" className="p-3 rounded-lg border border-border space-y-1.5">
                <div className="flex items-center justify-between gap-2">
                  <span className="font-semibold text-foreground">{tpl.title}</span>
                  <QuickCopyPill text={tpl.prompt} label="复制话术" onCopied={() => notify(`已复制：${tpl.title}`)} />
                </div>
                <p className="text-[11px] text-muted leading-relaxed">{tpl.prompt}</p>
              </Surface>
            ))}
          </div>
        )}
      </Card>
    </div>
  );
}
