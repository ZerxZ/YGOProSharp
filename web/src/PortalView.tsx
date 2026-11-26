import React, { useEffect, useState, useCallback, useRef, useMemo } from 'react';
import {
  Gamepad2,
  Sparkles,
  Swords,
  Video,
  Download,
  ShieldCheck,
  Send,
  Search,
  Eye,
  KeyRound,
  UserPlus,
  LogOut,
  Plus,
  RefreshCw,
  Layers,
  Users,
  Shield,
  ChevronDown,
  ChevronUp,
  HelpCircle
} from 'lucide-react';
import { apiFetch, setPortalCsrfToken } from './api';
import { QuickCopyPill, RoomConfigurator } from './RoomConfigurator';
import {
  StatBox,
  SmallCopy,
  LpVersusBar,
  MobileBottomSheet,
  type PortalUser,
  type SurfaceInfo
} from './portal/portalTypes';
import { PortalAuthModal } from './portal/PortalAuthModal';
import { PortalAiTokenCenter } from './portal/PortalAiTokenCenter';
import { TagForceCommandPad } from './portal/TagForceCommandPad';
import { ReplayReviewPanel } from './portal/ReplayReviewPanel';
import { DeckSixDimRadarSection } from './portal/DeckSixDimRadarSection';
import { CodeMirrorEditor } from './components/CodeMirrorEditor';

export type { SurfaceInfo } from './portal/portalTypes';

type PortalTab = 'lobby' | 'ai_tokens' | 'replays' | 'catalog' | 'selfservice';
type RoomFilter = 'all' | 'lobby' | 'duel' | 'tag' | 'random';

