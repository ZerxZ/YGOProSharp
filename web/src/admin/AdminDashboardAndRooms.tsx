import React, { useEffect, useState, useCallback, useRef } from 'react';
import {
  Activity,
  Check,
  Copy,
  Database,
  Eye,
  Gamepad2,
  Layers,
  Plus,
  RefreshCw,
  Send,
  Swords,
  Video
} from 'lucide-react';
import { apiFetch, AuthUser, hasPerm, openAuthenticatedWebSocket } from '../api';
import { BanlistOption, QuickCopyPill, RoomConfigurator } from '../RoomConfigurator';
import { ReplayReviewPanel } from '../portal/ReplayReviewPanel';
import { LpVersusBar, MobileBottomSheet, StatBox } from '../portal/portalTypes';

export function CopyButton({ text, label }: { text: string; label?: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <button
      type="button"
      onClick={(e) => {
        e.stopPropagation();
        navigator.clipboard?.writeText(text);
        setCopied(true);
        setTimeout(() => setCopied(false), 1500);
      }}
      className="inline-flex items-center gap-1 p-1.5 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800/80 active:scale-95 transition cursor-pointer shrink-0"
      title="复制"
    >
      {copied ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
      {label && <span className="text-[11px]">{copied ? '已复制' : label}</span>}
    </button>
  );
}

export function DashboardView({ user, notify }: { user: AuthUser; notify: (t: string, k?: 'ok' | 'err') => void }) {
  const [status, setStatus] = useState<any>(null);
  const [logs, setLogs] = useState<any[]>([]);
  const [logRedacted, setLogRedacted] = useState(false);
  const [shoutText, setShoutText] = useState('');
  const [maintReason, setMaintReason] = useState('');
  const wsRef = useRef<WebSocket | null>(null);

  const load = useCallback(async () => {
    try {
      const [st, lg] = await Promise.all([
        apiFetch('/api/v1/server/status'),
        apiFetch('/api/v1/system/logs?limit=120')
      ]);
      setStatus(st);
      setLogs(lg.entries || []);
      setLogRedacted(Boolean(lg.redactedForStreamer));
    } catch (err) {
      notify(err instanceof Error ? err.message : '加载总览失败', 'err');
    }
  }, [notify]);

  useEffect(() => {
    load();
    let active = true;
    openAuthenticatedWebSocket((evt) => {
      if (!active) return;
      if (evt.type === 'log.entry') {
        setLogs((prev) => [...prev.slice(-149), evt.data]);
      } else if (evt.type === 'cache.stats') {
        setStatus((prev: any) => (prev ? { ...prev, cache: evt.data } : prev));
      } else if (evt.type === 'room.created' || evt.type === 'room.updated' || evt.type === 'diy.reloaded') {
        load();
      }
    })
      .then((ws) => {
        wsRef.current = ws;
      })
      .catch(() => {});

    return () => {
      active = false;
      wsRef.current?.close();
    };
  }, [load]);

  const toggleMaintenance = async () => {
    if (!status) return;
    try {
      await apiFetch('/api/v1/server/maintenance', {
        method: 'POST',
        body: JSON.stringify({ enabled: !status.maintenanceMode, reason: maintReason })
      });
      notify(!status.maintenanceMode ? '已开启维护模式' : '已解除维护模式');
      load();
    } catch (err) {
      notify(err instanceof Error ? err.message : '操作失败', 'err');
    }
  };

  const sendBroadcast = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!shoutText.trim()) return;
    try {
      await apiFetch('/api/v1/server/broadcast', {
        method: 'POST',
        body: JSON.stringify({ message: shoutText })
      });
      setShoutText('');
      notify('全服黄字广播已发送');
    } catch (err) {
      notify(err instanceof Error ? err.message : '广播失败', 'err');
    }
  };

  const flushCache = async (ns?: string) => {
    try {
      await apiFetch('/api/v1/system/cache/flush', {
        method: 'POST',
        body: JSON.stringify({ namespace: ns || null })
      });
      notify(`已清理缓存 (${ns || '全部命名空间'})`);
      load();
    } catch (err) {
      notify(err instanceof Error ? err.message : '清理缓存失败', 'err');
    }
  };

  const triggerReload = async () => {
    try {
      const res = await apiFetch('/api/v1/diy/reload', { method: 'POST', body: '{}' });
      notify(`热重载完成：卡库代数 v${res.reload.catalogVersion}，盲盒池 ${res.reload.presetDeckCount} 套`);
      load();
    } catch (err) {
      notify(err instanceof Error ? err.message : '热重载失败', 'err');
    }
  };

  if (!status) {
    return <div className="text-sm text-slate-400 py-8 text-center">正在读取服务器运行指标...</div>;
  }

  return (
    <div className="space-y-5 animate-fade-in">
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-2.5 sm:gap-4">
        <StatBox
          title="游戏服务状态"
          value={status.maintenanceMode ? '维护拦截中' : status.isRunning ? `运行中 :${status.port}` : '已停止'}
          sub={`在线连接 ${status.clientCount} · 房间 ${status.roomCount}`}
          icon={<Gamepad2 className="w-4 h-4" />}
          accent={status.maintenanceMode ? 'amber' : 'emerald'}
        />
        <StatBox
          title="运营数据库健康"
          value={`${status.database.provider} (${status.database.healthy ? '正常' : '异常'})`}
          sub={`探针 ${status.database.latencyMs} ms · ${status.database.journalMode || 'relational'}`}
          icon={<Database className="w-4 h-4" />}
          accent={status.database.healthy ? 'sky' : 'rose'}
        />
        <StatBox
          title="HybridCache 命中率"
          value={`${status.cache.hitRatioPercent}%`}
          sub={`L1:${status.cache.l1Hits} · Redis:${status.cache.l2RedisHits} · Miss:${status.cache.misses}`}
          icon={<Activity className="w-4 h-4" />}
          accent="indigo"
        />
        <StatBox
          title="卡库与随机池快照"
          value={`Catalog v${status.catalogVersion}`}
          sub={`盲盒 ${status.presetDeckCount} · DIY ${status.enabledDiyPackageCount}/${status.diyPackageCount} · 禁限 ${status.banlistCount}`}
          icon={<Layers className="w-4 h-4" />}
          accent="purple"
        />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <div className="glass-card rounded-2xl p-4 space-y-3">
          <div className="flex items-center justify-between">
            <h2 className="text-sm font-bold flex items-center gap-2">
              <Database className="w-4 h-4 text-indigo-400" /> 缓存与热重载控制
            </h2>
            <button
              onClick={load}
              className="p-1.5 rounded-lg text-xs text-slate-400 hover:text-white hover:bg-slate-800/80 flex items-center gap-1 cursor-pointer"
            >
              <RefreshCw className="w-3.5 h-3.5" /> 刷新
            </button>
          </div>
          <div className="text-xs text-slate-400 space-y-1">
            <div>Redis L2 状态：{status.cache.redisConnected ? '已连接共享缓存' : '单机 L1 内存模式（自动降级）'}</div>
            <div>平均读取延迟：{status.cache.averageLatencyMs} ms · 活跃原子票据：{status.cache.activeTickets}</div>
          </div>
          <div className="flex flex-wrap gap-2 pt-1">
            {hasPerm(user, 'diy:reload') && (
              <button
                onClick={triggerReload}
                className="min-h-[36px] px-3 py-1.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-xs font-semibold text-white cursor-pointer"
              >
                原子热重载 DIY & 卡组池
              </button>
            )}
            {hasPerm(user, 'system:cache_flush') && (
              <>
                <button
                  onClick={() => flushCache('cards')}
                  className="min-h-[36px] px-3 py-1.5 rounded-xl bg-slate-800 hover:bg-slate-700 text-xs cursor-pointer"
                >
                  清卡库缓存
                </button>
                <button
                  onClick={() => flushCache()}
                  className="min-h-[36px] px-3 py-1.5 rounded-xl bg-slate-800 hover:bg-rose-900/60 text-xs cursor-pointer"
                >
                  清空全部缓存
                </button>
              </>
            )}
          </div>
        </div>

        <div className="glass-card rounded-2xl p-4 space-y-3">
          <h2 className="text-sm font-bold flex items-center gap-2">
            <Send className="w-4 h-4 text-amber-400" /> 全服黄字公告 (Shout)
          </h2>
          <form onSubmit={sendBroadcast} className="flex gap-2">
            <input
              value={shoutText}
              onChange={(e) => setShoutText(e.target.value)}
              placeholder="向所有活动房间发送黄色系统提示..."
              disabled={!hasPerm(user, 'room:shout')}
              className="flex-1 min-w-0 px-3 py-2 text-xs input-modern input-modern-indigo"
            />
            <button
              disabled={!hasPerm(user, 'room:shout')}
              className="min-h-[38px] px-3.5 py-1.5 rounded-xl bg-amber-500 hover:bg-amber-400 disabled:opacity-40 text-slate-950 font-bold text-xs shrink-0 cursor-pointer"
            >
              发送
            </button>
          </form>

          {hasPerm(user, 'server:stop') && (
            <div className="pt-2.5 border-t border-slate-800/80 flex flex-col sm:flex-row items-stretch sm:items-center gap-2">
              <input
                value={maintReason}
                onChange={(e) => setMaintReason(e.target.value)}
                placeholder="维护说明（可选）"
                className="flex-1 min-w-0 px-3 py-1.5 text-xs input-modern input-modern-indigo"
              />
              <button
                onClick={toggleMaintenance}
                className={`min-h-[36px] px-3.5 py-1.5 rounded-xl text-xs font-semibold shrink-0 cursor-pointer ${
                  status.maintenanceMode ? 'bg-emerald-600 hover:bg-emerald-500 text-white' : 'bg-rose-600 hover:bg-rose-500 text-white'
                }`}
              >
                {status.maintenanceMode ? '恢复迎客' : '停服维护'}
              </button>
            </div>
          )}
        </div>

        <div className="glass-card rounded-2xl p-4 space-y-2 text-xs text-slate-300">
          <h2 className="text-sm font-bold text-slate-100">当前会话权限摘要</h2>
          <div className="glass-subcard rounded-xl p-3 space-y-1.5">
            <div>
              登录身份：<span className="font-semibold text-white">{user.username}</span>{' '}
              <span className="text-indigo-300">({user.roleCodes.join(', ')})</span>
            </div>
            <div>IP 可见权限：{user.canViewIp ? '允许查看选手 IP' : '已自动脱敏隐藏 IP'}</div>
            <div>可签发 MCP 最高角色：{user.allowedMcpRolesToIssue.join(', ') || '无'}</div>
          </div>
        </div>
      </div>

      <div className="glass-card rounded-2xl p-4">
        <div className="flex items-center justify-between mb-3">
          <h2 className="text-sm font-bold">实时结构化日志流 (WebSocket)</h2>
          <span className="text-xs text-slate-400">{logs.length} 条记录</span>
        </div>
        {logRedacted ? (
          <div className="text-xs text-amber-300 py-8 text-center glass-subcard rounded-xl px-3">
            当前处于主播只读模式 (streamer)，已自动屏蔽底层连接日志以防直播画面泄露玩家 IP。
          </div>
        ) : (
          <div className="h-56 sm:h-64 overflow-y-auto font-mono text-[11px] glass-subcard rounded-xl p-3 space-y-1">
            {logs.map((l) => (
              <div key={l.sequence} className="leading-relaxed break-all">
                <span className="text-slate-500">{new Date(l.timestampUtc).toLocaleTimeString()}</span>{' '}
                <span
                  className={
                    l.level === 'Error' || l.level === 'Critical'
                      ? 'text-rose-400'
                      : l.level === 'Warning'
                        ? 'text-amber-400'
                        : 'text-indigo-400'
                  }
                >
                  [{l.level}]
                </span>{' '}
                <span className="text-slate-400">{l.category}:</span>{' '}
                <span className="text-slate-200">{l.message}</span>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

function AdminRoomDetailBody({
  selectedRoom,
  user,
  botDecks,
  selectedBotDeck,
  setSelectedBotDeck,
  spawnBot,
  toggleDeath,
  kickSlot,
  kickByName,
  closeRoom,
  roomChatMsg,
  setRoomChatMsg,
  sendRoomChat,
  notify
}: {
  selectedRoom: any;
  user: AuthUser;
  botDecks: string[];
  selectedBotDeck: string;
  setSelectedBotDeck: (v: string) => void;
  spawnBot: (roomName: string, fillAll: boolean) => void;
  toggleDeath: (roomName: string, enable: boolean) => void;
  kickSlot: (roomName: string, slotIndex: number) => void;
  kickByName: (roomName: string, playerName: string) => void;
  closeRoom: (roomName: string) => void;
  roomChatMsg: string;
  setRoomChatMsg: (v: string) => void;
  sendRoomChat: (e: React.FormEvent) => void;
  notify: (t: string, k?: 'ok' | 'err') => void;
}) {
  const summary = selectedRoom.summary;
  const isTag = summary.mode === 2;

  return (
    <div className="space-y-3.5 text-xs">
      <div className="flex items-center justify-between gap-2 border-b border-slate-800/80 pb-2.5">
        <div className="min-w-0">
          <h2 className="font-bold text-sm text-indigo-300 truncate">{summary.roomName}</h2>
          <div className="text-[11px] text-slate-400 truncate">
            MR{summary.masterRule} · 禁限表: {summary.banlistName || '无'} · 观战 {summary.observerCount}
          </div>
        </div>
        {(hasPerm(user, 'room:close') || hasPerm(user, 'room:create_own')) && (
          <button
            onClick={() => closeRoom(summary.roomName)}
            className="min-h-[34px] px-3 py-1.5 rounded-xl bg-rose-600/85 hover:bg-rose-600 text-white font-semibold text-xs shrink-0 cursor-pointer"
          >
            关闭房间
          </button>
        )}
      </div>

      <LpVersusBar
        team0Lp={summary.team0Lp}
        team1Lp={summary.team1Lp}
        startLp={summary.startLp}
        turnCount={summary.turnCount}
        isTag={isTag}
      />

      {!user.isStreamerOnly && summary.hostPassword && (
        <div className="glass-subcard rounded-xl p-3 space-y-2">
          <div className="flex items-center justify-between gap-2 flex-wrap">
            <span className="text-slate-400 truncate">
              主机密码: <code className="font-mono text-white font-semibold">{summary.hostPassword}</code>
            </span>
            <QuickCopyPill
              text={summary.hostPassword}
              label="复制主机密码"
              onCopied={() => notify(`已复制主机密码：${summary.hostPassword}`)}
            />
          </div>
          {summary.fullHostPassword && summary.fullHostPassword !== summary.hostPassword && (
            <div className="flex items-center justify-between gap-2 flex-wrap pt-1.5 border-t border-slate-800/70">
              <span className="text-slate-400 truncate">
                完整口令: <code className="font-mono text-indigo-300">{summary.fullHostPassword}</code>
              </span>
              <QuickCopyPill
                text={summary.fullHostPassword}
                label="复制规则串"
                onCopied={() => notify(`已复制规则口令：${summary.fullHostPassword}`)}
              />
            </div>
          )}
        </div>
      )}

      {hasPerm(user, 'bot:spawn') && summary.state === 'Lobby' && (
        <div className="space-y-2 glass-subcard p-3 rounded-xl">
          <div className="text-xs font-semibold text-slate-200">召唤内置 WindBot</div>
          <div className="flex flex-col sm:flex-row gap-2">
            <select
              value={selectedBotDeck}
              onChange={(e) => setSelectedBotDeck(e.target.value)}
              className="flex-1 min-w-0 px-2.5 py-1.5 text-xs input-modern input-modern-indigo"
            >
              <option value="random">随机 AI 卡组 (random)</option>
              {botDecks.map((d) => (
                <option key={d} value={d}>
                  {d}
                </option>
              ))}
            </select>
            <div className="flex gap-2 shrink-0">
              <button
                onClick={() => spawnBot(summary.roomName, false)}
                className="flex-1 sm:flex-none min-h-[34px] px-3 py-1.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white font-semibold cursor-pointer"
              >
                +1 AI
              </button>
              <button
                onClick={() => spawnBot(summary.roomName, true)}
                className="flex-1 sm:flex-none min-h-[34px] px-3 py-1.5 rounded-xl bg-purple-600 hover:bg-purple-500 text-white font-semibold cursor-pointer"
              >
                补满空席
              </button>
            </div>
          </div>
        </div>
      )}

      {hasPerm(user, 'room:start_death') && (
        <div className="flex items-center justify-between glass-subcard p-3 rounded-xl">
          <span className="font-medium text-slate-200">额外回合猝死规则 (ED)</span>
          <button
            onClick={() => toggleDeath(summary.roomName, !summary.isSuddenDeath)}
            className={`min-h-[32px] px-3 py-1 rounded-xl font-semibold cursor-pointer ${
              summary.isSuddenDeath ? 'bg-slate-700 text-slate-200' : 'bg-rose-600 hover:bg-rose-500 text-white'
            }`}
          >
            {summary.isSuddenDeath ? '取消 ED' : '开启 ED'}
          </button>
        </div>
      )}

      <div className="space-y-1.5">
        <div className="font-semibold text-slate-300">席位管理</div>
        {summary.slots.map((s: any) => (
          <div key={s.seatIndex} className="flex items-center justify-between gap-2 glass-subcard px-3 py-2 rounded-xl">
            <span className="truncate">
              <span className="font-mono text-slate-500 mr-1">#{s.seatIndex}</span>
              <span className="text-slate-100">{s.playerName || '<空>'}</span>{' '}
              {s.clientIp && <span className="font-mono text-[11px] text-slate-400">({s.clientIp})</span>}
            </span>
            {s.playerName && hasPerm(user, 'room:kick') && (
              <button
                onClick={() => kickSlot(summary.roomName, s.seatIndex)}
                className="px-2 py-0.5 rounded-lg bg-rose-950/60 border border-rose-500/30 text-rose-300 hover:bg-rose-900/60 shrink-0 cursor-pointer"
              >
                踢出
              </button>
            )}
          </div>
        ))}
      </div>

      <div className="space-y-1.5">
        <div className="font-semibold text-sky-300 flex items-center gap-1">
          <Eye className="w-3.5 h-3.5" /> 观战观众列表 ({(summary.observers || []).length} 人)
        </div>
        {(summary.observers || []).length > 0 ? (
          <div className="space-y-1 max-h-32 overflow-y-auto">
            {(summary.observers || []).map((obs: any, idx: number) => (
              <div key={`${obs.playerName}-${idx}`} className="flex items-center justify-between gap-2 glass-subcard px-3 py-1.5 rounded-xl">
                <span className="truncate">
                  {obs.playerName} {obs.clientIp ? `(${obs.clientIp})` : ''}
                </span>
                {hasPerm(user, 'room:kick') && (
                  <button
                    onClick={() => kickByName(summary.roomName, obs.playerName)}
                    className="text-rose-400 hover:underline shrink-0 cursor-pointer"
                  >
                    踢出观众
                  </button>
                )}
              </div>
            ))}
          </div>
        ) : (
          <div className="text-[11px] text-slate-500 glass-subcard px-3 py-2 rounded-xl">当前暂无观战观众</div>
        )}
      </div>

      <div className="space-y-2">
        <div className="font-semibold text-slate-300">公开事件与房间聊天</div>
        <div className="h-44 overflow-y-auto glass-subcard rounded-xl p-3 space-y-1.5">
          {(selectedRoom.publicEvents || []).length === 0 && (selectedRoom.chats || []).length === 0 && (
            <div className="text-slate-500 text-center py-8">暂无公开事件或聊天消息</div>
          )}
          {(selectedRoom.publicEvents || []).map((ev: any, idx: number) => (
            <div key={`ev-${idx}`} className="text-indigo-300 leading-relaxed">
              · {ev.summary}
            </div>
          ))}
          {(selectedRoom.chats || []).map((c: any, idx: number) => {
            const st = Number(c.senderType ?? 16);
            const badge =
              st === 8
                ? { text: '系统', cls: 'bg-amber-500/20 text-amber-300' }
                : st >= 0 && st < 4
                  ? isTag
                    ? st < 2
                      ? { text: 'A队', cls: 'bg-emerald-500/20 text-emerald-300' }
                      : { text: 'B队', cls: 'bg-sky-500/20 text-sky-300' }
                    : st === 0
                      ? { text: '0席', cls: 'bg-emerald-500/20 text-emerald-300' }
                      : { text: '1席', cls: 'bg-sky-500/20 text-sky-300' }
                  : { text: '观战', cls: 'bg-slate-800 text-slate-400' };
            return (
              <div key={`ch-${idx}`} className="text-slate-200 flex items-baseline gap-1.5 flex-wrap leading-relaxed">
                <span className={`px-1.5 py-0.2 rounded text-[10px] font-semibold ${badge.cls}`}>{badge.text}</span>
                <span className="text-amber-300 font-medium">{c.senderName}:</span>
                <span>{c.message}</span>
              </div>
            );
          })}
        </div>
        {hasPerm(user, 'room:shout') && (
          <form onSubmit={sendRoomChat} className="flex gap-2">
            <input
              value={roomChatMsg}
              onChange={(e) => setRoomChatMsg(e.target.value)}
              placeholder="发送裁判黄字消息..."
              className="flex-1 min-w-0 px-3 py-2 text-xs input-modern input-modern-indigo"
            />
            <button className="min-h-[36px] px-3.5 py-1.5 rounded-xl bg-amber-500 hover:bg-amber-400 text-slate-950 font-bold text-xs shrink-0 cursor-pointer">
              发送
            </button>
          </form>
        )}
      </div>
    </div>
  );
}

export function RoomsView({ user, notify }: { user: AuthUser; notify: (t: string, k?: 'ok' | 'err') => void }) {
  const [rooms, setRooms] = useState<any[]>([]);
  const [banlists, setBanlists] = useState<BanlistOption[]>([]);
  const [botDecks, setBotDecks] = useState<string[]>([]);
  const [replays, setReplays] = useState<any[]>([]);
  const [selectedRoom, setSelectedRoom] = useState<any>(null);
  const [mobileRoomSheetOpen, setMobileRoomSheetOpen] = useState(false);
  const [selectedReplayDetail, setSelectedReplayDetail] = useState<any>(null);
  const [loadingReplayId, setLoadingReplayId] = useState<number | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [roomChatMsg, setRoomChatMsg] = useState('');
  const [selectedBotDeck, setSelectedBotDeck] = useState('random');
  const replayPanelRef = useRef<HTMLDivElement | null>(null);

  const loadRooms = useCallback(async () => {
    try {
      const res = await apiFetch('/api/v1/rooms');
      setRooms(res.rooms || []);
      setBanlists(res.banlists || []);
      setBotDecks(res.availableBotDecks || []);
      if (hasPerm(user, 'duel_log:view') || hasPerm(user, 'duel_log:view_own')) {
        const rep = await apiFetch('/api/v1/replays?limit=25');
        setReplays(rep.records || []);
      }
    } catch (err) {
      notify(err instanceof Error ? err.message : '加载房间失败', 'err');
    }
  }, [user, notify]);

  const loadRoomDetail = useCallback(
    async (roomName: string, openMobileSheet = false) => {
      try {
        const res = await apiFetch(`/api/v1/rooms/${encodeURIComponent(roomName)}`);
        setSelectedRoom(res.room);
        if (openMobileSheet) {
          setMobileRoomSheetOpen(true);
        }
      } catch (err) {
        notify(err instanceof Error ? err.message : '读取房间详情失败', 'err');
      }
    },
    [notify]
  );

  const openReplayDetail = useCallback(
    async (duelId: number) => {
      setLoadingReplayId(duelId);
      try {
        const detail = await apiFetch(`/api/v1/replays/${duelId}/detail`);
        setSelectedReplayDetail(detail);
        setTimeout(() => replayPanelRef.current?.scrollIntoView({ behavior: 'smooth', block: 'nearest' }), 80);
      } catch (err) {
        notify(err instanceof Error ? err.message : '读取赛后复盘详情失败', 'err');
      } finally {
        setLoadingReplayId(null);
      }
    },
    [notify]
  );

  useEffect(() => {
    loadRooms();
    const timer = setInterval(loadRooms, 3000);
    return () => clearInterval(timer);
  }, [loadRooms]);

  const spawnBot = async (roomName: string, fillAll: boolean) => {
    try {
      const res = await apiFetch(`/api/v1/rooms/${encodeURIComponent(roomName)}/bots`, {
        method: 'POST',
        body: JSON.stringify({ deck: selectedBotDeck, fillAll })
      });
      notify(`已召唤 AI: ${(res.spawned || []).join(', ')}`);
      setTimeout(() => loadRoomDetail(roomName, false), 400);
      loadRooms();
    } catch (err) {
      notify(err instanceof Error ? err.message : '召唤 AI 失败', 'err');
    }
  };

  const toggleDeath = async (roomName: string, enable: boolean) => {
    try {
      await apiFetch(`/api/v1/rooms/${encodeURIComponent(roomName)}/death`, {
        method: 'POST',
        body: JSON.stringify({ enabled: enable, extraTurns: 4 })
      });
      notify(enable ? '已开启额外回合猝死 (ED)' : '已关闭猝死规则');
      loadRoomDetail(roomName, false);
    } catch (err) {
      notify(err instanceof Error ? err.message : '操作失败', 'err');
    }
  };

  const sendRoomChat = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedRoom || !roomChatMsg.trim()) return;
    const rn = selectedRoom.summary.roomName;
    try {
      await apiFetch(`/api/v1/rooms/${encodeURIComponent(rn)}/chat`, {
        method: 'POST',
        body: JSON.stringify({ message: roomChatMsg })
      });
      setRoomChatMsg('');
      loadRoomDetail(rn, false);
    } catch (err) {
      notify(err instanceof Error ? err.message : '发送失败', 'err');
    }
  };

  const kickSlot = async (roomName: string, slotIndex: number) => {
    try {
      await apiFetch(`/api/v1/rooms/${encodeURIComponent(roomName)}/kick`, {
        method: 'POST',
        body: JSON.stringify({ slotIndex })
      });
      notify('已踢出该席位玩家');
      loadRoomDetail(roomName, false);
      loadRooms();
    } catch (err) {
      notify(err instanceof Error ? err.message : '踢人失败', 'err');
    }
  };

  const kickByName = async (roomName: string, playerName: string) => {
    try {
      await apiFetch(`/api/v1/rooms/${encodeURIComponent(roomName)}/kick`, {
        method: 'POST',
        body: JSON.stringify({ playerName })
      });
      notify(`已将 ${playerName} 请出房间`);
      loadRoomDetail(roomName, false);
      loadRooms();
    } catch (err) {
      notify(err instanceof Error ? err.message : '踢人失败', 'err');
    }
  };

  const closeRoom = async (roomName: string) => {
    try {
      await apiFetch(`/api/v1/rooms/${encodeURIComponent(roomName)}`, { method: 'DELETE' });
      notify(`房间 ${roomName} 已关闭`);
      setSelectedRoom(null);
      setMobileRoomSheetOpen(false);
      loadRooms();
    } catch (err) {
      notify(err instanceof Error ? err.message : '关闭失败', 'err');
    }
  };

  return (
    <div className="space-y-5 animate-fade-in">
      {/* 手机端房间裁判底部抽屉 */}
      <MobileBottomSheet
        open={mobileRoomSheetOpen && Boolean(selectedRoom)}
        title={selectedRoom ? `裁判巡房 · ${selectedRoom.summary.roomName}` : '房间详情'}
        subtitle={
          selectedRoom
            ? `${selectedRoom.summary.mode === 2 ? '2v2 Tag' : selectedRoom.summary.mode === 1 ? 'Match' : 'Single'} · ${selectedRoom.summary.state}`
            : undefined
        }
        onClose={() => setMobileRoomSheetOpen(false)}
      >
        {selectedRoom && (
          <AdminRoomDetailBody
            selectedRoom={selectedRoom}
            user={user}
            botDecks={botDecks}
            selectedBotDeck={selectedBotDeck}
            setSelectedBotDeck={setSelectedBotDeck}
            spawnBot={spawnBot}
            toggleDeath={toggleDeath}
            kickSlot={kickSlot}
            kickByName={kickByName}
            closeRoom={closeRoom}
            roomChatMsg={roomChatMsg}
            setRoomChatMsg={setRoomChatMsg}
            sendRoomChat={sendRoomChat}
            notify={notify}
          />
        )}
      </MobileBottomSheet>

      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div>
          <h1 className="text-sm sm:text-base font-bold flex items-center gap-2">
            <Swords className="w-5 h-5 text-indigo-400 shrink-0" /> 实时房间大厅 · 裁判巡房 · 斗蛐蛐围观
          </h1>
          <p className="text-[11px] sm:text-xs text-slate-400 mt-0.5">
            严格遵守对局战争迷雾：后台仅展示公开场面 LP、回合数与公开事件，绝不透视暗手牌与牌堆顺序
          </p>
        </div>
        <div className="flex items-center gap-2 shrink-0">
          <button
            onClick={loadRooms}
            className="min-h-[38px] px-3 py-1.5 rounded-xl glass-card hover:bg-slate-800 text-xs flex items-center gap-1.5 cursor-pointer"
          >
            <RefreshCw className="w-3.5 h-3.5" /> 刷新
          </button>
          {(hasPerm(user, 'room:create') || hasPerm(user, 'room:create_own')) && (
            <button
              onClick={() => setCreateOpen((v) => !v)}
              className="flex-1 sm:flex-none min-h-[38px] px-3.5 py-1.5 rounded-xl bg-gradient-to-r from-indigo-600 to-purple-600 hover:from-indigo-500 hover:to-purple-500 text-white text-xs font-semibold flex items-center justify-center gap-1.5 shadow-lg shadow-indigo-950/50 cursor-pointer"
            >
              <Plus className="w-4 h-4" /> {createOpen ? '收起建房面板' : '新建房间 / 规则配置'}
            </button>
          )}
        </div>
      </div>

      {createOpen && (
        <RoomConfigurator
          apiEndpoint="/api/v1/rooms"
          theme="indigo"
          canCreateOnServer={hasPerm(user, 'room:create') || hasPerm(user, 'room:create_own')}
          canIssueAiTokens={user.allowedMcpRolesToIssue.includes('player')}
          banlists={banlists}
          onCreated={(res) => {
            loadRooms();
            if (res?.room?.roomName) loadRoomDetail(res.room.roomName, false);
          }}
          onClose={() => setCreateOpen(false)}
          notify={notify}
        />
      )}

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4 items-start">
        <div className="lg:col-span-2 grid grid-cols-1 md:grid-cols-2 gap-3">
          {rooms.length === 0 && (
            <div className="md:col-span-2 glass-card rounded-2xl p-8 text-center text-xs text-slate-400">
              当前暂无活动房间，可点击右上角「新建房间 / 规则配置」或从游戏客户端直接输入房号密码连入。
            </div>
          )}
          {rooms.map((r) => {
            const observers: any[] = r.observers || [];
            const copyPw = r.hostPassword || r.joinPassword;
            const isSelected = selectedRoom?.summary?.roomName === r.roomName;
            const isTag = r.mode === 2;

            return (
              <div
                key={r.roomName}
                onClick={() => loadRoomDetail(r.roomName, true)}
                className={`glass-card rounded-2xl p-4 space-y-3 cursor-pointer transition active:scale-[0.995] ${
                  isSelected ? 'border-indigo-500/80 ring-1 ring-indigo-500/30' : 'hover:border-indigo-500/40'
                }`}
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="font-bold text-sm truncate">{r.roomName}</span>
                  <div className="flex items-center gap-1.5 shrink-0 flex-wrap justify-end">
                    <span className="px-2 py-0.5 rounded-full text-[10px] font-semibold bg-indigo-500/20 border border-indigo-500/30 text-indigo-300">
                      {isTag ? '2v2 Tag' : r.mode === 1 ? 'Match' : 'Single'}
                    </span>
                    {r.randomDeckMode !== 'None' && (
                      <span className="px-2 py-0.5 rounded-full text-[10px] bg-purple-500/20 border border-purple-500/30 text-purple-200">
                        {r.randomDeckMode}
                      </span>
                    )}
                    {r.isSuddenDeath && (
                      <span className="px-2 py-0.5 rounded-full text-[10px] bg-rose-500/20 text-rose-300">ED</span>
                    )}
                    <span className="px-2 py-0.5 rounded-full text-[10px] bg-slate-800 text-slate-300">{r.state}</span>
                  </div>
                </div>

                <LpVersusBar
                  team0Lp={r.team0Lp}
                  team1Lp={r.team1Lp}
                  startLp={r.startLp}
                  turnCount={r.turnCount}
                  isTag={isTag}
                />

                {!user.isStreamerOnly && copyPw && (
                  <div className="flex items-center justify-between gap-2 glass-subcard px-3 py-1.5 rounded-xl text-xs">
                    <span className="text-slate-400 truncate">
                      主机密码: <code className="font-mono text-indigo-300 font-semibold">{copyPw}</code>
                    </span>
                    <QuickCopyPill text={copyPw} label="复制密码" onCopied={() => notify(`已复制主机密码：${copyPw}`)} />
                  </div>
                )}

                <div className="space-y-1 text-xs">
                  {r.slots.map((s: any) => (
                    <div key={s.seatIndex} className="flex items-center justify-between gap-2 text-slate-300">
                      <span className="truncate">
                        <span className="font-mono text-slate-500 mr-1">#{s.seatIndex}</span>
                        {s.playerName || '<空位>'}{' '}
                        {s.isHost && <span className="text-amber-400 text-[11px]">[房主]</span>}
                      </span>
                      <span className="text-[11px] text-slate-400 shrink-0">
                        {s.clientIp ? `${s.clientIp} · ` : ''}
                        {s.playerName ? (s.isReady ? '已准备' : s.seatOrigin) : ''}
                      </span>
                    </div>
                  ))}
                </div>

                <div className="pt-2 border-t border-slate-800/70 flex items-center justify-between gap-2 text-[11px] text-slate-400">
                  <div className="flex items-center gap-1 truncate">
                    <Eye className="w-3.5 h-3.5 text-sky-400 shrink-0" />
                    <span>观众 ({r.observerCount ?? observers.length})：</span>
                    <span className="text-slate-300 truncate">
                      {observers.length > 0 ? observers.map((o: any) => o.playerName).join('、') : '暂无观众'}
                    </span>
                  </div>
                  <span className="lg:hidden text-indigo-400 font-medium shrink-0">裁判面板 →</span>
                </div>
              </div>
            );
          })}
        </div>

        {/* 桌面端右侧常驻裁判面板 */}
        <div className="hidden lg:block lg:sticky lg:top-20 glass-card rounded-2xl p-4">
          {selectedRoom ? (
            <AdminRoomDetailBody
              selectedRoom={selectedRoom}
              user={user}
              botDecks={botDecks}
              selectedBotDeck={selectedBotDeck}
              setSelectedBotDeck={setSelectedBotDeck}
              spawnBot={spawnBot}
              toggleDeath={toggleDeath}
              kickSlot={kickSlot}
              kickByName={kickByName}
              closeRoom={closeRoom}
              roomChatMsg={roomChatMsg}
              setRoomChatMsg={setRoomChatMsg}
              sendRoomChat={sendRoomChat}
              notify={notify}
            />
          ) : (
            <div className="text-xs text-slate-500 py-14 text-center space-y-2">
              <Swords className="w-7 h-7 text-slate-700 mx-auto" />
              <div>点击左侧任意房间卡片</div>
              <div className="text-[11px]">查看详情、召唤 WindBot 或执行裁判操作</div>
            </div>
          )}
        </div>
      </div>

      <div ref={replayPanelRef}>
        {selectedReplayDetail && (
          <ReplayReviewPanel
            detail={selectedReplayDetail}
            onClose={() => setSelectedReplayDetail(null)}
            notify={notify}
          />
        )}
      </div>

      <div className="glass-card rounded-2xl p-4 sm:p-5 space-y-3.5">
        <h2 className="text-sm font-bold flex items-center gap-2">
          <Video className="w-4 h-4 text-sky-400 shrink-0" /> 历史对局战报与云录像 (客户端输入 R#编号 即可回放)
        </h2>

        {/* 手机端卡片列表 */}
        <div className="md:hidden space-y-2.5">
          {replays.map((r) => (
            <div
              key={r.id}
              onClick={() => openReplayDetail(r.id)}
              className={`glass-subcard rounded-2xl p-3.5 space-y-2 text-xs active:scale-[0.99] cursor-pointer ${
                selectedReplayDetail?.duelId === r.id ? 'border-indigo-500/60' : ''
              }`}
            >
              <div className="flex items-center justify-between gap-2">
                <div className="flex items-center gap-2">
                  <span className="font-mono font-bold text-indigo-300 text-sm">{r.replayCode}</span>
                  <span className="px-2 py-0.5 rounded-full bg-slate-800 text-[10px] text-slate-300">
                    {r.mode === 2 ? '2v2 Tag' : r.mode === 1 ? 'Match' : 'Single'} · {r.turnCount}T
                  </span>
                </div>
                <span className="text-[11px] text-slate-500">{new Date(r.endedAt).toLocaleDateString()}</span>
              </div>
              <div className="text-slate-200 font-medium">
                {(r.players || []).map((p: any) => `${p.playerName}${p.isWinner ? '(胜)' : ''}`).join(' vs ')}
              </div>
              <div className="flex items-center gap-2 pt-1" onClick={(e) => e.stopPropagation()}>
                <button
                  type="button"
                  onClick={() => openReplayDetail(r.id)}
                  className="flex-1 py-2 rounded-xl bg-indigo-600/25 border border-indigo-500/40 text-indigo-200 font-semibold cursor-pointer"
                >
                  {loadingReplayId === r.id ? '加载中...' : 'AI 复盘详情'}
                </button>
                <QuickCopyPill text={r.replayCode} label="复制口令" onCopied={() => notify(`已复制 ${r.replayCode}`)} />
                {r.hasYrp && hasPerm(user, 'replay:download') && (
                  <a
                    href={`/api/v1/replays/${r.id}.yrp`}
                    download
                    className="px-3 py-2 rounded-xl bg-slate-800 text-sky-300 font-medium"
                  >
                    .yrp
                  </a>
                )}
              </div>
            </div>
          ))}
          {replays.length === 0 && <div className="py-8 text-center text-xs text-slate-500">暂无历史战报记录</div>}
        </div>

        {/* 桌面端表格 */}
        <div className="hidden md:block overflow-x-auto">
          <table className="w-full text-xs text-left">
            <thead className="text-slate-400 border-b border-slate-800">
              <tr>
                <th className="py-2">回放口令</th>
                <th>房间</th>
                <th>模式 / 随机</th>
                <th>参战席位</th>
                <th>回合数</th>
                <th>时间</th>
                <th>录像</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-800/60">
              {replays.map((r) => (
                <tr
                  key={r.id}
                  onClick={() => openReplayDetail(r.id)}
                  className={`cursor-pointer transition hover:bg-slate-800/40 ${
                    selectedReplayDetail?.duelId === r.id ? 'bg-indigo-950/30' : ''
                  }`}
                >
                  <td className="py-2.5 font-mono text-indigo-300 flex items-center gap-1" onClick={(e) => e.stopPropagation()}>
                    {r.replayCode} <CopyButton text={r.replayCode} />
                  </td>
                  <td>{r.roomName}</td>
                  <td>
                    {r.mode === 2 ? '2v2 Tag' : r.mode === 1 ? 'Match' : 'Single'} · {r.randomDeckMode}
                  </td>
                  <td>
                    {(r.players || []).map((p: any) => `${p.playerName}${p.isWinner ? '(胜)' : ''}`).join(' vs ')}
                  </td>
                  <td>{r.turnCount}</td>
                  <td className="text-slate-400">{new Date(r.endedAt).toLocaleString()}</td>
                  <td className="space-x-2" onClick={(e) => e.stopPropagation()}>
                    <button
                      type="button"
                      onClick={() => openReplayDetail(r.id)}
                      className="px-2.5 py-1 rounded-lg bg-indigo-600/20 hover:bg-indigo-600/40 border border-indigo-500/40 text-indigo-200 cursor-pointer"
                    >
                      {loadingReplayId === r.id ? '加载中...' : 'AI 复盘详情'}
                    </button>
                    {r.hasYrp && hasPerm(user, 'replay:download') && (
                      <a href={`/api/v1/replays/${r.id}.yrp`} className="text-sky-400 hover:underline" download>
                        下载 .yrp
                      </a>
                    )}
                  </td>
                </tr>
              ))}
              {replays.length === 0 && (
                <tr>
                  <td colSpan={7} className="py-6 text-center text-slate-500">
                    暂无历史战报记录
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
