import React, { useEffect, useState, useCallback, useRef } from 'react';
import {
  Gamepad2,
  Sparkles,
  Swords,
  Video,
  Copy,
  Check,
  Download,
  ShieldCheck,
  Send,
  Search,
  Eye,
  KeyRound,
  UserPlus,
  LogOut,
  Plus,
  Trash2,
  Lock
} from 'lucide-react';
import { apiFetch, setCsrfToken } from './api';
import { QuickCopyPill, RoomConfigurator } from './RoomConfigurator';

type PortalTab = 'lobby' | 'ai_tokens' | 'replays' | 'catalog' | 'selfservice';

export interface SurfaceInfo {
  surface: 'admin' | 'portal';
  gamePort: number;
  adminPort?: number;
  portalPort?: number;
  publicGameHost?: string;
  mcpPortHint?: number;
}

interface PortalUser {
  userId: string;
  username: string;
  displayName: string;
  mustChangePassword: boolean;
  csrfToken: string;
  roleCodes: string[];
  allowedMcpRolesToIssue: string[];
}

export function UserPortalApp({ surfaceInfo }: { surfaceInfo: SurfaceInfo }) {
  const [tab, setTab] = useState<PortalTab>('lobby');
  const [portalUser, setPortalUser] = useState<PortalUser | null>(null);
  const [authModalMode, setAuthModalMode] = useState<'login' | 'register' | 'password' | null>(null);
  const [showConfigurator, setShowConfigurator] = useState(true);

  const [overview, setOverview] = useState<any>(null);
  const [rooms, setRooms] = useState<any[]>([]);
  const [selectedRoom, setSelectedRoom] = useState<any>(null);
  const [replays, setReplays] = useState<any[]>([]);
  const [diyPackages, setDiyPackages] = useState<any[]>([]);
  const [cards, setCards] = useState<any[]>([]);
  const [cardQuery, setCardQuery] = useState('');
  const [diyOnlySearch, setDiyOnlySearch] = useState(false);
  const [chaosDeck, setChaosDeck] = useState<any>(null);
  const [toast, setToast] = useState<{ text: string; kind: 'ok' | 'err' } | null>(null);
  const wsRef = useRef<WebSocket | null>(null);

  // 玩家自助表单状态
  const [vpassName, setVpassName] = useState('');
  const [vpassCode, setVpassCode] = useState('');
  const [vpassResult, setVpassResult] = useState('');

  const [tTag, setTTag] = useState('weekly');
  const [tPlayer, setTPlayer] = useState('');
  const [tYdk, setTYdk] = useState('');
  const [tResultHash, setTResultHash] = useState('');

  const [propAuthor, setPropAuthor] = useState('');
  const [propPkg, setPropPkg] = useState('community-diy');
  const [propCardId, setPropCardId] = useState(99100001);
  const [propCardName, setPropCardName] = useState('');
  const [propCardDesc, setPropCardDesc] = useState('');
  const [propLua, setPropLua] = useState(
    `local s,id=GetID()\nfunction s.initial_effect(c)\n\t-- 在此编写卡片效果 Lua 脚本\nend\n`
  );

  const notify = useCallback((text: string, kind: 'ok' | 'err' = 'ok') => {
    setToast({ text, kind });
    setTimeout(() => setToast((cur) => (cur?.text === text ? null : cur)), 3800);
  }, []);

  const refreshPortalAuth = useCallback(async () => {
    try {
      const me = await apiFetch<{ authenticated: boolean; user: PortalUser | null }>('/api/portal/auth/me');
      if (me.authenticated && me.user) {
        setCsrfToken(me.user.csrfToken);
        setPortalUser(me.user);
      } else {
        setCsrfToken('');
        setPortalUser(null);
      }
    } catch {
      setCsrfToken('');
      setPortalUser(null);
    }
  }, []);

  const loadLobby = useCallback(async () => {
    try {
      const [ov, rm, rp, pk] = await Promise.all([
        apiFetch('/api/portal/overview'),
        apiFetch('/api/portal/rooms'),
        apiFetch('/api/portal/replays?limit=30'),
        apiFetch('/api/portal/diy/packages')
      ]);
      setOverview(ov);
      setRooms(rm.rooms || []);
      setReplays(rp.replays || []);
      setDiyPackages(pk.packages || []);
    } catch (err) {
      notify(err instanceof Error ? err.message : '读取大厅状态失败', 'err');
    }
  }, [notify]);

  const openRoomDetail = useCallback(
    async (roomName: string) => {
      try {
        const res = await apiFetch(`/api/portal/rooms/${encodeURIComponent(roomName)}`);
        setSelectedRoom(res.room);
      } catch (err) {
        notify(err instanceof Error ? err.message : '房间已结束', 'err');
      }
    },
    [notify]
  );

  useEffect(() => {
    refreshPortalAuth();
    loadLobby();
    const proto = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const ws = new WebSocket(`${proto}//${window.location.host}/ws/public`);
    ws.onmessage = () => {
      loadLobby();
    };
    wsRef.current = ws;
    return () => {
      wsRef.current?.close();
    };
  }, [loadLobby, refreshPortalAuth]);

  const handlePortalLogout = async () => {
    try {
      await apiFetch('/api/portal/auth/logout', { method: 'POST', body: '{}' });
    } catch {
      // ignore
    }
    setCsrfToken('');
    setPortalUser(null);
    notify('已退出玩家账号');
  };

  const closeOwnRoom = async (roomName: string) => {
    try {
      await apiFetch(`/api/portal/rooms/${encodeURIComponent(roomName)}`, { method: 'DELETE' });
      notify(`已关闭房间 ${roomName}`);
      if (selectedRoom?.summary?.roomName === roomName) setSelectedRoom(null);
      loadLobby();
    } catch (err) {
      notify(err instanceof Error ? err.message : '关闭房间失败', 'err');
    }
  };

  const runSearch = async (e?: React.FormEvent) => {
    e?.preventDefault();
    try {
      const res = await apiFetch(
        `/api/portal/cards/search?q=${encodeURIComponent(cardQuery)}&diyOnly=${diyOnlySearch}&limit=36`
      );
      setCards(res.cards || []);
    } catch (err) {
      notify(err instanceof Error ? err.message : '检索失败', 'err');
    }
  };

  const drawChaos = async () => {
    try {
      const res = await apiFetch('/api/portal/decks/chaos-preview', {
        method: 'POST',
        body: JSON.stringify({ mainSize: 40, pureChaos: false })
      });
      setChaosDeck(res);
    } catch (err) {
      notify(err instanceof Error ? err.message : '试抽失败', 'err');
    }
  };

  const submitVpass = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const res = await apiFetch('/api/portal/player/vpass', {
        method: 'POST',
        body: JSON.stringify({ playerName: vpassName, vpass: vpassCode })
      });
      setVpassResult(res.loginFormat);
      notify(res.message);
    } catch (err) {
      notify(err instanceof Error ? err.message : '绑定失败', 'err');
    }
  };

  const submitTournamentDeck = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const res = await apiFetch('/api/portal/tournament-decks', {
        method: 'POST',
        body: JSON.stringify({ tournamentTag: tTag, playerName: tPlayer, ydkContent: tYdk })
      });
      setTResultHash(res.deckHash);
      notify(`交牌成功！主:${res.mainCount} 额:${res.extraCount} 副:${res.sideCount}`);
    } catch (err) {
      notify(err instanceof Error ? err.message : '交牌失败', 'err');
    }
  };

  const submitDiyProposal = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const res = await apiFetch('/api/portal/diy/proposals', {
        method: 'POST',
        body: JSON.stringify({
          packageId: propPkg,
          authorName: propAuthor || portalUser?.username,
          card: {
            id: Number(propCardId),
            ot: 4,
            type: 0x21,
            level: 4,
            race: 1,
            attribute: 1,
            attack: 1800,
            defense: 1200,
            name: propCardName,
            desc: propCardDesc
          },
          luaScript: propLua
        })
      });
      setPropCardName('');
      setPropCardDesc('');
      notify(res.message);
    } catch (err) {
      notify(err instanceof Error ? err.message : '投稿失败', 'err');
    }
  };

  const gameHost = overview?.publicGameHost || surfaceInfo.publicGameHost || window.location.hostname;
  const gamePort = overview?.gamePort || surfaceInfo.gamePort || 7911;

  return (
    <div className="min-h-screen bg-slate-950 text-slate-100 flex flex-col">
      <header className="border-b border-emerald-900/50 bg-slate-900/90 backdrop-blur sticky top-0 z-30">
        <div className="max-w-7xl mx-auto px-4 h-14 flex items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <div className="w-8 h-8 rounded-lg bg-emerald-600 flex items-center justify-center font-bold text-white shadow">
              Y
            </div>
            <div>
              <div className="font-semibold text-sm flex items-center gap-2">
                YGOProSharp 玩家大厅
                <span className="px-2 py-0.5 rounded text-[11px] bg-emerald-500/20 text-emerald-300 border border-emerald-500/30">
                  用户面板独立端口 :{window.location.port || surfaceInfo.portalPort}
                </span>
              </div>
              <div className="text-[11px] text-slate-400">自助建房配置 · 观众列表围观 · 个人 AI Token · 云录像回放</div>
            </div>
          </div>

          <nav className="flex items-center gap-1 overflow-x-auto">
            {(
              [
                ['lobby', '实时大厅 & 建房配置', <Gamepad2 className="w-4 h-4" />],
                ['ai_tokens', '我的 AI Token', <KeyRound className="w-4 h-4" />],
                ['replays', '战报榜 & 云录像', <Video className="w-4 h-4" />],
                ['catalog', 'DIY 扩展包 & 卡查', <Sparkles className="w-4 h-4" />],
                ['selfservice', '玩家自助 & DIY 投稿', <ShieldCheck className="w-4 h-4" />]
              ] as [PortalTab, string, React.ReactNode][]
            ).map(([k, label, icon]) => (
              <button
                key={k}
                onClick={() => setTab(k)}
                className={`px-3 py-1.5 rounded-md text-xs font-medium flex items-center gap-1.5 transition cursor-pointer ${
                  tab === k ? 'bg-emerald-600 text-white' : 'text-slate-300 hover:bg-slate-800'
                }`}
              >
                {icon}
                {label}
              </button>
            ))}
          </nav>

          <div className="flex items-center gap-2">
            <div className="hidden xl:flex items-center gap-1.5 text-xs bg-slate-950 px-2.5 py-1.5 rounded-lg border border-slate-800">
              <span className="text-slate-400">联机地址:</span>
              <span className="font-mono text-emerald-300 font-semibold">
                {gameHost}:{gamePort}
              </span>
              <SmallCopy text={`${gameHost}:${gamePort}`} />
            </div>

            {portalUser ? (
              <div className="flex items-center gap-1.5">
                <button
                  onClick={() => setAuthModalMode('password')}
                  className="px-2.5 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-xs font-medium text-emerald-300 cursor-pointer"
                  title="点击修改个人用户名或密码"
                >
                  {portalUser.displayName}
                </button>
                <button
                  onClick={handlePortalLogout}
                  className="p-1.5 rounded-lg text-slate-400 hover:text-rose-400 hover:bg-slate-800 cursor-pointer"
                  title="退出登录"
                >
                  <LogOut className="w-4 h-4" />
                </button>
              </div>
            ) : (
              <div className="flex items-center gap-1.5">
                <button
                  onClick={() => setAuthModalMode('login')}
                  className="px-2.5 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-xs font-medium cursor-pointer"
                >
                  登录
                </button>
                <button
                  onClick={() => setAuthModalMode('register')}
                  className="px-3 py-1.5 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-xs font-semibold flex items-center gap-1 cursor-pointer"
                >
                  <UserPlus className="w-3.5 h-3.5" /> 注册账号
                </button>
              </div>
            )}
          </div>
        </div>
      </header>

      {authModalMode && (
        <PortalAuthModal
          mode={authModalMode}
          currentUser={portalUser}
          onSwitchMode={setAuthModalMode}
          onClose={() => setAuthModalMode(null)}
          onAuthenticated={(u, msg) => {
            setCsrfToken(u.csrfToken);
            setPortalUser(u);
            setAuthModalMode(null);
            notify(msg);
          }}
        />
      )}

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

      <main className="max-w-7xl w-full mx-auto px-4 py-6 flex-1 space-y-6">
        {tab === 'lobby' && (
          <>
            <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
              <StatBox
                title="游戏服务器"
                value={overview?.maintenanceMode ? '维护中' : overview?.isRunning ? `${gameHost}:${gamePort}` : '离线'}
                sub={overview?.maintenanceReason || '支持 YGOPro / MDPro3 / KoishiPro 直连'}
              />
              <StatBox
                title="当前在线"
                value={`${overview?.clientCount ?? 0} 人`}
                sub={`进行中/待命房间 ${overview?.roomCount ?? 0} 个`}
              />
              <StatBox
                title="随机盲盒卡池"
                value={`${overview?.presetDeckCount ?? 0} 套`}
                sub="输入 RD# 或 CHAOS# 免组卡即玩"
              />
              <StatBox
                title="本服 DIY 扩展包"
                value={`${overview?.enabledDiyPackageCount ?? 0} 个`}
                sub={`卡库版本 v${overview?.catalogVersion ?? 1}`}
              />
            </div>

            {/* 可视化房间配置器与一键建房 / 快速复制主机密码 */}
            {showConfigurator ? (
              <RoomConfigurator
                apiEndpoint="/api/portal/rooms"
                theme="emerald"
                canCreateOnServer={Boolean(portalUser)}
                canIssueAiTokens={Boolean(portalUser?.allowedMcpRolesToIssue?.includes('player'))}
                banlists={overview?.banlists || []}
                onRequireAuth={() => setAuthModalMode('register')}
                onCreated={(res) => {
                  loadLobby();
                  if (res?.room?.roomName) openRoomDetail(res.room.roomName);
                }}
                onClose={() => setShowConfigurator(false)}
                notify={notify}
              />
            ) : (
              <div className="flex items-center justify-between bg-slate-900 border border-slate-800 rounded-xl px-4 py-3 text-xs">
                <span className="text-slate-300">想要自定义对局模式、盲盒随机或生成外部 AI 对战 Token？</span>
                <button
                  onClick={() => setShowConfigurator(true)}
                  className="px-3 py-1.5 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white font-semibold flex items-center gap-1 cursor-pointer"
                >
                  <Plus className="w-3.5 h-3.5" /> 展开创建房间配置器
                </button>
              </div>
            )}

            {overview?.ruleGuides && (
              <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-3">
                <div className="text-xs font-semibold text-emerald-300">常用客户端建房口令速查（一键复制后粘贴到游戏「主机密码」即可）</div>
                <div className="grid grid-cols-1 md:grid-cols-3 lg:grid-cols-4 gap-2.5">
                  {overview.ruleGuides.map((g: any) => (
                    <div key={g.prefix} className="bg-slate-950 border border-slate-800 rounded-lg p-2.5 text-xs space-y-1">
                      <div className="flex items-center justify-between">
                        <span className="font-mono font-bold text-emerald-300">{g.prefix}</span>
                        <SmallCopy text={g.prefix} onCopied={() => notify(`已复制口令模板：${g.prefix}`)} />
                      </div>
                      <div className="font-medium text-slate-200">{g.title}</div>
                      <div className="text-[11px] text-slate-400">{g.desc}</div>
                    </div>
                  ))}
                </div>
              </div>
            )}

            <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
              <div className="lg:col-span-2 space-y-3">
                <div className="flex items-center justify-between">
                  <h2 className="text-sm font-semibold flex items-center gap-2">
                    <Swords className="w-4 h-4 text-emerald-400" /> 实时公开房间大厅（支持一键复制主机密码进房对战或观战）
                  </h2>
                  <button onClick={loadLobby} className="px-2.5 py-1 rounded bg-slate-800 hover:bg-slate-700 text-xs cursor-pointer">
                    刷新
                  </button>
                </div>

                {rooms.length === 0 && (
                  <div className="bg-slate-900 border border-slate-800 rounded-xl p-8 text-center text-sm text-slate-400">
                    当前暂无活动房间，可在上方选择房间配置一键创建，或打开游戏客户端连入{' '}
                    <span className="font-mono text-emerald-300">{gameHost}:{gamePort}</span> 输入主机密码开房！
                  </div>
                )}

                <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                  {rooms.map((r) => {
                    const observers: any[] = r.observers || [];
                    const hostPw = r.hostPassword || r.roomName;
                    return (
                      <div
                        key={r.roomName}
                        onClick={() => openRoomDetail(r.roomName)}
                        className={`bg-slate-900 border rounded-xl p-4 space-y-2.5 cursor-pointer transition ${
                          selectedRoom?.summary?.roomName === r.roomName
                            ? 'border-emerald-500'
                            : 'border-slate-800 hover:border-emerald-500/50'
                        }`}
                      >
                        <div className="flex items-center justify-between gap-2">
                          <span className="font-bold text-sm truncate">{r.roomName}</span>
                          <div className="flex items-center gap-1.5 shrink-0">
                            {r.randomDeckMode && r.randomDeckMode !== 'None' && (
                              <span className="px-1.5 py-0.5 rounded text-[10px] bg-purple-500/20 text-purple-300">
                                {r.randomDeckMode}
                              </span>
                            )}
                            <span className="px-2 py-0.5 rounded text-[11px] bg-emerald-500/20 text-emerald-300">
                              {r.mode === 2 ? '2v2 Tag' : r.mode === 1 ? 'Match' : 'Single'} · {r.state}
                            </span>
                          </div>
                        </div>

                        <div className="flex items-center justify-between bg-slate-950 px-2.5 py-1.5 rounded border border-slate-800/80 text-xs">
                          <span className="text-slate-400 truncate">
                            主机密码: <code className="text-emerald-300 font-mono font-semibold">{hostPw}</code>
                          </span>
                          <QuickCopyPill
                            text={hostPw}
                            label="复制主机密码"
                            onCopied={() => notify(`已复制房间主机密码：${hostPw}`)}
                          />
                        </div>

                        <div className="grid grid-cols-2 gap-2 text-xs bg-slate-950 p-2 rounded">
                          <div>
                            A 队 LP: <span className="font-mono text-emerald-400 font-bold">{r.team0Lp}</span>
                          </div>
                          <div>
                            B 队 LP: <span className="font-mono text-sky-400 font-bold">{r.team1Lp}</span> (T{r.turnCount})
                          </div>
                        </div>

                        <div className="space-y-1 text-xs text-slate-300">
                          {r.slots.map((s: any) => (
                            <div key={s.seatIndex} className="flex justify-between">
                              <span>
                                #{s.seatIndex} {s.playerName || '<等待加入>'}{' '}
                                {s.isHost && <span className="text-amber-400 text-[10px]">[房主]</span>}
                              </span>
                              <span className="text-slate-500">{s.playerName ? (s.isReady ? '已准备' : '在席') : ''}</span>
                            </div>
                          ))}
                        </div>

                        {/* 房间卡片底部：观众列表预览与自建房管理 */}
                        <div className="pt-2 border-t border-slate-800/80 flex items-center justify-between gap-2 text-[11px] text-slate-400">
                          <div className="flex items-center gap-1 truncate">
                            <Eye className="w-3.5 h-3.5 text-sky-400 shrink-0" />
                            <span>观众 ({r.observerCount ?? observers.length})：</span>
                            <span className="text-slate-300 truncate">
                              {observers.length > 0 ? observers.map((o: any) => o.playerName).join('、') : '暂无观众'}
                            </span>
                          </div>
                          {portalUser && r.ownerUsername && portalUser.username.toLowerCase() === String(r.ownerUsername).toLowerCase() && (
                            <button
                              type="button"
                              onClick={(e) => {
                                e.stopPropagation();
                                closeOwnRoom(r.roomName);
                              }}
                              className="text-rose-400 hover:underline shrink-0 cursor-pointer"
                            >
                              关闭房间
                            </button>
                          )}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>

              <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-3">
                <h3 className="text-sm font-semibold flex items-center gap-2">
                  <Eye className="w-4 h-4 text-emerald-400" /> 房间详情 · 观众列表 · 实时实况
                </h3>
                {selectedRoom ? (
                  <div className="space-y-3 text-xs">
                    <div className="bg-slate-950 border border-slate-800 rounded-lg p-2.5 space-y-2">
                      <div className="flex items-center justify-between">
                        <span className="font-bold text-sm text-emerald-300">{selectedRoom.summary.roomName}</span>
                        <span className="text-[11px] text-slate-400">
                          MR{selectedRoom.summary.masterRule} · 禁限表: {selectedRoom.summary.banlistName || '无'}
                        </span>
                      </div>
                      <div className="flex items-center justify-between gap-2 flex-wrap">
                        <span className="text-slate-400">
                          主机密码: <code className="font-mono text-white">{selectedRoom.summary.hostPassword || selectedRoom.summary.roomName}</code>
                        </span>
                        <QuickCopyPill
                          text={selectedRoom.summary.hostPassword || selectedRoom.summary.roomName}
                          label="复制主机密码"
                          onCopied={() =>
                            notify(`已复制主机密码：${selectedRoom.summary.hostPassword || selectedRoom.summary.roomName}`)
                          }
                        />
                      </div>
                      {selectedRoom.summary.fullHostPassword &&
                        selectedRoom.summary.fullHostPassword !== selectedRoom.summary.roomName && (
                          <div className="flex items-center justify-between gap-2 flex-wrap pt-1 border-t border-slate-800/70">
                            <span className="text-slate-400 truncate">
                              规则口令: <code className="font-mono text-emerald-300">{selectedRoom.summary.fullHostPassword}</code>
                            </span>
                            <QuickCopyPill
                              text={selectedRoom.summary.fullHostPassword}
                              label="复制完整规则串"
                              onCopied={() => notify(`已复制规则口令：${selectedRoom.summary.fullHostPassword}`)}
                            />
                          </div>
                        )}
                    </div>

                    {/* 独立观众列表区块 */}
                    <div className="bg-slate-950 border border-slate-800 rounded-lg p-2.5 space-y-1.5">
                      <div className="font-semibold text-sky-300 flex items-center justify-between">
                        <span>当前观战观众列表 ({(selectedRoom.summary.observers || []).length} 人)</span>
                        <span className="text-[10px] text-slate-500">复制主机密码进房自动观战</span>
                      </div>
                      {(selectedRoom.summary.observers || []).length > 0 ? (
                        <div className="flex flex-wrap gap-1.5 pt-0.5">
                          {(selectedRoom.summary.observers || []).map((obs: any, idx: number) => (
                            <span
                              key={`${obs.playerName}-${idx}`}
                              className="inline-flex items-center gap-1 px-2 py-0.5 rounded bg-sky-500/15 border border-sky-500/30 text-sky-200"
                            >
                              <Eye className="w-3 h-3" />
                              {obs.playerName}
                            </span>
                          ))}
                        </div>
                      ) : (
                        <div className="text-slate-500 text-[11px]">当前暂无观战玩家</div>
                      )}
                    </div>

                    <div className="space-y-1">
                      <div className="font-semibold text-slate-300">公开对局事件与聊天</div>
                      <div className="h-56 overflow-y-auto bg-slate-950 border border-slate-800 rounded p-2.5 space-y-1">
                        {(selectedRoom.publicEvents || []).map((e: any, i: number) => (
                          <div key={`e-${i}`} className="text-emerald-300">
                            · {e.summary}
                          </div>
                        ))}
                        {(selectedRoom.chats || []).map((c: any, i: number) => (
                          <div key={`c-${i}`} className="text-slate-200">
                            <span className="text-amber-300">{c.senderName}:</span> {c.message}
                          </div>
                        ))}
                      </div>
                    </div>
                  </div>
                ) : (
                  <div className="text-xs text-slate-500 py-12 text-center">点击左侧任意房间查看观众列表、主机密码与实时战况</div>
                )}
              </div>
            </div>
          </>
        )}

        {tab === 'ai_tokens' && (
          <PortalAiTokenCenter
            user={portalUser}
            mcpPortHint={surfaceInfo.mcpPortHint}
            onRequireAuth={() => setAuthModalMode('register')}
            notify={notify}
          />
        )}

        {tab === 'replays' && (
          <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-3">
            <h2 className="text-sm font-semibold">全服历史对局战报与云录像（复制 R#编号 粘贴到游戏客户端「主机密码」即可直接看回放）</h2>
            <table className="w-full text-xs text-left">
              <thead className="text-slate-400 border-b border-slate-800">
                <tr>
                  <th className="py-2">回放主机密码</th>
                  <th>房间</th>
                  <th>模式</th>
                  <th>对阵玩家</th>
                  <th>回合</th>
                  <th>结束时间</th>
                  <th>下载</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/60">
                {replays.map((r) => (
                  <tr key={r.id}>
                    <td className="py-2">
                      <QuickCopyPill
                        text={r.replayCode}
                        label={`复制 ${r.replayCode}`}
                        onCopied={() => notify(`已复制回放主机密码：${r.replayCode}`)}
                      />
                    </td>
                    <td>{r.roomName}</td>
                    <td>{r.mode === 2 ? '2v2 Tag' : r.mode === 1 ? 'Match' : 'Single'}</td>
                    <td>{(r.players || []).map((p: any) => `${p.playerName}${p.isWinner ? '(胜)' : ''}`).join(' vs ')}</td>
                    <td>{r.turnCount}</td>
                    <td className="text-slate-400">{new Date(r.endedAt).toLocaleString()}</td>
                    <td>
                      {r.hasYrp && (
                        <a href={`/api/portal/replays/${r.id}.yrp`} download className="text-sky-400 hover:underline">
                          下载 .yrp
                        </a>
                      )}
                    </td>
                  </tr>
                ))}
                {replays.length === 0 && (
                  <tr>
                    <td colSpan={7} className="py-6 text-center text-slate-500">
                      暂无已归档的历史对局录像
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}

        {tab === 'catalog' && (
          <div className="space-y-6">
            <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-3">
              <h2 className="text-sm font-semibold flex items-center gap-2">
                <Download className="w-4 h-4 text-emerald-400" /> 本服启用 DIY 扩展包下载（下载 .ypk 放入客户端 expansions/ 文件夹即可显示卡图与文本）
              </h2>
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
                {diyPackages.map((pkg) => (
                  <div key={pkg.id} className="bg-slate-950 border border-slate-800 rounded-xl p-4 space-y-2 text-xs">
                    <div className="flex items-center justify-between">
                      <span className="font-bold text-sm text-slate-100">{pkg.name}</span>
                      <span className="font-mono text-[11px] text-slate-400">v{pkg.version}</span>
                    </div>
                    <div className="text-slate-400">
                      作者: {pkg.author || '服主'} · 收录卡片 {pkg.cardCount} 张
                    </div>
                    <a
                      href={`/api/portal/diy/packages/${encodeURIComponent(pkg.id)}/export.ypk`}
                      download
                      className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white font-semibold"
                    >
                      <Download className="w-3.5 h-3.5" /> 下载 {pkg.id}.ypk
                    </a>
                  </div>
                ))}
                {diyPackages.length === 0 && (
                  <div className="text-xs text-slate-500 py-4">当前暂无启用的自定义 DIY 扩展包</div>
                )}
              </div>
            </div>

            <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
              <div className="lg:col-span-2 bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-3">
                <form onSubmit={runSearch} className="flex flex-wrap gap-2">
                  <input
                    value={cardQuery}
                    onChange={(e) => setCardQuery(e.target.value)}
                    placeholder="输入卡密、卡名或效果关键字查卡..."
                    className="flex-1 px-3 py-1.5 rounded bg-slate-950 border border-slate-700 text-xs"
                  />
                  <label className="flex items-center gap-1.5 text-xs text-slate-300 px-2">
                    <input
                      type="checkbox"
                      checked={diyOnlySearch}
                      onChange={(e) => setDiyOnlySearch(e.target.checked)}
                    />
                    仅看 DIY 新卡
                  </label>
                  <button className="px-4 py-1.5 rounded bg-emerald-600 text-xs font-semibold flex items-center gap-1 cursor-pointer">
                    <Search className="w-3.5 h-3.5" /> 查卡
                  </button>
                </form>

                <div className="grid grid-cols-1 md:grid-cols-2 gap-2 max-h-96 overflow-y-auto">
                  {cards.map((c) => (
                    <div key={c.id} className="bg-slate-950 border border-slate-800 rounded-lg p-3 text-xs space-y-1">
                      <div className="flex justify-between font-semibold">
                        <span>
                          {c.name} <span className="font-mono text-slate-400">#{c.id}</span>
                        </span>
                        {c.isDiy && <span className="text-emerald-400">DIY</span>}
                      </div>
                      <div className="text-[11px] text-slate-400 line-clamp-3">{c.desc}</div>
                    </div>
                  ))}
                </div>
              </div>

              <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-3 text-xs">
                <div className="flex items-center justify-between">
                  <span className="font-semibold text-sm">混沌随机组卡娱乐试抽</span>
                  <button onClick={drawChaos} className="px-3 py-1 rounded bg-purple-600 text-white cursor-pointer">
                    试抽一套
                  </button>
                </div>
                {chaosDeck ? (
                  <div className="space-y-2 bg-slate-950 p-3 rounded border border-slate-800">
                    <div className="flex justify-between items-center">
                      <span className="text-purple-300 font-semibold">
                        主卡组 {chaosDeck.mainCount} + 额外 {chaosDeck.extraCount}
                      </span>
                      <SmallCopy text={chaosDeck.ydk} onCopied={() => notify('已复制 .ydk 卡组内容')} />
                    </div>
                    <div className="text-[11px] text-slate-400">
                      抽样：{chaosDeck.sampleMainCards.map((x: any) => x.name).join('、')}
                    </div>
                  </div>
                ) : (
                  <div className="text-slate-500 py-8 text-center">点击右上角试抽并一键复制 .ydk 构筑</div>
                )}
              </div>
            </div>
          </div>
        )}

        {tab === 'selfservice' && (
          <div className="grid grid-cols-1 lg:grid-cols-3 gap-4 text-xs">
            <form onSubmit={submitVpass} className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-3">
              <h3 className="text-sm font-semibold text-emerald-300">1. 游戏昵称防冒用保护 ($vpass)</h3>
              <p className="text-slate-400">首次绑定后，他人若不携带正确口令将无法使用你的昵称进入服务器。</p>
              <input
                value={vpassName}
                onChange={(e) => setVpassName(e.target.value)}
                placeholder="你的游戏昵称 (如 Kaiba)"
                className="w-full px-3 py-1.5 rounded bg-slate-950 border border-slate-700"
                required
              />
              <input
                type="password"
                value={vpassCode}
                onChange={(e) => setVpassCode(e.target.value)}
                placeholder="设置保护口令 (4~32位)"
                className="w-full px-3 py-1.5 rounded bg-slate-950 border border-slate-700"
                required
              />
              <button className="w-full py-2 rounded bg-emerald-600 hover:bg-emerald-500 font-semibold cursor-pointer">
                绑定 / 验证昵称口令
              </button>
              {vpassResult && (
                <div className="p-2.5 rounded bg-slate-950 border border-emerald-500/40 font-mono flex justify-between items-center">
                  <span>客户端昵称填：{vpassResult}</span>
                  <SmallCopy text={vpassResult} />
                </div>
              )}
            </form>

            <form onSubmit={submitTournamentDeck} className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-3">
              <h3 className="text-sm font-semibold text-sky-300">2. 赛事选手自助交牌 (.ydk)</h3>
              <div className="grid grid-cols-2 gap-2">
                <input
                  value={tTag}
                  onChange={(e) => setTTag(e.target.value)}
                  placeholder="赛事标识 (如 weekly)"
                  className="px-2.5 py-1.5 rounded bg-slate-950 border border-slate-700"
                  required
                />
                <input
                  value={tPlayer}
                  onChange={(e) => setTPlayer(e.target.value)}
                  placeholder="参赛游戏昵称"
                  className="px-2.5 py-1.5 rounded bg-slate-950 border border-slate-700"
                  required
                />
              </div>
              <textarea
                rows={6}
                value={tYdk}
                onChange={(e) => setTYdk(e.target.value)}
                placeholder="粘贴 .ydk 文件全部文本内容 (#main ...)"
                className="w-full p-2 rounded bg-slate-950 border border-slate-700 font-mono"
                required
              />
              <button className="w-full py-2 rounded bg-sky-600 hover:bg-sky-500 font-semibold cursor-pointer">
                提交比赛卡组并获取校验码
              </button>
              {tResultHash && (
                <div className="p-2 rounded bg-slate-950 border border-sky-500/40 font-mono text-[11px] break-all">
                  卡组 SHA-256 凭证：{tResultHash}
                </div>
              )}
            </form>

            <form onSubmit={submitDiyProposal} className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-2.5">
              <h3 className="text-sm font-semibold text-amber-300">3. 社区 DIY 新卡投稿（经服主审批生效）</h3>
              <div className="grid grid-cols-2 gap-2">
                <input
                  value={propAuthor}
                  onChange={(e) => setPropAuthor(e.target.value)}
                  placeholder={portalUser ? `署名 (默认 ${portalUser.username})` : '投稿人署名'}
                  className="px-2.5 py-1.5 rounded bg-slate-950 border border-slate-700"
                />
                <input
                  value={propPkg}
                  onChange={(e) => setPropPkg(e.target.value)}
                  placeholder="目标包 ID"
                  className="px-2.5 py-1.5 rounded bg-slate-950 border border-slate-700 font-mono"
                />
              </div>
              <div className="grid grid-cols-3 gap-2">
                <input
                  type="number"
                  value={propCardId}
                  onChange={(e) => setPropCardId(Number(e.target.value))}
                  placeholder="卡密"
                  className="px-2.5 py-1.5 rounded bg-slate-950 border border-slate-700 font-mono"
                  required
                />
                <input
                  value={propCardName}
                  onChange={(e) => setPropCardName(e.target.value)}
                  placeholder="新卡名称"
                  className="col-span-2 px-2.5 py-1.5 rounded bg-slate-950 border border-slate-700"
                  required
                />
              </div>
              <textarea
                rows={2}
                value={propCardDesc}
                onChange={(e) => setPropCardDesc(e.target.value)}
                placeholder="卡片效果文本..."
                className="w-full p-2 rounded bg-slate-950 border border-slate-700"
                required
              />
              <textarea
                rows={4}
                value={propLua}
                onChange={(e) => setPropLua(e.target.value)}
                spellCheck={false}
                className="w-full p-2 rounded bg-slate-950 border border-slate-700 font-mono text-[11px] text-emerald-200"
              />
              <button className="w-full py-2 rounded bg-amber-500 hover:bg-amber-400 text-slate-950 font-semibold flex items-center justify-center gap-1 cursor-pointer">
                <Send className="w-3.5 h-3.5" /> 投递至管理后台待审队列
              </button>
            </form>
          </div>
        )}
      </main>
    </div>
  );
}

function PortalAiTokenCenter({
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
  const [role, setRole] = useState('player');
  const [roomsText, setRoomsText] = useState('');
  const [expiresHours, setExpiresHours] = useState(72);
  const [newlyIssuedRaw, setNewlyIssuedRaw] = useState('');
  const [serverMcpPort, setServerMcpPort] = useState<number | undefined>(mcpPortHint);

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
      const res = await apiFetch('/api/portal/mcp/tokens', {
        method: 'POST',
        body: JSON.stringify({
          name: name.trim() || undefined,
          mcpRole: role,
          allowedRooms: rooms,
          maxSessions: 1,
          expiresInHours: Number(expiresHours)
        })
      });
      setNewlyIssuedRaw(res.rawToken);
      setName('');
      notify('AI Token 创建成功！请复制保存');
      loadTokens();
    } catch (err) {
      notify(err instanceof Error ? err.message : '创建 AI Token 失败', 'err');
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

        <form onSubmit={issueToken} className="grid grid-cols-1 md:grid-cols-5 gap-3 items-end">
          <div>
            <label className="block text-slate-400 mb-1">Token 备注名称（选填）</label>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="如 MyClaudePartner"
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
              <div key={t.id} className="bg-slate-950 p-3 rounded border border-slate-800 flex items-center justify-between">
                <div>
                  <div className="font-semibold">
                    {t.name} <span className="text-emerald-400">[{t.mcpRole}]</span>{' '}
                    {t.revokedAt && <span className="text-rose-400">(已吊销)</span>}
                  </div>
                  <div className="text-[11px] text-slate-400 font-mono">
                    前缀 {t.tokenPrefix}... · 房间限制: {(t.allowedRooms || []).join(', ') || '不限'}
                  </div>
                </div>
                {!t.revokedAt && (
                  <button
                    onClick={async () => {
                      await apiFetch(`/api/portal/mcp/tokens/${t.id}`, { method: 'DELETE' });
                      notify('Token 已吊销');
                      loadTokens();
                    }}
                    className="text-rose-400 hover:underline flex items-center gap-1 cursor-pointer"
                  >
                    <Trash2 className="w-3.5 h-3.5" /> 吊销
                  </button>
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

function PortalAuthModal({
  mode,
  currentUser,
  onSwitchMode,
  onClose,
  onAuthenticated
}: {
  mode: 'login' | 'register' | 'password';
  currentUser: PortalUser | null;
  onSwitchMode: (m: 'login' | 'register' | 'password') => void;
  onClose: () => void;
  onAuthenticated: (u: PortalUser, msg: string) => void;
}) {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPw, setConfirmPw] = useState('');
  const [currentPassword, setCurrentPassword] = useState('');
  const [newUsername, setNewUsername] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      if (mode === 'register') {
        if (password.length < 6) {
          setError('密码长度至少需要 6 位字符。');
          setLoading(false);
          return;
        }
        if (password !== confirmPw) {
          setError('两次输入的密码不一致。');
          setLoading(false);
          return;
        }
        const res = await apiFetch<{ user: PortalUser; message: string }>('/api/portal/auth/register', {
          method: 'POST',
          body: JSON.stringify({ username: username.trim(), password })
        });
        onAuthenticated(res.user, res.message || '注册并登录成功！');
      } else if (mode === 'login') {
        const res = await apiFetch<{ user: PortalUser; message: string }>('/api/portal/auth/login', {
          method: 'POST',
          body: JSON.stringify({ username: username.trim(), password })
        });
        onAuthenticated(res.user, res.message || '登录成功！');
      } else {
        if (password && password.length < 6) {
          setError('新密码长度至少需要 6 位字符。');
          setLoading(false);
          return;
        }
        const res = await apiFetch<{ user: PortalUser; message: string }>('/api/portal/auth/change-password', {
          method: 'POST',
          body: JSON.stringify({
            currentPassword,
            newUsername: newUsername.trim() || undefined,
            newPassword: password || undefined
          })
        });
        if (res.user) {
          onAuthenticated(res.user, res.message || '账号设置已更新');
        } else {
          onClose();
        }
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : '操作失败');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 bg-black/70 backdrop-blur-xs flex items-center justify-center p-4">
      <form onSubmit={submit} className="w-full max-w-md bg-slate-900 border border-slate-700 rounded-2xl p-6 shadow-2xl space-y-4 text-xs">
        <div className="flex items-center justify-between">
          <div className="font-bold text-sm flex items-center gap-2 text-emerald-300">
            <Lock className="w-4 h-4" />
            {mode === 'register' ? '注册新玩家账号' : mode === 'login' ? '玩家账号登录' : `修改账号设置 (${currentUser?.username})`}
          </div>
          <button type="button" onClick={onClose} className="text-slate-400 hover:text-white cursor-pointer">
            关闭
          </button>
        </div>

        {mode !== 'password' && (
          <div className="grid grid-cols-2 gap-1 bg-slate-950 p-1 rounded-lg border border-slate-800">
            <button
              type="button"
              onClick={() => {
                setError('');
                onSwitchMode('login');
              }}
              className={`py-1.5 rounded-md font-semibold cursor-pointer ${
                mode === 'login' ? 'bg-emerald-600 text-white' : 'text-slate-400'
              }`}
            >
              已有账号登录
            </button>
            <button
              type="button"
              onClick={() => {
                setError('');
                onSwitchMode('register');
              }}
              className={`py-1.5 rounded-md font-semibold cursor-pointer ${
                mode === 'register' ? 'bg-emerald-600 text-white' : 'text-slate-400'
              }`}
            >
              免费注册账号
            </button>
          </div>
        )}

        {error && <div className="p-2.5 rounded bg-rose-950/70 border border-rose-500/40 text-rose-200">{error}</div>}

        {mode === 'password' ? (
          <>
            <div>
              <label className="block text-slate-400 mb-1">当前原密码（必填校验）</label>
              <input
                type="password"
                value={currentPassword}
                onChange={(e) => setCurrentPassword(e.target.value)}
                className="w-full px-3 py-2 rounded-lg bg-slate-950 border border-slate-700"
                required
              />
            </div>
            <div>
              <label className="block text-slate-400 mb-1">自定义新用户名（选填，留空保持 {currentUser?.username}）</label>
              <input
                value={newUsername}
                onChange={(e) => setNewUsername(e.target.value)}
                placeholder={`留空保持 ${currentUser?.username}`}
                className="w-full px-3 py-2 rounded-lg bg-slate-950 border border-slate-700"
              />
            </div>
            <div>
              <label className="block text-slate-400 mb-1">自定义新密码（最少 6 位，留空保持原密码）</label>
              <input
                type="password"
                minLength={6}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="至少 6 位字符"
                className="w-full px-3 py-2 rounded-lg bg-slate-950 border border-slate-700"
              />
            </div>
          </>
        ) : (
          <>
            <div>
              <label className="block text-slate-400 mb-1">账号用户名（2~32 位字符）</label>
              <input
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                placeholder="如 Duelist_Yugi"
                minLength={2}
                maxLength={32}
                className="w-full px-3 py-2 rounded-lg bg-slate-950 border border-slate-700"
                required
              />
            </div>
            <div>
              <label className="block text-slate-400 mb-1">登录密码（最少 6 位字符）</label>
              <input
                type="password"
                minLength={6}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="至少 6 位字符"
                className="w-full px-3 py-2 rounded-lg bg-slate-950 border border-slate-700"
                required
              />
            </div>
            {mode === 'register' && (
              <div>
                <label className="block text-slate-400 mb-1">确认密码</label>
                <input
                  type="password"
                  minLength={6}
                  value={confirmPw}
                  onChange={(e) => setConfirmPw(e.target.value)}
                  placeholder="再次输入密码"
                  className="w-full px-3 py-2 rounded-lg bg-slate-950 border border-slate-700"
                  required
                />
              </div>
            )}
          </>
        )}

        <button
          disabled={loading}
          className="w-full py-2.5 rounded-lg bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 font-semibold text-sm text-white cursor-pointer"
        >
          {loading ? '正在提交...' : mode === 'register' ? '立即注册并登录' : mode === 'login' ? '安全登录' : '保存账号设置'}
        </button>
      </form>
    </div>
  );
}

function StatBox({ title, value, sub }: { title: string; value: string; sub: string }) {
  return (
    <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-1">
      <div className="text-xs text-slate-400">{title}</div>
      <div className="text-lg font-bold text-emerald-300 font-mono">{value}</div>
      <div className="text-[11px] text-slate-500">{sub}</div>
    </div>
  );
}

function SmallCopy({ text, onCopied }: { text: string; onCopied?: () => void }) {
  const [ok, setOk] = useState(false);
  return (
    <button
      type="button"
      onClick={(e) => {
        e.stopPropagation();
        navigator.clipboard?.writeText(text);
        setOk(true);
        onCopied?.();
        setTimeout(() => setOk(false), 1200);
      }}
      className="p-1 rounded hover:bg-slate-800 text-slate-400 hover:text-white cursor-pointer"
      title="复制"
    >
      {ok ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
    </button>
  );
}