function RoomDetailBody({
  room,
  portalUser,
  onRequireAuth,
  notify
}: {
  room: any;
  portalUser: PortalUser | null;
  onRequireAuth: () => void;
  notify: (t: string, k?: 'ok' | 'err') => void;
}) {
  const hostPw = room.summary.hostPassword || room.summary.roomName;
  const isTag = room.summary.mode === 2;

  return (
    <div className="space-y-3 text-xs">
      <div className="glass-subcard rounded-2xl p-3 space-y-2.5">
        <div className="flex items-center justify-between gap-2">
          <span className="font-bold text-sm text-emerald-300 truncate">{room.summary.roomName}</span>
          <span className="text-[11px] text-slate-400 shrink-0">
            MR{room.summary.masterRule} · {room.summary.banlistName || '无禁限'}
          </span>
        </div>

        <LpVersusBar
          team0Lp={room.summary.team0Lp}
          team1Lp={room.summary.team1Lp}
          startLp={room.summary.startLp}
          turnCount={room.summary.turnCount}
          isTag={isTag}
        />

        <div className="flex items-center justify-between gap-2 flex-wrap pt-0.5">
          <span className="text-slate-400 truncate">
            进房密码: <code className="font-mono text-white font-semibold">{hostPw}</code>
          </span>
          <QuickCopyPill text={hostPw} label="复制主机密码" onCopied={() => notify(`已复制主机密码：${hostPw}`)} />
        </div>

        {room.summary.fullHostPassword && room.summary.fullHostPassword !== room.summary.roomName && (
          <div className="flex items-center justify-between gap-2 flex-wrap pt-2 border-t border-slate-800/70">
            <span className="text-slate-400 truncate">
              完整规则串: <code className="font-mono text-emerald-300">{room.summary.fullHostPassword}</code>
            </span>
            <QuickCopyPill
              text={room.summary.fullHostPassword}
              label="复制规则串"
              onCopied={() => notify(`已复制规则口令：${room.summary.fullHostPassword}`)}
            />
          </div>
        )}
      </div>

      {isTag && (
        <TagForceCommandPad
          roomName={room.summary.roomName}
          slots={room.summary.slots || []}
          user={portalUser}
          onRequireAuth={onRequireAuth}
          notify={notify}
        />
      )}

      <div className="glass-subcard rounded-2xl p-3 space-y-1.5">
        <div className="font-semibold text-sky-300 flex items-center justify-between">
          <span>观战席 ({(room.summary.observers || []).length} 人)</span>
          <span className="text-[10px] text-slate-500">满座进房自动转观战</span>
        </div>
        {(room.summary.observers || []).length > 0 ? (
          <div className="flex flex-wrap gap-1.5 pt-0.5">
            {(room.summary.observers || []).map((obs: any, idx: number) => (
              <span
                key={`${obs.playerName}-${idx}`}
                className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-sky-500/15 border border-sky-500/30 text-sky-200"
              >
                <Eye className="w-3 h-3" />
                {obs.playerName}
              </span>
            ))}
          </div>
        ) : (
          <div className="text-slate-500 text-[11px]">当前暂无观战观众</div>
        )}
      </div>

      <div className="space-y-1.5">
        <div className="font-semibold text-slate-300 flex items-center justify-between">
          <span>实时公开战况与房间聊天</span>
          <span className="text-[10px] text-slate-500">战争迷雾保护</span>
        </div>
        <div className="h-60 overflow-y-auto glass-subcard rounded-2xl p-3 space-y-1.5">
          {(room.publicEvents || []).length === 0 && (room.chats || []).length === 0 && (
            <div className="text-slate-500 text-center py-10">等待玩家出牌或发言...</div>
          )}
          {(room.publicEvents || []).map((e: any, i: number) => (
            <div key={`e-${i}`} className="text-emerald-300/95 leading-relaxed">
              · {e.summary}
            </div>
          ))}
          {(room.chats || []).map((c: any, i: number) => {
            const st = Number(c.senderType ?? 16);
            const badge =
              st === 8
                ? { text: '系统', cls: 'bg-amber-500/20 text-amber-300 border-amber-500/30' }
                : st >= 0 && st < 4
                  ? isTag
                    ? st < 2
                      ? { text: 'A队', cls: 'bg-emerald-500/20 text-emerald-300 border-emerald-500/30' }
                      : { text: 'B队', cls: 'bg-sky-500/20 text-sky-300 border-sky-500/30' }
                    : st === 0
                      ? { text: '0席', cls: 'bg-emerald-500/20 text-emerald-300 border-emerald-500/30' }
                      : { text: '1席', cls: 'bg-sky-500/20 text-sky-300 border-sky-500/30' }
                  : { text: '观战', cls: 'bg-slate-800 text-slate-400 border-slate-700' };
            return (
              <div key={`c-${i}`} className="text-slate-200 flex items-baseline gap-1.5 flex-wrap leading-relaxed">
                <span className={`px-1.5 py-0.2 rounded border text-[10px] font-semibold ${badge.cls}`}>{badge.text}</span>
                <span className="text-amber-300 font-medium">{c.senderName}:</span>
                <span>{c.message}</span>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}

export function UserPortalApp({
  surfaceInfo,
  onNavigateAdmin
}: {
  surfaceInfo: SurfaceInfo;
  onNavigateAdmin?: () => void;
}) {
  const [tab, setTab] = useState<PortalTab>('lobby');
  const [portalUser, setPortalUser] = useState<PortalUser | null>(null);
  const [authModalMode, setAuthModalMode] = useState<'login' | 'register' | 'password' | null>(null);
  const [showConfigurator, setShowConfigurator] = useState<boolean>(() =>
    typeof window !== 'undefined' ? window.innerWidth >= 768 : true
  );
  const [showRuleGuides, setShowRuleGuides] = useState<boolean>(false);
  const [showConnectHelp, setShowConnectHelp] = useState<boolean>(false);
  const [roomFilter, setRoomFilter] = useState<RoomFilter>('all');
  const [roomKeyword, setRoomKeyword] = useState<string>('');
  const [mobileRoomSheetOpen, setMobileRoomSheetOpen] = useState<boolean>(false);

  const [overview, setOverview] = useState<any>(null);
  const [rooms, setRooms] = useState<any[]>([]);
  const [selectedRoom, setSelectedRoom] = useState<any>(null);
  const [selectedReplayDetail, setSelectedReplayDetail] = useState<any>(null);
  const [loadingReplayId, setLoadingReplayId] = useState<number | null>(null);
  const [replays, setReplays] = useState<any[]>([]);
  const [replayFilter, setReplayFilter] = useState<string>('');
  const [diyPackages, setDiyPackages] = useState<any[]>([]);
  const [cards, setCards] = useState<any[]>([]);
  const [cardQuery, setCardQuery] = useState('');
  const [diyOnlySearch, setDiyOnlySearch] = useState(false);
  const [chaosDeck, setChaosDeck] = useState<any>(null);
  const [toast, setToast] = useState<{ text: string; kind: 'ok' | 'err' } | null>(null);
  const wsRef = useRef<WebSocket | null>(null);
  const replayTheaterRef = useRef<HTMLDivElement | null>(null);
  const selectedRoomNameRef = useRef<string | null>(null);
  selectedRoomNameRef.current = selectedRoom?.summary?.roomName ?? null;

  const [vpassName, setVpassName] = useState('');
  const [vpassCode, setVpassCode] = useState('');
  const [vpassResult, setVpassResult] = useState('');

  const [tTag, setTTag] = useState('weekly');
  const [tPlayer, setTPlayer] = useState('');
  const [tYdk, setTYdk] = useState('');
  const [tResultHash, setTResultHash] = useState('');

  const [propMode, setPropMode] = useState<'single' | 'ypk'>('single');
  const [propUploadingYpk, setPropUploadingYpk] = useState(false);
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
    setTimeout(() => setToast((cur) => (cur?.text === text ? null : cur)), 3600);
  }, []);

  const refreshPortalAuth = useCallback(async () => {
    try {
      const me = await apiFetch<{ authenticated: boolean; user: PortalUser | null }>('/api/portal/auth/me');
      if (me.authenticated && me.user) {
        setPortalCsrfToken(me.user.csrfToken);
        setPortalUser(me.user);
      } else {
        setPortalCsrfToken('');
        setPortalUser(null);
      }
    } catch {
      setPortalCsrfToken('');
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
    async (roomName: string, openSheetOnMobile = false) => {
      try {
        const res = await apiFetch(`/api/portal/rooms/${encodeURIComponent(roomName)}`);
        setSelectedRoom(res.room);
        if (openSheetOnMobile) {
          setMobileRoomSheetOpen(true);
        }
      } catch (err) {
        notify(err instanceof Error ? err.message : '房间已结束', 'err');
      }
    },
    [notify]
  );

  const openReplayDetail = useCallback(
    async (duelId: number) => {
      setLoadingReplayId(duelId);
      try {
        const detail = await apiFetch(`/api/portal/replays/${duelId}/detail`);
        setSelectedReplayDetail(detail);
        setTimeout(() => replayTheaterRef.current?.scrollIntoView({ behavior: 'smooth', block: 'nearest' }), 80);
      } catch (err) {
        notify(err instanceof Error ? err.message : '读取赛后复盘详情失败', 'err');
      } finally {
        setLoadingReplayId(null);
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
      if (selectedRoomNameRef.current) {
        openRoomDetail(selectedRoomNameRef.current, false);
      }
    };
    wsRef.current = ws;
    return () => {
      wsRef.current?.close();
    };
  }, [loadLobby, openRoomDetail, refreshPortalAuth]);

  const handlePortalLogout = async () => {
    try {
      await apiFetch('/api/portal/auth/logout', { method: 'POST', body: '{}' });
    } catch {
      // ignore
    }
    setPortalCsrfToken('');
    setPortalUser(null);
    notify('已退出玩家账号');
  };

  const closeOwnRoom = async (roomName: string) => {
    try {
      await apiFetch(`/api/portal/rooms/${encodeURIComponent(roomName)}`, { method: 'DELETE' });
      notify(`已关闭房间 ${roomName}`);
      if (selectedRoom?.summary?.roomName === roomName) {
        setSelectedRoom(null);
        setMobileRoomSheetOpen(false);
      }
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
    if (!tYdk.trim()) {
      notify('请先粘贴或输入 .ydk 卡组文本内容', 'err');
      return;
    }
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

  const submitYpkProposal = async (files: FileList | null) => {
    if (!files || files.length === 0) return;
    setPropUploadingYpk(true);
    try {
      const fd = new FormData();
      fd.append('file', files[0]);
      if (propPkg.trim()) fd.append('packageId', propPkg.trim());
      if ((propAuthor || portalUser?.username)?.trim()) {
        fd.append('authorName', (propAuthor || portalUser?.username || '').trim());
      }
      const res = await apiFetch('/api/portal/diy/proposals/import-ypk', {
        method: 'POST',
        body: fd
      });
      notify(res.message || 'YPK 整包投稿成功！');
    } catch (err) {
      notify(err instanceof Error ? err.message : '上传 .ypk 投稿失败', 'err');
    } finally {
      setPropUploadingYpk(false);
    }
  };

  const filteredRooms = useMemo(() => {
    return rooms.filter((r) => {
      if (roomFilter === 'lobby' && r.state !== 'Lobby') return false;
      if (roomFilter === 'duel' && r.state === 'Lobby') return false;
      if (roomFilter === 'tag' && r.mode !== 2) return false;
      if (roomFilter === 'random' && (!r.randomDeckMode || r.randomDeckMode === 'None')) return false;
      if (roomKeyword.trim()) {
        const kw = roomKeyword.trim().toLowerCase();
        const inName = String(r.roomName || '').toLowerCase().includes(kw);
        const inSlot = (r.slots || []).some((s: any) => String(s.playerName || '').toLowerCase().includes(kw));
        if (!inName && !inSlot) return false;
      }
      return true;
    });
  }, [rooms, roomFilter, roomKeyword]);

  const filteredReplays = useMemo(() => {
    if (!replayFilter.trim()) return replays;
    const kw = replayFilter.trim().toLowerCase();
    return replays.filter(
      (r) =>
        String(r.replayCode || '').toLowerCase().includes(kw) ||
        String(r.roomName || '').toLowerCase().includes(kw) ||
        (r.players || []).some((p: any) => String(p.playerName || '').toLowerCase().includes(kw))
    );
  }, [replays, replayFilter]);

  const gameHost = overview?.publicGameHost || surfaceInfo.publicGameHost || window.location.hostname;
  const gamePort = overview?.gamePort || surfaceInfo.gamePort || 7911;
  const fullServerAddr = `${gameHost}:${gamePort}`;

  const navItems: { key: PortalTab; label: string; shortLabel: string; icon: React.ReactNode }[] = [
    { key: 'lobby', label: '实时大厅 & 建房', shortLabel: '大厅', icon: <Gamepad2 className="w-4 h-4" /> },
    { key: 'ai_tokens', label: '我的 AI 令牌', shortLabel: 'AI 令牌', icon: <KeyRound className="w-4 h-4" /> },
    { key: 'replays', label: '战报 & 复盘剧场', shortLabel: '战报', icon: <Video className="w-4 h-4" /> },
    { key: 'catalog', label: '卡查 & 六维雷达', shortLabel: '卡查', icon: <Sparkles className="w-4 h-4" /> },
    { key: 'selfservice', label: '玩家自助 & 投稿', shortLabel: '自助', icon: <ShieldCheck className="w-4 h-4" /> }
  ];

  return (
    <div className="min-h-screen bg-slate-950 portal-ambient-bg text-slate-100 flex flex-col">
      {/* 顶部毛玻璃导航栏 */}
      <header className="border-b border-white/[0.08] bg-slate-950/80 backdrop-blur-xl sticky top-0 z-30">
        <div className="max-w-7xl mx-auto px-3 sm:px-4 h-14 sm:h-16 flex items-center justify-between gap-2 sm:gap-4">
          <div className="flex items-center gap-2.5 min-w-0">
            <div className="w-9 h-9 rounded-xl bg-gradient-to-br from-emerald-500 to-teal-600 flex items-center justify-center font-black text-white shadow-lg shadow-emerald-950/60 shrink-0">
              Y
            </div>
            <div className="min-w-0">
              <div className="font-bold text-sm sm:text-base flex items-center gap-2">
                <span className="truncate">YGOProSharp</span>
                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-medium bg-emerald-500/15 text-emerald-300 border border-emerald-500/25 shrink-0">
                  <span
                    className={`w-1.5 h-1.5 rounded-full ${
                      overview?.maintenanceMode ? 'bg-amber-400' : 'bg-emerald-400 animate-pulse'
                    }`}
                  />
                  {overview?.maintenanceMode ? '维护中' : '在线'}
                </span>
              </div>
              <div className="text-[11px] text-slate-400 truncate hidden sm:block">
                玩家决斗大厅 · AI 搭档双打 · 赛后全视角复盘
              </div>
            </div>
          </div>

          {/* 桌面端胶囊导航 */}
          <nav className="hidden md:flex items-center gap-1 p-1 rounded-2xl bg-slate-900/90 border border-white/[0.06]">
            {navItems.map((item) => (
              <button
                key={item.key}
                onClick={() => setTab(item.key)}
                className={`px-3 py-1.5 rounded-xl text-xs font-semibold flex items-center gap-1.5 transition cursor-pointer ${
                  tab === item.key
                    ? 'bg-emerald-600 text-white shadow-md shadow-emerald-950/60'
                    : 'text-slate-300 hover:text-white hover:bg-slate-800/70'
                }`}
              >
                {item.icon}
                <span>{item.label}</span>
              </button>
            ))}
          </nav>

          {/* 右侧联机地址胶囊 + 账号操作 */}
          <div className="flex items-center gap-1.5 sm:gap-2 shrink-0">
            <button
              type="button"
              onClick={() => {
                navigator.clipboard?.writeText(fullServerAddr);
                notify(`已复制游戏联机地址：${fullServerAddr}`);
              }}
              className="inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-xl bg-slate-900/90 hover:bg-slate-800 border border-slate-800 text-[11px] font-mono text-emerald-300 cursor-pointer"
              title="点击复制游戏服务器地址"
            >
              <span className="hidden lg:inline font-sans text-slate-400">联机:</span>
              <span>{fullServerAddr}</span>
            </button>

            <button
              type="button"
              onClick={() => setShowConnectHelp((v) => !v)}
              className="p-2 rounded-xl bg-slate-900/80 hover:bg-slate-800 text-slate-400 hover:text-emerald-300 border border-slate-800 cursor-pointer"
              title="如何用客户端联机进房？"
            >
              <HelpCircle className="w-4 h-4" />
            </button>

            {portalUser ? (
              <div className="flex items-center gap-1">
                <button
                  onClick={() => setAuthModalMode('password')}
                  className="px-2.5 py-1.5 rounded-xl bg-emerald-500/15 hover:bg-emerald-500/25 border border-emerald-500/30 text-xs font-semibold text-emerald-200 max-w-28 truncate cursor-pointer"
                  title="修改个人账号或密码"
                >
                  {portalUser.displayName}
                </button>
                <button
                  onClick={handlePortalLogout}
                  className="p-2 rounded-xl text-slate-400 hover:text-rose-400 hover:bg-slate-800 cursor-pointer"
                  title="退出登录"
                >
                  <LogOut className="w-4 h-4" />
                </button>
              </div>
            ) : (
              <div className="flex items-center gap-1.5">
                <button
                  onClick={() => setAuthModalMode('login')}
                  className="px-2.5 sm:px-3 py-1.5 rounded-xl bg-slate-800/90 hover:bg-slate-700 text-xs font-medium cursor-pointer"
                >
                  登录
                </button>
                <button
                  onClick={() => setAuthModalMode('register')}
                  className="px-2.5 sm:px-3 py-1.5 rounded-xl bg-gradient-to-r from-emerald-600 to-teal-500 hover:from-emerald-500 hover:to-teal-400 text-xs font-semibold text-white flex items-center gap-1 shadow-md shadow-emerald-950/50 cursor-pointer"
                >
                  <UserPlus className="w-3.5 h-3.5" />
                  <span className="hidden sm:inline">注册</span>
                </button>
              </div>
            )}

            {onNavigateAdmin && surfaceInfo.unifiedPortal !== false && (
              <button
                type="button"
                onClick={onNavigateAdmin}
                className="inline-flex p-2 rounded-xl bg-slate-900/80 hover:bg-indigo-950/60 border border-slate-800 text-slate-400 hover:text-indigo-300 cursor-pointer"
                title="进入管理后台 (/admin)"
              >
                <Shield className="w-4 h-4" />
              </button>
            )}
          </div>
        </div>

        {/* 联机新手三步提示条 */}
        {showConnectHelp && (
          <div className="border-t border-emerald-500/25 bg-emerald-950/40 px-4 py-2.5 text-xs animate-fade-in">
            <div className="max-w-7xl mx-auto flex flex-col sm:flex-row sm:items-center justify-between gap-2 text-slate-200">
              <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
                <span className="font-semibold text-emerald-300">客户端三步联机：</span>
                <span>① 打开 MDPro3 / YGOPro「联机模式」</span>
                <span>
                  ② 主机地址填 <code className="text-emerald-300 font-mono">{gameHost}</code>，端口填{' '}
                  <code className="text-emerald-300 font-mono">{gamePort}</code>
                </span>
                <span>③ 主机密码粘贴任意房间口令（输入 R#编号 可直接看云录像）</span>
              </div>
              <button
                type="button"
                onClick={() => setShowConnectHelp(false)}
                className="text-emerald-300 hover:underline self-end sm:self-auto shrink-0 cursor-pointer"
              >
                知道了
              </button>
            </div>
          </div>
        )}
      </header>

      {authModalMode && (
        <PortalAuthModal
          mode={authModalMode}
          currentUser={portalUser}
          onSwitchMode={setAuthModalMode}
          onClose={() => setAuthModalMode(null)}
          onAuthenticated={(u, msg) => {
            setPortalCsrfToken(u.csrfToken);
            setPortalUser(u);
            setAuthModalMode(null);
            notify(msg);
          }}
        />
      )}

      {/* 悬浮通知（手机端自动避让底部 Tab 栏） */}
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

      {/* 手机端房间详情底部抽屉 */}
      <MobileBottomSheet
        open={mobileRoomSheetOpen && Boolean(selectedRoom)}
        title={selectedRoom ? `房间实况 · ${selectedRoom.summary.roomName}` : '房间详情'}
        subtitle={
          selectedRoom
            ? `${selectedRoom.summary.mode === 2 ? '2v2 双打' : selectedRoom.summary.mode === 1 ? 'Match BO3' : '1v1 单局'} · ${selectedRoom.summary.state}`
            : undefined
        }
        onClose={() => setMobileRoomSheetOpen(false)}
      >
        {selectedRoom && (
          <RoomDetailBody
            room={selectedRoom}
            portalUser={portalUser}
            onRequireAuth={() => {
              setMobileRoomSheetOpen(false);
              setAuthModalMode('login');
            }}
            notify={notify}
          />
        )}
      </MobileBottomSheet>

      <main className="max-w-7xl w-full mx-auto px-3 sm:px-4 pt-4 sm:pt-6 pb-mobile-nav flex-1 space-y-5">
        {tab === 'lobby' && (
          <>
            {/* 顶部四宫格状态卡片 */}
            <div className="grid grid-cols-2 lg:grid-cols-4 gap-2.5 sm:gap-4">
              <StatBox
                title="游戏联机地址"
                value={overview?.maintenanceMode ? '维护拦截中' : fullServerAddr}
                sub={overview?.maintenanceReason || '兼容 MDPro3 / YGOPro 直连'}
                icon={<Gamepad2 className="w-4 h-4" />}
                accent={overview?.maintenanceMode ? 'amber' : 'emerald'}
              />
              <StatBox
                title="实时在线玩家"
                value={`${overview?.clientCount ?? 0} 人`}
                sub={`活动房间 ${overview?.roomCount ?? 0} 个`}
                icon={<Users className="w-4 h-4" />}
                accent="sky"
              />
              <StatBox
                title="随机盲盒主题池"
                value={`${overview?.presetDeckCount ?? 0} 套`}
                sub="RD# 盲盒 · CHAOS# 混沌即玩"
                icon={<Sparkles className="w-4 h-4" />}
                accent="purple"
              />
              <StatBox
                title="DIY 扩展卡库"
                value={`${overview?.enabledDiyPackageCount ?? 0} 包启用`}
                sub={`Catalog v${overview?.catalogVersion ?? 1}`}
                icon={<Layers className="w-4 h-4" />}
                accent="indigo"
              />
            </div>

            {/* 建房配置面板（手机端默认折叠为醒目横幅，保证首屏直接看到房间列表） */}
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
                  if (res?.room?.roomName) openRoomDetail(res.room.roomName, false);
                }}
                onClose={() => setShowConfigurator(false)}
                notify={notify}
              />
            ) : (
              <div className="glass-card border border-emerald-500/30 rounded-2xl p-3.5 sm:px-5 sm:py-4 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                <div className="space-y-0.5">
                  <div className="font-bold text-sm text-emerald-300 flex items-center gap-1.5">
                    <Sparkles className="w-4 h-4 text-amber-400" /> 想自己开一桌盲盒娱乐、2v2 双打或 AI 斗蛐蛐？
                  </div>
                  <div className="text-[11px] text-slate-400">
                    支持一键选择玩法模板、自动生成进房口令，并顺带签发本房专属外部 AI 对战令牌
                  </div>
                </div>
                <button
                  onClick={() => setShowConfigurator(true)}
                  className="w-full sm:w-auto min-h-[40px] px-4 py-2 rounded-xl bg-gradient-to-r from-emerald-600 to-teal-500 hover:from-emerald-500 hover:to-teal-400 text-white text-xs font-semibold flex items-center justify-center gap-1.5 shadow-lg shadow-emerald-950/50 shrink-0 cursor-pointer"
                >
                  <Plus className="w-4 h-4" /> 创建房间 / 生成口令
                </button>
              </div>
            )}

            {/* 可折叠客户端口令速查 */}
            {overview?.ruleGuides && (
              <div className="glass-card rounded-2xl px-4 py-3 space-y-3">
                <button
                  type="button"
                  onClick={() => setShowRuleGuides((v) => !v)}
                  className="w-full flex items-center justify-between text-xs font-semibold text-slate-200 cursor-pointer"
                >
                  <span className="flex items-center gap-2">
                    <span className="px-2 py-0.5 rounded-md bg-emerald-500/15 text-emerald-300 text-[11px]">速查</span>
                    <span>客户端建房规则前缀口令表（免网页登录，直接粘贴到游戏主机密码框即可生效）</span>
                  </span>
                  {showRuleGuides ? (
                    <ChevronUp className="w-4 h-4 text-slate-400 shrink-0" />
                  ) : (
                    <ChevronDown className="w-4 h-4 text-slate-400 shrink-0" />
                  )}
                </button>

                {showRuleGuides && (
                  <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-2.5 pt-1 animate-fade-in">
                    {overview.ruleGuides.map((g: any) => (
                      <div key={g.prefix} className="glass-subcard rounded-xl p-3 text-xs space-y-1">
                        <div className="flex items-center justify-between">
                          <span className="font-mono font-bold text-emerald-300">{g.prefix}</span>
                          <SmallCopy text={g.prefix} onCopied={() => notify(`已复制口令模板：${g.prefix}`)} />
                        </div>
                        <div className="font-semibold text-slate-200">{g.title}</div>
                        <div className="text-[11px] text-slate-400 leading-relaxed">{g.desc}</div>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            )}

            {/* 房间列表 + 桌面端右侧常驻实况栏 */}
            <div className="grid grid-cols-1 lg:grid-cols-12 gap-4 items-start">
              <div className="lg:col-span-8 space-y-3">
                {/* 筛选与搜索工具条 */}
                <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2.5">
                  <div className="flex items-center gap-1.5 overflow-x-auto no-scrollbar pb-0.5">
                    {(
                      [
                        ['all', `全部 (${rooms.length})`],
                        ['lobby', '等待加入'],
                        ['duel', '决斗中'],
                        ['tag', '2v2 双打'],
                        ['random', '随机盲盒']
                      ] as [RoomFilter, string][]
                    ).map(([key, label]) => (
                      <button
                        key={key}
                        type="button"
                        onClick={() => setRoomFilter(key)}
                        className={`px-3 py-1.5 rounded-full text-xs font-medium shrink-0 transition cursor-pointer ${
                          roomFilter === key
                            ? 'bg-emerald-600 text-white shadow-sm'
                            : 'glass-card text-slate-300 hover:text-white'
                        }`}
                      >
                        {label}
                      </button>
                    ))}
                  </div>

                  <div className="flex items-center gap-2">
                    <div className="relative flex-1 sm:w-44">
                      <Search className="w-3.5 h-3.5 text-slate-500 absolute left-3 top-1/2 -translate-y-1/2" />
                      <input
                        value={roomKeyword}
                        onChange={(e) => setRoomKeyword(e.target.value)}
                        placeholder="搜房名或玩家..."
                        className="w-full pl-8 pr-3 py-1.5 text-xs input-modern"
                      />
                    </div>
                    <button
                      onClick={loadLobby}
                      className="p-2 rounded-xl glass-card hover:bg-slate-800 text-slate-300 cursor-pointer"
                      title="刷新房间列表"
                    >
                      <RefreshCw className="w-4 h-4" />
                    </button>
                  </div>
                </div>

                {filteredRooms.length === 0 ? (
                  <div className="glass-card rounded-2xl p-10 text-center space-y-2">
                    <Swords className="w-8 h-8 text-slate-600 mx-auto" />
                    <div className="text-sm font-semibold text-slate-300">
                      {rooms.length === 0 ? '当前暂无活动房间' : '没有符合筛选条件的房间'}
                    </div>
                    <div className="text-xs text-slate-400">
                      可在上方一键创建房间，或在客户端连入{' '}
                      <code className="font-mono text-emerald-300">{fullServerAddr}</code> 输入口令直接开房！
                    </div>
                  </div>
                ) : (
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                    {filteredRooms.map((r) => {
                      const observers: any[] = r.observers || [];
                      const hostPw = r.hostPassword || r.roomName;
                      const isSelected = selectedRoom?.summary?.roomName === r.roomName;
                      const isTag = r.mode === 2;
                      return (
                        <div
                          key={r.roomName}
                          onClick={() => openRoomDetail(r.roomName, true)}
                          className={`glass-card rounded-2xl p-4 space-y-3 cursor-pointer transition active:scale-[0.995] ${
                            isSelected
                              ? 'border-emerald-500/70 ring-1 ring-emerald-500/30'
                              : 'hover:border-emerald-500/40'
                          }`}
                        >
                          <div className="flex items-center justify-between gap-2">
                            <div className="flex items-center gap-2 min-w-0">
                              <span
                                className={`w-2 h-2 rounded-full shrink-0 ${
                                  r.state === 'Lobby' ? 'bg-amber-400' : 'bg-emerald-400 animate-pulse'
                                }`}
                              />
                              <span className="font-bold text-sm text-slate-100 truncate">{r.roomName}</span>
                            </div>
                            <div className="flex items-center gap-1.5 shrink-0">
                              {r.randomDeckMode && r.randomDeckMode !== 'None' && (
                                <span className="px-2 py-0.5 rounded-full text-[10px] font-medium bg-purple-500/20 border border-purple-500/30 text-purple-200">
                                  {r.randomDeckMode}
                                </span>
                              )}
                              <span className="px-2 py-0.5 rounded-full text-[10px] font-semibold bg-emerald-500/15 border border-emerald-500/30 text-emerald-300">
                                {isTag ? '2v2 双打' : r.mode === 1 ? 'Match' : '1v1'} ·{' '}
                                {r.state === 'Lobby' ? '待开局' : '决斗中'}
                              </span>
                            </div>
                          </div>

                          {/* 直观双阵营 LP 血条 */}
                          <LpVersusBar
                            team0Lp={r.team0Lp}
                            team1Lp={r.team1Lp}
                            startLp={r.startLp}
                            turnCount={r.turnCount}
                            isTag={isTag}
                          />

                          {/* 一键复制进房口令条 */}
                          <div className="flex items-center justify-between gap-2 glass-subcard px-3 py-1.5 rounded-xl text-xs">
                            <span className="text-slate-400 truncate">
                              口令: <code className="text-emerald-300 font-mono font-bold">{hostPw}</code>
                            </span>
                            <QuickCopyPill
                              text={hostPw}
                              label="复制进房"
                              onCopied={() => notify(`已复制房间主机密码：${hostPw}`)}
                            />
                          </div>

                          {/* 选手席位网格 */}
                          <div className="grid grid-cols-2 gap-1.5 text-xs">
                            {r.slots.map((s: any) => (
                              <div
                                key={s.seatIndex}
                                className={`px-2.5 py-1.5 rounded-lg border flex items-center justify-between gap-1 ${
                                  s.playerName
                                    ? 'bg-slate-900/90 border-slate-800 text-slate-200'
                                    : 'bg-slate-950/40 border-dashed border-slate-800/80 text-slate-500'
                                }`}
                              >
                                <span className="truncate">
                                  <span className="text-slate-500 font-mono mr-1">#{s.seatIndex}</span>
                                  {s.playerName || '空席待入'}
                                </span>
                                {s.isHost ? (
                                  <span className="text-amber-400 text-[10px] shrink-0">房主</span>
                                ) : s.playerName && r.state === 'Lobby' ? (
                                  <span className={`text-[10px] shrink-0 ${s.isReady ? 'text-emerald-400' : 'text-slate-500'}`}>
                                    {s.isReady ? '就绪' : '准备中'}
                                  </span>
                                ) : null}
                              </div>
                            ))}
                          </div>

                          <div className="pt-2 border-t border-slate-800/70 flex items-center justify-between gap-2 text-[11px] text-slate-400">
                            <div className="flex items-center gap-1 truncate">
                              <Eye className="w-3.5 h-3.5 text-sky-400 shrink-0" />
                              <span>观众 {r.observerCount ?? observers.length}</span>
                              {observers.length > 0 && (
                                <span className="text-slate-300 truncate">
                                  · {observers.map((o: any) => o.playerName).join('、')}
                                </span>
                              )}
                            </div>
                            <div className="flex items-center gap-2 shrink-0">
                              <span className="lg:hidden text-emerald-400 font-medium">点开详情 →</span>
                              {portalUser &&
                                r.ownerUsername &&
                                portalUser.username.toLowerCase() === String(r.ownerUsername).toLowerCase() && (
                                  <button
                                    type="button"
                                    onClick={(e) => {
                                      e.stopPropagation();
                                      closeOwnRoom(r.roomName);
                                    }}
                                    className="text-rose-400 hover:underline cursor-pointer"
                                  >
                                    关房
                                  </button>
                                )}
                            </div>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                )}
              </div>

              {/* 桌面端右侧吸顶房间实况栏（手机端改为点击弹 BottomSheet，不占用流式高度） */}
              <div className="hidden lg:block lg:col-span-4 lg:sticky lg:top-20 glass-card rounded-2xl p-4 space-y-3">
                <h3 className="text-sm font-bold flex items-center gap-2">
                  <Eye className="w-4 h-4 text-emerald-400" /> 房间实况 · 2v2 指挥 · 观众席
                </h3>
                {selectedRoom ? (
                  <RoomDetailBody
                    room={selectedRoom}
                    portalUser={portalUser}
                    onRequireAuth={() => setAuthModalMode('login')}
                    notify={notify}
                  />
                ) : (
                  <div className="text-xs text-slate-500 py-14 text-center space-y-2">
                    <Eye className="w-7 h-7 text-slate-700 mx-auto" />
                    <div>点击左侧任意房间卡片</div>
                    <div className="text-[11px]">查看实时公开事件、聊天与 2v2 搭档战术盘</div>
                  </div>
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
          <div className="space-y-4 animate-fade-in">
            <div ref={replayTheaterRef}>
              {selectedReplayDetail && (
                <ReplayReviewPanel
                  detail={selectedReplayDetail}
                  onClose={() => setSelectedReplayDetail(null)}
                  notify={notify}
                />
              )}
            </div>

            <div className="glass-card rounded-2xl p-4 sm:p-5 space-y-4">
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                <div>
                  <h2 className="text-sm sm:text-base font-bold flex items-center gap-2">
                    <Video className="w-4 h-4 text-emerald-400" /> 全服历史对局战报 & AI 赛后复盘剧场
                  </h2>
                  <p className="text-[11px] text-slate-400 mt-0.5">
                    游戏客户端输入 <code className="text-emerald-300 font-mono">R#编号</code> 可直接观看云回放；点击「AI 复盘详情」查看 0~100 发挥评分与时间轴
                  </p>
                </div>
                <div className="relative sm:w-56">
                  <Search className="w-3.5 h-3.5 text-slate-500 absolute left-3 top-1/2 -translate-y-1/2" />
                  <input
                    value={replayFilter}
                    onChange={(e) => setReplayFilter(e.target.value)}
                    placeholder="按选手名或 R#编号 筛选..."
                    className="w-full pl-8 pr-3 py-2 text-xs input-modern"
                  />
                </div>
              </div>

              {/* 手机端自适应卡片列表 (md:hidden) */}
              <div className="md:hidden space-y-2.5">
                {filteredReplays.map((r) => (
                  <div
                    key={r.id}
                    onClick={() => openReplayDetail(r.id)}
                    className={`glass-subcard rounded-2xl p-3.5 space-y-2.5 text-xs active:scale-[0.99] cursor-pointer ${
                      selectedReplayDetail?.duelId === r.id ? 'border-emerald-500/60' : ''
                    }`}
                  >
                    <div className="flex items-center justify-between gap-2">
                      <div className="flex items-center gap-2">
                        <span className="font-mono font-bold text-emerald-300 text-sm">{r.replayCode}</span>
                        <span className="px-2 py-0.5 rounded-full bg-slate-800 text-[10px] text-slate-300">
                          {r.mode === 2 ? '2v2 双打' : r.mode === 1 ? 'Match' : '1v1'} · {r.turnCount} 回合
                        </span>
                      </div>
                      <span className="text-[11px] text-slate-500">{new Date(r.endedAt).toLocaleDateString()}</span>
                    </div>

                    <div className="text-slate-200 font-medium leading-relaxed">
                      {(r.players || []).map((p: any, idx: number) => (
                        <span key={idx}>
                          {idx > 0 && <span className="text-slate-500 mx-1.5">vs</span>}
                          <span className={p.isWinner ? 'text-emerald-300 font-bold' : 'text-slate-300'}>
                            {p.playerName}
                            {p.isWinner ? ' (胜)' : ''}
                          </span>
                        </span>
                      ))}
                    </div>

                    <div className="flex items-center gap-2 pt-1" onClick={(e) => e.stopPropagation()}>
                      <button
                        type="button"
                        onClick={() => openReplayDetail(r.id)}
                        className="flex-1 py-2 rounded-xl bg-emerald-600/25 border border-emerald-500/40 text-emerald-200 font-semibold text-center cursor-pointer"
                      >
                        {loadingReplayId === r.id ? '加载中...' : '打开 AI 复盘剧场'}
                      </button>
                      <QuickCopyPill
                        text={r.replayCode}
                        label="复制口令"
                        onCopied={() => notify(`已复制回放口令：${r.replayCode}`)}
                      />
                      {r.hasYrp && (
                        <a
                          href={`/api/portal/replays/${r.id}.yrp`}
                          download
                          className="px-3 py-2 rounded-xl bg-slate-800 text-sky-300 font-medium"
                        >
                          .yrp
                        </a>
                      )}
                    </div>
                  </div>
                ))}
                {filteredReplays.length === 0 && (
                  <div className="py-10 text-center text-xs text-slate-500">暂无匹配的历史战报记录</div>
                )}
              </div>

              {/* 桌面端精致数据表格 (hidden md:block) */}
              <div className="hidden md:block overflow-x-auto">
                <table className="w-full text-xs text-left">
                  <thead className="text-slate-400 border-b border-slate-800">
                    <tr>
                      <th className="py-2.5">回放口令</th>
                      <th>房间</th>
                      <th>模式</th>
                      <th>对阵选手</th>
                      <th>回合</th>
                      <th>结束时间</th>
                      <th className="text-right">复盘 & 录像</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-800/60">
                    {filteredReplays.map((r) => (
                      <tr
                        key={r.id}
                        onClick={() => openReplayDetail(r.id)}
                        className={`cursor-pointer transition hover:bg-slate-800/40 ${
                          selectedReplayDetail?.duelId === r.id ? 'bg-emerald-950/30' : ''
                        }`}
                      >
                        <td className="py-3" onClick={(e) => e.stopPropagation()}>
                          <QuickCopyPill
                            text={r.replayCode}
                            label={r.replayCode}
                            onCopied={() => notify(`已复制回放主机密码：${r.replayCode}`)}
                          />
                        </td>
                        <td className="font-semibold text-slate-200">{r.roomName}</td>
                        <td>
                          <span className="px-2 py-0.5 rounded-full bg-slate-800 text-[11px]">
                            {r.mode === 2 ? '2v2 Tag' : r.mode === 1 ? 'Match' : 'Single'}
                          </span>
                        </td>
                        <td>
                          {(r.players || []).map((p: any, idx: number) => (
                            <span key={idx}>
                              {idx > 0 && <span className="text-slate-500 mx-1">vs</span>}
                              <span className={p.isWinner ? 'text-emerald-300 font-semibold' : 'text-slate-300'}>
                                {p.playerName}
                                {p.isWinner ? '(胜)' : ''}
                              </span>
                            </span>
                          ))}
                        </td>
                        <td className="font-mono">{r.turnCount}</td>
                        <td className="text-slate-400">{new Date(r.endedAt).toLocaleString()}</td>
                        <td className="text-right space-x-2" onClick={(e) => e.stopPropagation()}>
                          <button
                            type="button"
                            onClick={() => openReplayDetail(r.id)}
                            className="px-3 py-1 rounded-lg bg-emerald-600/20 hover:bg-emerald-600/35 border border-emerald-500/40 text-emerald-200 font-medium cursor-pointer"
                          >
                            {loadingReplayId === r.id ? '加载中...' : 'AI 复盘详情'}
                          </button>
                          {r.hasYrp && (
                            <a href={`/api/portal/replays/${r.id}.yrp`} download className="text-sky-400 hover:underline">
                              下载 .yrp
                            </a>
                          )}
                        </td>
                      </tr>
                    ))}
                    {filteredReplays.length === 0 && (
                      <tr>
                        <td colSpan={7} className="py-10 text-center text-slate-500">
                          暂无已归档的历史对局录像
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        )}

        {tab === 'catalog' && (
          <div className="space-y-5 animate-fade-in">
            <DeckSixDimRadarSection chaosYdk={chaosDeck?.ydk} notify={notify} />

            <div className="glass-card rounded-2xl p-4 sm:p-5 space-y-3.5">
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2.5">
                <div>
                  <h2 className="text-sm sm:text-base font-bold flex items-center gap-2">
                    <Download className="w-4 h-4 text-emerald-400" /> 本服 DIY 扩展包 (.ypk) 导出下载
                  </h2>
                  <p className="text-[11px] text-slate-400 mt-0.5">
                    下载 <code className="text-emerald-300 font-mono">.ypk</code> 文件放入客户端{' '}
                    <code className="text-slate-300 font-mono">expansions/</code> 目录即可完整显示 DIY 卡图、字段与效果文本
                  </p>
                </div>
                {diyPackages.length > 0 && (
                  <a
                    href="/api/portal/diy/export-all.ypk"
                    download
                    className="min-h-[38px] inline-flex items-center justify-center gap-1.5 px-4 py-2 rounded-xl bg-gradient-to-r from-emerald-600 to-teal-500 hover:from-emerald-500 hover:to-teal-400 text-white text-xs font-semibold shadow-lg shadow-emerald-950/50 shrink-0"
                  >
                    <Download className="w-3.5 h-3.5" /> 一键导出全服整合包 (.ypk)
                  </a>
                )}
              </div>
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
                {diyPackages.map((pkg) => (
                  <div key={pkg.id} className="glass-subcard rounded-2xl p-4 space-y-2.5 text-xs">
                    <div className="flex items-center justify-between">
                      <span className="font-bold text-sm text-slate-100">{pkg.name}</span>
                      <span className="px-2 py-0.5 rounded-full bg-slate-800 font-mono text-[10px] text-slate-300">
                        v{pkg.version}
                      </span>
                    </div>
                    <div className="text-slate-400">
                      作者: {pkg.author || '服主'} · 收录卡片 <b className="text-emerald-300">{pkg.cardCount}</b> 张
                    </div>
                    <a
                      href={`/api/portal/diy/packages/${encodeURIComponent(pkg.id)}/export.ypk`}
                      download
                      className="w-full sm:w-auto inline-flex items-center justify-center gap-1.5 px-3.5 py-2 rounded-xl bg-emerald-600 hover:bg-emerald-500 text-white font-semibold"
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
              <div className="lg:col-span-2 glass-card rounded-2xl p-4 sm:p-5 space-y-3.5">
                <form onSubmit={runSearch} className="flex flex-col sm:flex-row gap-2">
                  <input
                    value={cardQuery}
                    onChange={(e) => setCardQuery(e.target.value)}
                    placeholder="输入卡密、卡名或效果关键字查卡..."
                    className="flex-1 px-3.5 py-2 text-xs input-modern"
                  />
                  <div className="flex items-center justify-between sm:justify-end gap-2">
                    <label className="flex items-center gap-1.5 text-xs text-slate-300 px-2 cursor-pointer">
                      <input
                        type="checkbox"
                        checked={diyOnlySearch}
                        onChange={(e) => setDiyOnlySearch(e.target.checked)}
                        className="w-4 h-4 rounded accent-emerald-500"
                      />
                      仅看 DIY 新卡
                    </label>
                    <button className="min-h-[38px] px-4 py-2 rounded-xl bg-emerald-600 hover:bg-emerald-500 text-xs font-semibold text-white flex items-center gap-1.5 cursor-pointer">
                      <Search className="w-3.5 h-3.5" /> 检索卡片
                    </button>
                  </div>
                </form>

                <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5 max-h-96 overflow-y-auto pr-0.5">
                  {cards.map((c) => (
                    <div key={c.id} className="glass-subcard rounded-xl p-3 text-xs space-y-1.5">
                      <div className="flex items-center justify-between gap-2 font-semibold">
                        <span className="truncate text-slate-100">
                          {c.name} <span className="font-mono text-slate-400 text-[11px]">#{c.id}</span>
                        </span>
                        {c.isDiy && (
                          <span className="px-1.5 py-0.5 rounded bg-emerald-500/20 text-emerald-300 text-[10px] shrink-0">
                            DIY
                          </span>
                        )}
                      </div>
                      <div className="text-[11px] text-slate-400 line-clamp-3 leading-relaxed">{c.desc}</div>
                    </div>
                  ))}
                  {cards.length === 0 && (
                    <div className="sm:col-span-2 py-8 text-center text-xs text-slate-500">
                      输入关键词点击「检索卡片」查看卡表详情
                    </div>
                  )}
                </div>
              </div>

              <div className="glass-card rounded-2xl p-4 sm:p-5 space-y-3 text-xs">
                <div className="flex items-center justify-between">
                  <span className="font-bold text-sm">混沌随机组卡试抽</span>
                  <button
                    onClick={drawChaos}
                    className="px-3 py-1.5 rounded-xl bg-purple-600 hover:bg-purple-500 text-white font-semibold cursor-pointer"
                  >
                    试抽一套
                  </button>
                </div>
                {chaosDeck ? (
                  <div className="space-y-2.5 glass-subcard p-3.5 rounded-xl">
                    <div className="flex justify-between items-center">
                      <span className="text-purple-300 font-semibold">
                        主卡组 {chaosDeck.mainCount} + 额外 {chaosDeck.extraCount}
                      </span>
                      <SmallCopy text={chaosDeck.ydk} label="复制 .ydk" onCopied={() => notify('已复制 .ydk 卡组内容')} />
                    </div>
                    <div className="text-[11px] text-slate-400 leading-relaxed">
                      抽样：{chaosDeck.sampleMainCards.map((x: any) => x.name).join('、')}
                    </div>
                  </div>
                ) : (
                  <div className="text-slate-500 py-10 text-center glass-subcard rounded-xl">
                    点击右上角「试抽一套」生成平衡混沌卡组，支持直接送入上方六维雷达剖析
                  </div>
                )}
              </div>
            </div>
          </div>
        )}

        {tab === 'selfservice' && (
          <div className="grid grid-cols-1 lg:grid-cols-3 gap-4 text-xs animate-fade-in">
            <form onSubmit={submitVpass} className="glass-card rounded-2xl p-5 space-y-3.5">
              <h3 className="text-sm font-bold text-emerald-300">1. 游戏昵称防冒用保护 ($vpass)</h3>
              <p className="text-slate-400 leading-relaxed">
                绑定专属口令后，客户端输入 <code className="text-emerald-300 font-mono">昵称$口令</code> 即可受保护登录，防止他人冒名顶替。
              </p>
              <input
                value={vpassName}
                onChange={(e) => setVpassName(e.target.value)}
                placeholder="你的游戏昵称 (如 Kaiba)"
                className="w-full px-3.5 py-2.5 input-modern"
                required
              />
              <input
                type="password"
                value={vpassCode}
                onChange={(e) => setVpassCode(e.target.value)}
                placeholder="设置保护口令 (4~32位)"
                className="w-full px-3.5 py-2.5 input-modern"
                required
              />
              <button className="w-full min-h-[42px] py-2.5 rounded-xl bg-emerald-600 hover:bg-emerald-500 text-white font-semibold cursor-pointer">
                绑定 / 验证昵称口令
              </button>
              {vpassResult && (
                <div className="p-3 rounded-xl glass-subcard border border-emerald-500/40 font-mono flex justify-between items-center gap-2">
                  <span className="truncate">客户端填：{vpassResult}</span>
                  <SmallCopy text={vpassResult} onCopied={() => notify('已复制登录格式')} />
                </div>
              )}
            </form>

            <form onSubmit={submitTournamentDeck} className="glass-card rounded-2xl p-5 space-y-3">
              <h3 className="text-sm font-bold text-sky-300">2. 赛事选手自助交牌 (.ydk)</h3>
              <div className="grid grid-cols-2 gap-2.5">
                <input
                  value={tTag}
                  onChange={(e) => setTTag(e.target.value)}
                  placeholder="赛事标识 (weekly)"
                  className="px-3 py-2 input-modern"
                  required
                />
                <input
                  value={tPlayer}
                  onChange={(e) => setTPlayer(e.target.value)}
                  placeholder="参赛游戏昵称"
                  className="px-3 py-2 input-modern"
                  required
                />
              </div>
              <CodeMirrorEditor
                value={tYdk}
                onChange={setTYdk}
                language="ydk"
                accent="emerald"
                placeholder={'#created by ...\n#main\n89631143\n#extra\n!side'}
                minHeight="145px"
                maxHeight="240px"
                compact
              />
              <button className="w-full min-h-[42px] py-2.5 rounded-xl bg-sky-600 hover:bg-sky-500 text-white font-semibold cursor-pointer">
                提交比赛卡组并获取校验码
              </button>
              {tResultHash && (
                <div className="p-2.5 rounded-xl glass-subcard border border-sky-500/40 font-mono text-[11px] break-all">
                  SHA-256 凭证：{tResultHash}
                </div>
              )}
            </form>

            <form onSubmit={submitDiyProposal} className="glass-card rounded-2xl p-5 space-y-3">
              <div className="flex items-center justify-between gap-2 flex-wrap">
                <h3 className="text-sm font-bold text-amber-300">3. 社区 DIY 投稿 / 导入 .ypk</h3>
                <div className="inline-flex p-0.5 rounded-xl bg-slate-950 border border-slate-800 text-[11px]">
                  <button
                    type="button"
                    onClick={() => setPropMode('single')}
                    className={`px-2.5 py-1 rounded-lg font-medium transition cursor-pointer ${
                      propMode === 'single' ? 'bg-amber-500 text-slate-950 font-semibold' : 'text-slate-400 hover:text-slate-200'
                    }`}
                  >
                    单卡编写
                  </button>
                  <button
                    type="button"
                    onClick={() => setPropMode('ypk')}
                    className={`px-2.5 py-1 rounded-lg font-medium transition cursor-pointer ${
                      propMode === 'ypk' ? 'bg-amber-500 text-slate-950 font-semibold' : 'text-slate-400 hover:text-slate-200'
                    }`}
                  >
                    导入 .ypk 整包
                  </button>
                </div>
              </div>
              <div className="grid grid-cols-2 gap-2">
                <input
                  value={propAuthor}
                  onChange={(e) => setPropAuthor(e.target.value)}
                  placeholder={portalUser ? `署名 (${portalUser.username})` : '投稿人署名'}
                  className="px-3 py-2 input-modern"
                />
                <input
                  value={propPkg}
                  onChange={(e) => setPropPkg(e.target.value)}
                  placeholder="目标包 ID (留空按包名)"
                  className="px-3 py-2 input-modern font-mono"
                />
              </div>

              {propMode === 'single' ? (
                <>
                  <div className="grid grid-cols-3 gap-2">
                    <input
                      type="number"
                      value={propCardId}
                      onChange={(e) => setPropCardId(Number(e.target.value))}
                      placeholder="卡密"
                      className="px-3 py-2 input-modern font-mono"
                      required
                    />
                    <input
                      value={propCardName}
                      onChange={(e) => setPropCardName(e.target.value)}
                      placeholder="新卡名称"
                      className="col-span-2 px-3 py-2 input-modern"
                      required
                    />
                  </div>
                  <textarea
                    rows={2}
                    value={propCardDesc}
                    onChange={(e) => setPropCardDesc(e.target.value)}
                    placeholder="卡片效果文本..."
                    className="w-full p-2.5 input-modern"
                    required
                  />
                  <CodeMirrorEditor
                    value={propLua}
                    onChange={setPropLua}
                    language="lua"
                    accent="emerald"
                    title={`script/c${propCardId}.lua`}
                    minHeight="135px"
                    maxHeight="250px"
                    compact
                  />
                  <button className="w-full min-h-[42px] py-2.5 rounded-xl bg-amber-500 hover:bg-amber-400 text-slate-950 font-bold flex items-center justify-center gap-1.5 cursor-pointer">
                    <Send className="w-3.5 h-3.5" /> 投递至管理后台待审队列
                  </button>
                </>
              ) : (
                <div className="space-y-3 pt-1">
                  <div className="glass-subcard rounded-2xl p-4 border border-dashed border-amber-500/40 text-center space-y-2">
                    <div className="text-slate-200 font-semibold">
                      直接导入 DataEditorX / DataEditorY / YGOFabrica 导出的 <code className="text-amber-300 font-mono">.ypk</code>
                    </div>
                    <p className="text-[11px] text-slate-400 leading-relaxed">
                      自动解包校验包内 <code className="text-slate-300 font-mono">.cdb</code>、
                      <code className="text-slate-300 font-mono">script/*.lua</code>、
                      <code className="text-slate-300 font-mono">pics/*</code>、
                      <code className="text-slate-300 font-mono">deck/*.ydk</code> 与{' '}
                      <code className="text-slate-300 font-mono">strings.conf</code>，预检通过后送入后台审批队列。
                    </p>
                    <label
                      className={`w-full min-h-[44px] mt-1 inline-flex items-center justify-center gap-1.5 px-4 py-2.5 rounded-xl bg-amber-500 hover:bg-amber-400 text-slate-950 font-bold cursor-pointer ${
                        propUploadingYpk ? 'opacity-60 pointer-events-none' : ''
                      }`}
                    >
                      <Send className="w-3.5 h-3.5" />
                      <span>{propUploadingYpk ? '正在上传并安全预检...' : '选择本地 .ypk / .zip 文件上传投稿'}</span>
                      <input
                        type="file"
                        accept=".ypk,.zip"
                        className="hidden"
                        onChange={(e) => {
                          submitYpkProposal(e.target.files);
                          e.target.value = '';
                        }}
                      />
                    </label>
                  </div>
                </div>
              )}
            </form>
          </div>
        )}
      </main>

      {/* 手机端原生体验底部固定 Tab 导航栏 (md:hidden) */}
      <nav className="md:hidden fixed bottom-0 inset-x-0 z-40 bg-slate-950/92 backdrop-blur-xl border-t border-white/[0.08] pb-safe">
        <div className="grid grid-cols-5 h-14">
          {navItems.map((item) => {
            const active = tab === item.key;
            return (
              <button
                key={item.key}
                type="button"
                onClick={() => {
                  setTab(item.key);
                  window.scrollTo({ top: 0, behavior: 'smooth' });
                }}
                className={`relative flex flex-col items-center justify-center gap-0.5 text-[10px] font-semibold transition cursor-pointer ${
                  active ? 'text-emerald-300' : 'text-slate-400 active:text-slate-200'
                }`}
              >
                {active && (
                  <span className="absolute top-0 w-7 h-0.5 rounded-full bg-emerald-400 shadow-[0_0_8px_rgba(52,211,153,0.9)]" />
                )}
                {item.icon}
                <span>{item.shortLabel}</span>
              </button>
            );
          })}
        </div>
      </nav>
    </div>
  );
}
