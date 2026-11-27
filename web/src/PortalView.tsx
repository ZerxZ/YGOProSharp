import React, { useEffect, useState, useCallback, useRef, useMemo } from 'react';
import {
  Alert,
  Button,
  Card,
  Chip,
  Input,
  Label,
  SearchField,
  Surface,
  Switch,
  Tabs,
  TextField,
  Tooltip
} from '@heroui/react';
import {
  Gamepad2,
  Sparkles,
  Swords,
  Video,
  Palette,
  ShieldCheck,
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
  HelpCircle,
  BookOpen
} from 'lucide-react';
import { apiFetch, setPortalCsrfToken } from './api';
import { QuickCopyPill, RoomConfigurator } from './RoomConfigurator';
import {
  StatBox,
  SmallCopy,
  LpVersusBar,
  type PortalUser,
  type SurfaceInfo
} from './portal/portalTypes';
import { PortalAuthModal } from './portal/PortalAuthModal';
import { PortalAiTokenCenter } from './portal/PortalAiTokenCenter';
import { TagForceCommandPad } from './portal/TagForceCommandPad';
import { ReplayReviewPanel } from './portal/ReplayReviewPanel';
import { DeckSixDimRadarSection } from './portal/DeckSixDimRadarSection';
import { PortalDiyWorkshop } from './portal/PortalDiyWorkshop';
import { CodeMirrorEditor } from './components/CodeMirrorEditor';
import {
  ConsoleLogEntry,
  WorkspaceInspectorTab,
  WorkspaceSidebarGroup,
  WorkspaceShell
} from './components/WorkspaceShell';

export type { SurfaceInfo } from './portal/portalTypes';

type PortalTab = 'lobby' | 'ai_tokens' | 'replays' | 'diy_workshop' | 'catalog';
type RoomFilter = 'all' | 'lobby' | 'duel' | 'tag' | 'random';
type InspectorTabKey = 'room' | 'create' | 'guides';

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
      <Surface variant="secondary" className="rounded-xl p-3 space-y-2.5 border border-border">
        <div className="flex items-center justify-between gap-2">
          <span className="font-bold text-sm text-emerald-300 truncate">{room.summary.roomName}</span>
          <Chip size="sm" variant="soft" className="h-5 px-2 text-[10px]">
            MR{room.summary.masterRule} · {room.summary.banlistName || '无禁限'}
          </Chip>
        </div>

        <LpVersusBar
          team0Lp={room.summary.team0Lp}
          team1Lp={room.summary.team1Lp}
          startLp={room.summary.startLp}
          turnCount={room.summary.turnCount}
          isTag={isTag}
        />

        <div className="flex items-center justify-between gap-2 flex-wrap pt-0.5">
          <span className="text-muted truncate">
            进房密码: <code className="font-mono text-foreground font-semibold">{hostPw}</code>
          </span>
          <QuickCopyPill text={hostPw} label="复制口令" onCopied={() => notify(`已复制主机密码：${hostPw}`)} />
        </div>

        {room.summary.fullHostPassword && room.summary.fullHostPassword !== room.summary.roomName && (
          <div className="flex items-center justify-between gap-2 flex-wrap pt-2 border-t border-separator">
            <span className="text-muted truncate">
              完整规则串: <code className="font-mono text-emerald-300">{room.summary.fullHostPassword}</code>
            </span>
            <QuickCopyPill
              text={room.summary.fullHostPassword}
              label="复制规则串"
              onCopied={() => notify(`已复制规则口令：${room.summary.fullHostPassword}`)}
            />
          </div>
        )}
      </Surface>

      {isTag && (
        <TagForceCommandPad
          roomName={room.summary.roomName}
          slots={room.summary.slots || []}
          user={portalUser}
          onRequireAuth={onRequireAuth}
          notify={notify}
        />
      )}

      <Surface variant="secondary" className="rounded-xl p-3 space-y-1.5 border border-border">
        <div className="font-semibold text-sky-300 flex items-center justify-between">
          <span>观战席 ({(room.summary.observers || []).length} 人)</span>
          <span className="text-[10px] text-muted">满座进房自动转观战</span>
        </div>
        {(room.summary.observers || []).length > 0 ? (
          <div className="flex flex-wrap gap-1.5 pt-0.5">
            {(room.summary.observers || []).map((obs: any, idx: number) => (
              <Chip key={`${obs.playerName}-${idx}`} size="sm" variant="soft" color="accent" className="h-5 px-2 text-[11px]">
                <Eye className="w-3 h-3" />
                <Chip.Label>{obs.playerName}</Chip.Label>
              </Chip>
            ))}
          </div>
        ) : (
          <div className="text-muted text-[11px]">当前暂无观战观众</div>
        )}
      </Surface>

      <div className="space-y-1.5">
        <div className="font-semibold text-foreground flex items-center justify-between">
          <span>实时公开战况与房间聊天</span>
          <span className="text-[10px] text-muted">战争迷雾保护</span>
        </div>
        <Surface variant="secondary" className="h-64 overflow-y-auto rounded-xl p-3 space-y-1.5 border border-border">
          {(room.publicEvents || []).length === 0 && (room.chats || []).length === 0 && (
            <div className="text-muted text-center py-10">等待玩家出牌或发言...</div>
          )}
          {(room.publicEvents || []).map((e: any, i: number) => (
            <div key={`e-${i}`} className="text-emerald-300/95 leading-relaxed font-mono text-[11px]">
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
                  : { text: '观战', cls: 'bg-surface-tertiary text-muted border-border' };
            return (
              <div key={`c-${i}`} className="text-foreground flex items-baseline gap-1.5 flex-wrap leading-relaxed">
                <span className={`px-1.5 py-0.2 rounded border text-[10px] font-semibold ${badge.cls}`}>{badge.text}</span>
                <span className="text-amber-300 font-medium">{c.senderName}:</span>
                <span>{c.message}</span>
              </div>
            );
          })}
        </Surface>
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
  const [roomFilter, setRoomFilter] = useState<RoomFilter>('all');
  const [roomKeyword, setRoomKeyword] = useState<string>('');

  // 右侧三标签分栏状态 (对齐 T3 Code 右侧面板)
  const [inspectorOpen, setInspectorOpen] = useState<boolean>(true);
  const [mobileInspectorOpen, setMobileInspectorOpen] = useState<boolean>(false);
  const [inspectorTab, setInspectorTab] = useState<InspectorTabKey>('room');
  const [consoleLogs, setConsoleLogs] = useState<ConsoleLogEntry[]>([]);

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

  const pushLog = useCallback((source: string, message: string, level: ConsoleLogEntry['level'] = 'info') => {
    const time = new Date().toLocaleTimeString('zh-CN', { hour12: false });
    setConsoleLogs((prev) => [{ id: `${Date.now()}-${Math.random()}`, time, level, source, message }, ...prev].slice(0, 60));
  }, []);

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
    async (roomName: string, openOnMobile = false) => {
      try {
        const res = await apiFetch(`/api/portal/rooms/${encodeURIComponent(roomName)}`);
        setSelectedRoom(res.room);
        setInspectorTab('room');
        setInspectorOpen(true);
        if (openOnMobile) {
          setMobileInspectorOpen(true);
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
    pushLog('YGOProSharp.Portal', '已连接实时决斗大厅工作台，等待对局广播...');
    const proto = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const ws = new WebSocket(`${proto}//${window.location.host}/ws/public`);
    ws.onmessage = (ev) => {
      try {
        const parsed = JSON.parse(ev.data);
        const typeStr = String(parsed.type || 'lobby.sync');
        const summaryStr =
          parsed.payload?.summary ||
          parsed.payload?.message ||
          parsed.payload?.roomName ||
          '大厅状态已同步';
        pushLog(typeStr, String(summaryStr), typeStr.includes('chat') ? 'chat' : 'event');
      } catch {
        pushLog('ws.public', '收到大厅状态更新');
      }
      loadLobby();
      if (selectedRoomNameRef.current) {
        openRoomDetail(selectedRoomNameRef.current, false);
      }
    };
    wsRef.current = ws;
    return () => {
      wsRef.current?.close();
    };
  }, [loadLobby, openRoomDetail, pushLog, refreshPortalAuth]);

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
        setMobileInspectorOpen(false);
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

  const navItems: { key: PortalTab; label: string; shortLabel: string; icon: React.ReactNode; badge?: number }[] = [
    {
      key: 'lobby',
      label: '实时大厅 & 房间',
      shortLabel: '大厅',
      icon: <Gamepad2 className="w-3.5 h-3.5" />,
      badge: rooms.length
    },
    { key: 'ai_tokens', label: '我的 AI 令牌', shortLabel: 'AI 令牌', icon: <KeyRound className="w-3.5 h-3.5" /> },
    {
      key: 'replays',
      label: '战报 & 复盘剧场',
      shortLabel: '战报',
      icon: <Video className="w-3.5 h-3.5" />,
      badge: replays.length || undefined
    },
    {
      key: 'diy_workshop',
      label: 'DIY 制卡工坊',
      shortLabel: 'DIY 工坊',
      icon: <Palette className="w-3.5 h-3.5" />,
      badge: diyPackages.length || undefined
    },
    { key: 'catalog', label: '卡查 · 雷达 · 赛事', shortLabel: '卡查/赛事', icon: <Sparkles className="w-3.5 h-3.5" /> }
  ];

  const currentNavObj = navItems.find((n) => n.key === tab) || navItems[0];

  // 左侧栏活动分组 (对齐 T3 Code 的「运行中」/「已收尾」分组列表)
  const sidebarGroups: WorkspaceSidebarGroup[] = useMemo(
    () => [
      {
        id: 'running-rooms',
        label: '运行中房间',
        count: rooms.length,
        defaultOpen: true,
        emptyText: '当前暂无活动对局',
        items: rooms.slice(0, 12).map((r) => {
          const occupied = (r.slots || []).filter((s: any) => Boolean(s.playerName)).map((s: any) => s.playerName);
          return {
            id: r.roomName,
            title: r.roomName,
            meta: r.state === 'Lobby' ? '待开局' : `T${r.turnCount || 1}`,
            subtitle: occupied.length > 0 ? occupied.join(' vs ') : '空席等待加入',
            statusColor: r.state === 'Lobby' ? 'amber' : 'emerald',
            active: selectedRoom?.summary?.roomName === r.roomName,
            onSelect: () => {
              setTab('lobby');
              openRoomDetail(r.roomName, true);
            }
          };
        })
      },
      {
        id: 'finished-replays',
        label: '已收尾战报',
        count: replays.length,
        defaultOpen: false,
        emptyText: '暂无归档录像',
        items: replays.slice(0, 8).map((rp) => ({
          id: String(rp.id),
          title: `${rp.replayCode} · ${rp.roomName}`,
          meta: `${rp.turnCount}T`,
          subtitle: (rp.players || []).map((p: any) => p.playerName).join(' vs '),
          statusColor: 'muted' as const,
          active: selectedReplayDetail?.duelId === rp.id,
          onSelect: () => {
            setTab('replays');
            openReplayDetail(rp.id);
          }
        }))
      }
    ],
    [openReplayDetail, openRoomDetail, replays, rooms, selectedReplayDetail?.duelId, selectedRoom?.summary?.roomName]
  );

  // 右侧多标签分栏 (对齐 T3 Code 右侧 Diff / Inspector 文件标签栏)
  const inspectorTabs: WorkspaceInspectorTab[] = [
    {
      key: 'room',
      label: selectedRoom ? `实况 · ${selectedRoom.summary.roomName}` : '房间实况',
      icon: <Eye className="w-3.5 h-3.5 text-emerald-400" />,
      content: selectedRoom ? (
        <RoomDetailBody
          room={selectedRoom}
          portalUser={portalUser}
          onRequireAuth={() => {
            setMobileInspectorOpen(false);
            setAuthModalMode('login');
          }}
          notify={notify}
        />
      ) : (
        <div className="py-16 px-4 text-center space-y-3 text-xs text-muted">
          <Eye className="w-7 h-7 text-muted/50 mx-auto" />
          <div className="font-semibold text-foreground/85">尚未选中对局房间</div>
          <p className="leading-relaxed">
            点击中央或左侧任意房间查看实时 LP、观战席、公开战况与 2v2 搭档战术盘；或切换至上方「快速建房」标签开桌。
          </p>
          <Button
            size="sm"
            variant="secondary"
            onPress={() => setInspectorTab('create')}
          >
            <Plus className="w-3.5 h-3.5" /> 切换至快速建房
          </Button>
        </div>
      )
    },
    {
      key: 'create',
      label: '快速建房',
      icon: <Plus className="w-3.5 h-3.5 text-amber-400" />,
      content: (
        <RoomConfigurator
          apiEndpoint="/api/portal/rooms"
          theme="emerald"
          canCreateOnServer={Boolean(portalUser)}
          canIssueAiTokens={Boolean(portalUser?.allowedMcpRolesToIssue?.includes('player'))}
          banlists={overview?.banlists || []}
          onRequireAuth={() => {
            setMobileInspectorOpen(false);
            setAuthModalMode('register');
          }}
          onCreated={(res) => {
            loadLobby();
            if (res?.room?.roomName) {
              openRoomDetail(res.room.roomName, false);
            }
          }}
          notify={notify}
        />
      )
    },
    {
      key: 'guides',
      label: '联机口令',
      icon: <BookOpen className="w-3.5 h-3.5 text-sky-400" />,
      content: (
        <div className="space-y-3 text-xs">
          <Surface variant="secondary" className="rounded-xl p-3 space-y-2 border border-border">
            <div className="font-semibold text-emerald-300">MDPro3 / YGOPro 客户端三步直连</div>
            <div className="space-y-1 text-muted leading-relaxed">
              <div>① 打开游戏客户端进入「联机模式」</div>
              <div>
                ② 主机地址填 <code className="text-foreground font-mono">{gameHost}</code>，端口填{' '}
                <code className="text-foreground font-mono">{gamePort}</code>
              </div>
              <div>③ 主机密码框粘贴房间名或下方规则前缀口令（输入 R#编号 可直接看云回放）</div>
            </div>
            <QuickCopyPill
              text={fullServerAddr}
              label={`复制地址 ${fullServerAddr}`}
              onCopied={() => notify(`已复制服务器地址：${fullServerAddr}`)}
            />
          </Surface>

          <div className="space-y-2">
            <div className="font-semibold text-foreground px-0.5">免登录客户端建房规则前缀</div>
            {(overview?.ruleGuides || []).map((g: any) => (
              <Surface key={g.prefix} variant="secondary" className="rounded-xl p-3 space-y-1 border border-border">
                <div className="flex items-center justify-between">
                  <span className="font-mono font-bold text-emerald-300">{g.prefix}</span>
                  <SmallCopy text={g.prefix} label="复制" onCopied={() => notify(`已复制口令模板：${g.prefix}`)} />
                </div>
                <div className="font-semibold text-foreground">{g.title}</div>
                <div className="text-[11px] text-muted leading-relaxed">{g.desc}</div>
              </Surface>
            ))}
          </div>
        </div>
      )
    }
  ];

  const openQuickCreateRoom = () => {
    setInspectorTab('create');
    setInspectorOpen(true);
    setMobileInspectorOpen(true);
  };

  return (
    <WorkspaceShell
      themeClass="portal-theme"
      ambientClass="portal-ambient-bg"
      monogram="YP"
      monogramClass="bg-emerald-500/20 text-emerald-300 border-emerald-500/35"
      workspaceName="YGOProSharp"
      workspaceBranchTag="main"
      breadcrumbCurrent={currentNavObj.label}
      statusBadge={
        <Chip
          size="sm"
          variant="soft"
          color={overview?.maintenanceMode ? 'warning' : 'success'}
          className="h-5 px-2 text-[10px]"
        >
          <span
            className={`w-1.5 h-1.5 rounded-full ${
              overview?.maintenanceMode ? 'bg-amber-400' : 'bg-emerald-400 animate-pulse'
            }`}
          />
          <Chip.Label>{overview?.maintenanceMode ? '维护中' : `在线 ${overview?.clientCount ?? 0}`}</Chip.Label>
        </Chip>
      }
      navItems={navItems}
      activeNavKey={tab}
      onSelectNav={(k) => {
        setTab(k as PortalTab);
        window.scrollTo({ top: 0, behavior: 'smooth' });
      }}
      sidebarQuickAction={{
        label: '快速创建房间 / 生成口令',
        icon: <Plus className="w-3.5 h-3.5" />,
        onPress: openQuickCreateRoom
      }}
      sidebarGroups={sidebarGroups}
      sidebarFooterTop={
        portalUser ? (
          <div className="flex items-center justify-between gap-2 px-2 py-1.5 rounded-lg bg-surface border border-border/80 text-xs">
            <button
              type="button"
              onClick={() => setAuthModalMode('password')}
              className="min-w-0 text-left truncate cursor-pointer"
              title="点击修改个人用户名或密码"
            >
              <div className="font-semibold text-foreground truncate">{portalUser.displayName}</div>
              <div className="text-[10px] text-muted font-mono truncate">@{portalUser.username}</div>
            </button>
            <Tooltip delay={200}>
              <Button
                isIconOnly
                size="sm"
                variant="ghost"
                aria-label="退出登录"
                className="h-6 w-6 min-w-0 text-muted data-[hovered=true]:text-danger"
                onPress={handlePortalLogout}
              >
                <LogOut className="w-3.5 h-3.5" />
              </Button>
              <Tooltip.Content>
                <p className="text-xs">退出玩家账号</p>
              </Tooltip.Content>
            </Tooltip>
          </div>
        ) : (
          <div className="grid grid-cols-2 gap-1.5">
            <Button size="sm" variant="secondary" className="h-7 text-xs" onPress={() => setAuthModalMode('login')}>
              登录
            </Button>
            <Button size="sm" variant="primary" className="h-7 text-xs" onPress={() => setAuthModalMode('register')}>
              <UserPlus className="w-3 h-3" /> 注册
            </Button>
          </div>
        )
      }
      sidebarUtilityLeft={
        <Tooltip delay={200}>
          <button
            type="button"
            onClick={() => {
              navigator.clipboard?.writeText(fullServerAddr);
              notify(`已复制游戏联机地址：${fullServerAddr}`);
            }}
            className="text-[11px] font-mono text-emerald-400 hover:underline truncate cursor-pointer"
          >
            {fullServerAddr}
          </button>
          <Tooltip.Content>
            <p className="text-xs">点击复制客户端联机地址</p>
          </Tooltip.Content>
        </Tooltip>
      }
      sidebarUtilityRight={
        <>
          <Tooltip delay={200}>
            <Button
              isIconOnly
              size="sm"
              variant="ghost"
              aria-label="联机口令速查"
              className="h-7 w-7 min-w-0 text-muted data-[hovered=true]:text-foreground"
              onPress={() => {
                setInspectorTab('guides');
                setInspectorOpen(true);
                setMobileInspectorOpen(true);
              }}
            >
              <HelpCircle className="w-3.5 h-3.5" />
            </Button>
            <Tooltip.Content>
              <p className="text-xs">联机指引与规则前缀速查</p>
            </Tooltip.Content>
          </Tooltip>

          {onNavigateAdmin && surfaceInfo.unifiedPortal !== false && (
            <Tooltip delay={200}>
              <Button
                isIconOnly
                size="sm"
                variant="ghost"
                aria-label="进入管理后台"
                className="h-7 w-7 min-w-0 text-muted data-[hovered=true]:text-indigo-400"
                onPress={onNavigateAdmin}
              >
                <Shield className="w-3.5 h-3.5" />
              </Button>
              <Tooltip.Content>
                <p className="text-xs">管理控制台 (/admin)</p>
              </Tooltip.Content>
            </Tooltip>
          )}
        </>
      }
      headerRightActions={
        <div className="flex items-center gap-1.5">
          <Button size="sm" variant="primary" className="h-7 px-2.5 text-xs" onPress={openQuickCreateRoom}>
            <Plus className="w-3.5 h-3.5" />
            <span>新建房间</span>
          </Button>

          {!portalUser && (
            <Button
              size="sm"
              variant="secondary"
              className="hidden sm:inline-flex h-7 px-2.5 text-xs"
              onPress={() => setAuthModalMode('login')}
            >
              登录
            </Button>
          )}
        </div>
      }
      inspectorTabs={tab === 'lobby' ? inspectorTabs : []}
      activeInspectorKey={inspectorTab}
      onChangeInspectorKey={(k) => setInspectorTab(k as InspectorTabKey)}
      inspectorOpen={inspectorOpen}
      onInspectorOpenChange={setInspectorOpen}
      mobileInspectorOpen={mobileInspectorOpen}
      onMobileInspectorOpenChange={setMobileInspectorOpen}
      consoleLogs={consoleLogs}
      consoleStatusText={`Duel TCP ${fullServerAddr} · Rooms ${rooms.length} · Presets ${overview?.presetDeckCount ?? 0}`}
      modals={
        authModalMode ? (
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
        ) : null
      }
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
      {tab === 'lobby' && (
        <div className="space-y-4">
          {/* 顶部紧凑四指标栏 */}
          <div className="grid grid-cols-2 xl:grid-cols-4 gap-2.5">
            <StatBox
              title="游戏联机地址"
              value={overview?.maintenanceMode ? '维护拦截中' : fullServerAddr}
              sub={overview?.maintenanceReason || '兼容 MDPro3 / YGOPro 直连'}
              icon={<Gamepad2 className="w-3.5 h-3.5" />}
              accent={overview?.maintenanceMode ? 'amber' : 'emerald'}
            />
            <StatBox
              title="实时在线玩家"
              value={`${overview?.clientCount ?? 0} 人`}
              sub={`活动房间 ${overview?.roomCount ?? 0} 个`}
              icon={<Users className="w-3.5 h-3.5" />}
              accent="sky"
            />
            <StatBox
              title="随机盲盒主题池"
              value={`${overview?.presetDeckCount ?? 0} 套`}
              sub="RD# 盲盒 · CHAOS# 混沌即玩"
              icon={<Sparkles className="w-3.5 h-3.5" />}
              accent="purple"
            />
            <StatBox
              title="DIY 扩展卡库"
              value={`${overview?.enabledDiyPackageCount ?? 0} 包启用`}
              sub={`Catalog v${overview?.catalogVersion ?? 1}`}
              icon={<Layers className="w-3.5 h-3.5" />}
              accent="indigo"
            />
          </div>

          {/* 房间筛选 Tabs + 搜索栏 */}
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2.5">
            <Tabs
              selectedKey={roomFilter}
              onSelectionChange={(k) => setRoomFilter(String(k) as RoomFilter)}
              className="max-w-full"
            >
              <Tabs.ListContainer>
                <Tabs.List aria-label="房间模式筛选">
                  <Tabs.Tab id="all" className="text-xs px-2.5">
                    全部 ({rooms.length})
                    <Tabs.Indicator />
                  </Tabs.Tab>
                  <Tabs.Tab id="lobby" className="text-xs px-2.5">
                    等待加入
                    <Tabs.Indicator />
                  </Tabs.Tab>
                  <Tabs.Tab id="duel" className="text-xs px-2.5">
                    决斗中
                    <Tabs.Indicator />
                  </Tabs.Tab>
                  <Tabs.Tab id="tag" className="text-xs px-2.5">
                    2v2 双打
                    <Tabs.Indicator />
                  </Tabs.Tab>
                  <Tabs.Tab id="random" className="text-xs px-2.5">
                    随机盲盒
                    <Tabs.Indicator />
                  </Tabs.Tab>
                </Tabs.List>
              </Tabs.ListContainer>
            </Tabs>

            <div className="flex items-center gap-2">
              <SearchField
                aria-label="搜索房间名或玩家"
                variant="secondary"
                value={roomKeyword}
                onChange={setRoomKeyword}
                className="flex-1 sm:w-48"
              >
                <SearchField.Group>
                  <SearchField.SearchIcon />
                  <SearchField.Input placeholder="搜房名或玩家..." className="text-xs" />
                  <SearchField.ClearButton />
                </SearchField.Group>
              </SearchField>

              <Tooltip delay={200}>
                <Button isIconOnly size="sm" variant="secondary" aria-label="刷新房间列表" onPress={loadLobby}>
                  <RefreshCw className="w-3.5 h-3.5" />
                </Button>
                <Tooltip.Content>
                  <p className="text-xs">刷新实时房间列表</p>
                </Tooltip.Content>
              </Tooltip>
            </div>
          </div>

          {/* 房间卡片网格 */}
          {filteredRooms.length === 0 ? (
            <Card className="rounded-xl p-10 text-center items-center space-y-2.5">
              <Swords className="w-7 h-7 text-muted mx-auto" />
              <Card.Title className="text-sm font-semibold">
                {rooms.length === 0 ? '当前暂无活动房间' : '没有符合筛选条件的房间'}
              </Card.Title>
              <Card.Description className="text-xs max-w-md">
                点击右上角「新建房间」一键选择盲盒、2v2 双打或 AI 斗蛐蛐模板开桌，或在游戏客户端直连{' '}
                <code className="font-mono text-emerald-400">{fullServerAddr}</code> 输入口令建房。
              </Card.Description>
              <div className="flex items-center gap-2 pt-1">
                <Button size="sm" variant="primary" onPress={openQuickCreateRoom}>
                  <Plus className="w-3.5 h-3.5" /> 立即新建房间
                </Button>
                <Button
                  size="sm"
                  variant="secondary"
                  onPress={() => {
                    setInspectorTab('guides');
                    setInspectorOpen(true);
                    setMobileInspectorOpen(true);
                  }}
                >
                  查看规则口令表
                </Button>
              </div>
            </Card>
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
                    className="cursor-pointer transition active:scale-[0.996]"
                  >
                    <Card
                      className={`rounded-xl p-3.5 space-y-2.5 transition ${
                        isSelected
                          ? 'border-accent ring-1 ring-accent/35'
                          : 'hover:border-foreground/25'
                      }`}
                    >
                      <div className="flex items-center justify-between gap-2">
                        <div className="flex items-center gap-2 min-w-0">
                          <span
                            className={`w-2 h-2 rounded-full shrink-0 ${
                              r.state === 'Lobby' ? 'bg-amber-400' : 'bg-emerald-400 animate-pulse'
                            }`}
                          />
                          <span className="font-bold text-sm text-foreground truncate">{r.roomName}</span>
                        </div>
                        <div className="flex items-center gap-1.5 shrink-0">
                          {r.randomDeckMode && r.randomDeckMode !== 'None' && (
                            <Chip size="sm" variant="soft" className="h-5 px-2 text-[10px]">
                              {r.randomDeckMode}
                            </Chip>
                          )}
                          <Chip
                            size="sm"
                            variant="soft"
                            color={r.state === 'Lobby' ? 'warning' : 'success'}
                            className="h-5 px-2 text-[10px]"
                          >
                            {isTag ? '2v2 双打' : r.mode === 1 ? 'Match' : '1v1'} ·{' '}
                            {r.state === 'Lobby' ? '待开局' : '决斗中'}
                          </Chip>
                        </div>
                      </div>

                      <LpVersusBar
                        team0Lp={r.team0Lp}
                        team1Lp={r.team1Lp}
                        startLp={r.startLp}
                        turnCount={r.turnCount}
                        isTag={isTag}
                      />

                      <Surface variant="secondary" className="flex items-center justify-between gap-2 px-2.5 py-1.5 rounded-lg text-xs border border-border/70">
                        <span className="text-muted truncate">
                          口令: <code className="text-emerald-400 font-mono font-bold">{hostPw}</code>
                        </span>
                        <QuickCopyPill
                          text={hostPw}
                          label="复制进房"
                          onCopied={() => notify(`已复制房间主机密码：${hostPw}`)}
                        />
                      </Surface>

                      <div className="grid grid-cols-2 gap-1.5 text-xs">
                        {r.slots.map((s: any) => (
                          <div
                            key={s.seatIndex}
                            className={`px-2.5 py-1.5 rounded-lg border flex items-center justify-between gap-1 ${
                              s.playerName
                                ? 'bg-surface-secondary border-border text-foreground'
                                : 'bg-background/40 border-dashed border-border/70 text-muted'
                            }`}
                          >
                            <span className="truncate">
                              <span className="text-muted font-mono mr-1">#{s.seatIndex}</span>
                              {s.playerName || '空席待入'}
                            </span>
                            {s.isHost ? (
                              <span className="text-amber-400 text-[10px] shrink-0">房主</span>
                            ) : s.playerName && r.state === 'Lobby' ? (
                              <span className={`text-[10px] shrink-0 ${s.isReady ? 'text-emerald-400' : 'text-muted'}`}>
                                {s.isReady ? '就绪' : '准备中'}
                              </span>
                            ) : null}
                          </div>
                        ))}
                      </div>

                      <div className="pt-2 border-t border-separator flex items-center justify-between gap-2 text-[11px] text-muted">
                        <div className="flex items-center gap-1 truncate">
                          <Eye className="w-3.5 h-3.5 text-sky-400 shrink-0" />
                          <span>观众 {r.observerCount ?? observers.length}</span>
                          {observers.length > 0 && (
                            <span className="text-foreground/80 truncate">
                              · {observers.map((o: any) => o.playerName).join('、')}
                            </span>
                          )}
                        </div>
                        <div className="flex items-center gap-2 shrink-0" onClick={(e) => e.stopPropagation()}>
                          <span className="lg:hidden text-emerald-400 font-medium">查看实况 →</span>
                          {portalUser &&
                            r.ownerUsername &&
                            portalUser.username.toLowerCase() === String(r.ownerUsername).toLowerCase() && (
                              <Button
                                size="sm"
                                variant="danger-soft"
                                className="h-6 px-2 text-[11px]"
                                onPress={() => closeOwnRoom(r.roomName)}
                              >
                                关房
                              </Button>
                            )}
                        </div>
                      </div>
                    </Card>
                  </div>
                );
              })}
            </div>
          )}
        </div>
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

          <Card className="rounded-xl p-4 sm:p-5 space-y-4">
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
              <div>
                <h2 className="text-sm sm:text-base font-bold flex items-center gap-2">
                  <Video className="w-4 h-4 text-emerald-400" /> 全服历史对局战报 & AI 赛后复盘剧场
                </h2>
                <p className="text-[11px] text-muted mt-0.5">
                  游戏客户端输入 <code className="text-emerald-400 font-mono">R#编号</code> 可直接观看云回放；点击「AI 复盘详情」查看 0~100 发挥评分与时间轴
                </p>
              </div>
              <SearchField
                aria-label="筛选历史战报"
                variant="secondary"
                value={replayFilter}
                onChange={setReplayFilter}
                className="sm:w-60"
              >
                <SearchField.Group>
                  <SearchField.SearchIcon />
                  <SearchField.Input placeholder="按选手名或 R#编号 筛选..." className="text-xs" />
                  <SearchField.ClearButton />
                </SearchField.Group>
              </SearchField>
            </div>

            {/* 移动端卡片列表 */}
            <div className="md:hidden space-y-2.5">
              {filteredReplays.map((r) => (
                <Surface
                  key={r.id}
                  variant="secondary"
                  className={`rounded-xl p-3.5 space-y-2.5 text-xs border ${
                    selectedReplayDetail?.duelId === r.id ? 'border-accent' : 'border-border'
                  }`}
                >
                  <div onClick={() => openReplayDetail(r.id)} className="space-y-2 cursor-pointer">
                    <div className="flex items-center justify-between gap-2">
                      <div className="flex items-center gap-2">
                        <span className="font-mono font-bold text-emerald-400 text-sm">{r.replayCode}</span>
                        <Chip size="sm" variant="soft" className="h-5 px-2 text-[10px]">
                          {r.mode === 2 ? '2v2 双打' : r.mode === 1 ? 'Match' : '1v1'} · {r.turnCount} 回合
                        </Chip>
                      </div>
                      <span className="text-[11px] text-muted">{new Date(r.endedAt).toLocaleDateString()}</span>
                    </div>

                    <div className="text-foreground font-medium leading-relaxed">
                      {(r.players || []).map((p: any, idx: number) => (
                        <span key={idx}>
                          {idx > 0 && <span className="text-muted mx-1.5">vs</span>}
                          <span className={p.isWinner ? 'text-emerald-400 font-bold' : 'text-foreground/85'}>
                            {p.playerName}
                            {p.isWinner ? ' (胜)' : ''}
                          </span>
                        </span>
                      ))}
                    </div>
                  </div>

                  <div className="flex items-center gap-2 pt-1">
                    <Button
                      size="sm"
                      variant="primary"
                      className="flex-1"
                      isPending={loadingReplayId === r.id}
                      onPress={() => openReplayDetail(r.id)}
                    >
                      {loadingReplayId === r.id ? '加载中...' : '打开 AI 复盘剧场'}
                    </Button>
                    <QuickCopyPill
                      text={r.replayCode}
                      label="复制口令"
                      onCopied={() => notify(`已复制回放口令：${r.replayCode}`)}
                    />
                    {r.hasYrp && (
                      <a
                        href={`/api/portal/replays/${r.id}.yrp`}
                        download
                        className="px-3 py-1.5 rounded-lg bg-surface-tertiary text-sky-400 font-medium border border-border"
                      >
                        .yrp
                      </a>
                    )}
                  </div>
                </Surface>
              ))}
              {filteredReplays.length === 0 && (
                <div className="py-10 text-center text-xs text-muted">暂无匹配的历史战报记录</div>
              )}
            </div>

            {/* 桌面端数据表格 */}
            <div className="hidden md:block overflow-x-auto">
              <table className="w-full text-xs text-left">
                <thead className="text-muted border-b border-separator">
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
                <tbody className="divide-y divide-separator">
                  {filteredReplays.map((r) => (
                    <tr
                      key={r.id}
                      onClick={() => openReplayDetail(r.id)}
                      className={`cursor-pointer transition hover:bg-surface-secondary/60 ${
                        selectedReplayDetail?.duelId === r.id ? 'bg-surface-secondary' : ''
                      }`}
                    >
                      <td className="py-3" onClick={(e) => e.stopPropagation()}>
                        <QuickCopyPill
                          text={r.replayCode}
                          label={r.replayCode}
                          onCopied={() => notify(`已复制回放主机密码：${r.replayCode}`)}
                        />
                      </td>
                      <td className="font-semibold text-foreground">{r.roomName}</td>
                      <td>
                        <Chip size="sm" variant="soft" className="h-5 px-2 text-[11px]">
                          {r.mode === 2 ? '2v2 Tag' : r.mode === 1 ? 'Match' : 'Single'}
                        </Chip>
                      </td>
                      <td>
                        {(r.players || []).map((p: any, idx: number) => (
                          <span key={idx}>
                            {idx > 0 && <span className="text-muted mx-1">vs</span>}
                            <span className={p.isWinner ? 'text-emerald-400 font-semibold' : 'text-foreground/85'}>
                              {p.playerName}
                              {p.isWinner ? '(胜)' : ''}
                            </span>
                          </span>
                        ))}
                      </td>
                      <td className="font-mono">{r.turnCount}</td>
                      <td className="text-muted">{new Date(r.endedAt).toLocaleString()}</td>
                      <td className="text-right space-x-2" onClick={(e) => e.stopPropagation()}>
                        <Button
                          size="sm"
                          variant="secondary"
                          isPending={loadingReplayId === r.id}
                          onPress={() => openReplayDetail(r.id)}
                        >
                          {loadingReplayId === r.id ? '加载中...' : 'AI 复盘详情'}
                        </Button>
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
                      <td colSpan={7} className="py-10 text-center text-muted">
                        暂无已归档的历史对局录像
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </Card>
        </div>
      )}

      {tab === 'diy_workshop' && (
        <PortalDiyWorkshop diyPackages={diyPackages} portalUser={portalUser} notify={notify} />
      )}

      {tab === 'catalog' && (
        <div className="space-y-4 animate-fade-in">
          <DeckSixDimRadarSection chaosYdk={chaosDeck?.ydk} notify={notify} />

          <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
            <Card className="lg:col-span-2 rounded-xl p-4 sm:p-5 space-y-3.5">
              <form onSubmit={runSearch} className="flex flex-col sm:flex-row sm:items-center gap-2.5">
                <SearchField
                  aria-label="检索卡密或卡名"
                  variant="secondary"
                  value={cardQuery}
                  onChange={setCardQuery}
                  className="flex-1"
                >
                  <SearchField.Group>
                    <SearchField.SearchIcon />
                    <SearchField.Input placeholder="输入卡密、卡名或效果关键字查卡..." className="text-xs" />
                    <SearchField.ClearButton />
                  </SearchField.Group>
                </SearchField>

                <div className="flex items-center justify-between sm:justify-end gap-3">
                  <Switch size="sm" isSelected={diyOnlySearch} onChange={setDiyOnlySearch}>
                    <Switch.Control>
                      <Switch.Thumb />
                    </Switch.Control>
                    <Switch.Content>
                      <Label className="text-xs">仅看 DIY 新卡</Label>
                    </Switch.Content>
                  </Switch>

                  <Button type="submit" size="sm" variant="primary">
                    <Search className="w-3.5 h-3.5" /> 检索卡片
                  </Button>
                </div>
              </form>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5 max-h-96 overflow-y-auto pr-0.5">
                {cards.map((c) => (
                  <Surface key={c.id} variant="secondary" className="rounded-lg p-3 text-xs space-y-1.5 border border-border">
                    <div className="flex items-center justify-between gap-2 font-semibold">
                      <span className="truncate text-foreground">
                        {c.name} <span className="font-mono text-muted text-[11px]">#{c.id}</span>
                      </span>
                      {c.isDiy && (
                        <Chip size="sm" variant="soft" color="accent" className="h-5 px-1.5 text-[10px] shrink-0">
                          DIY
                        </Chip>
                      )}
                    </div>
                    <div className="text-[11px] text-muted line-clamp-3 leading-relaxed">{c.desc}</div>
                  </Surface>
                ))}
                {cards.length === 0 && (
                  <div className="sm:col-span-2 py-8 text-center text-xs text-muted">
                    输入关键词点击「检索卡片」查看卡表详情
                  </div>
                )}
              </div>
            </Card>

            <Card className="rounded-xl p-4 sm:p-5 space-y-3 text-xs">
              <Card.Header className="p-0 flex-row items-center justify-between">
                <Card.Title className="font-bold text-sm">混沌随机组卡试抽</Card.Title>
                <Button size="sm" variant="secondary" onPress={drawChaos}>
                  <Sparkles className="w-3.5 h-3.5 text-purple-400" />
                  试抽一套
                </Button>
              </Card.Header>
              <Card.Content className="p-0">
                {chaosDeck ? (
                  <Surface variant="secondary" className="space-y-2.5 p-3.5 rounded-lg border border-border">
                    <div className="flex justify-between items-center">
                      <span className="text-purple-400 font-semibold">
                        主卡组 {chaosDeck.mainCount} + 额外 {chaosDeck.extraCount}
                      </span>
                      <SmallCopy text={chaosDeck.ydk} label="复制 .ydk" onCopied={() => notify('已复制 .ydk 卡组内容')} />
                    </div>
                    <div className="text-[11px] text-muted leading-relaxed">
                      抽样：{chaosDeck.sampleMainCards.map((x: any) => x.name).join('、')}
                    </div>
                  </Surface>
                ) : (
                  <Surface variant="secondary" className="text-muted py-10 px-4 text-center rounded-lg border border-border">
                    点击右上角「试抽一套」生成平衡混沌卡组，支持直接送入上方六维雷达剖析
                  </Surface>
                )}
              </Card.Content>
            </Card>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4 text-xs">
            <Card className="rounded-xl p-5">
              <form onSubmit={submitVpass} className="space-y-3.5">
                <div className="space-y-1">
                  <h3 className="text-sm font-bold text-emerald-400 flex items-center gap-1.5">
                    <ShieldCheck className="w-4 h-4" /> 游戏昵称防冒用保护 ($vpass)
                  </h3>
                  <p className="text-muted leading-relaxed">
                    绑定专属口令后，客户端输入 <code className="text-emerald-400 font-mono">昵称$口令</code> 即可受保护登录，防止他人冒名顶替。
                  </p>
                </div>
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5">
                  <TextField fullWidth isRequired value={vpassName} onChange={setVpassName}>
                    <Label className="text-xs">游戏昵称</Label>
                    <Input variant="secondary" placeholder="如 Kaiba" />
                  </TextField>
                  <TextField fullWidth isRequired type="password" value={vpassCode} onChange={setVpassCode}>
                    <Label className="text-xs">保护口令 (4~32位)</Label>
                    <Input variant="secondary" placeholder="设置专属口令" />
                  </TextField>
                </div>
                <Button type="submit" variant="primary" fullWidth>
                  绑定 / 验证昵称口令
                </Button>
                {vpassResult && (
                  <Surface variant="secondary" className="p-3 rounded-lg border border-emerald-500/40 font-mono flex justify-between items-center gap-2">
                    <span className="truncate">客户端填：{vpassResult}</span>
                    <SmallCopy text={vpassResult} onCopied={() => notify('已复制登录格式')} />
                  </Surface>
                )}
              </form>
            </Card>

            <Card className="rounded-xl p-5">
              <form onSubmit={submitTournamentDeck} className="space-y-3">
                <h3 className="text-sm font-bold text-sky-400">赛事选手自助交牌 (.ydk)</h3>
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5">
                  <TextField fullWidth isRequired value={tTag} onChange={setTTag}>
                    <Label className="text-xs">赛事标识</Label>
                    <Input variant="secondary" placeholder="weekly" />
                  </TextField>
                  <TextField fullWidth isRequired value={tPlayer} onChange={setTPlayer}>
                    <Label className="text-xs">参赛游戏昵称</Label>
                    <Input variant="secondary" placeholder="输入参赛昵称" />
                  </TextField>
                </div>
                <CodeMirrorEditor
                  value={tYdk}
                  onChange={setTYdk}
                  language="ydk"
                  accent="emerald"
                  placeholder={'#created by ...\n#main\n89631143\n#extra\n!side'}
                  minHeight="135px"
                  maxHeight="220px"
                  compact
                />
                <Button type="submit" variant="secondary" fullWidth>
                  提交比赛卡组并获取校验码
                </Button>
                {tResultHash && (
                  <Surface variant="secondary" className="p-2.5 rounded-lg border border-sky-500/40 font-mono text-[11px] break-all">
                    SHA-256 凭证：{tResultHash}
                  </Surface>
                )}
              </form>
            </Card>
          </div>
        </div>
      )}
    </WorkspaceShell>
  );
}
