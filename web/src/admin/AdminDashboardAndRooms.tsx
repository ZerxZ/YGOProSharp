import React, { useEffect, useState, useCallback, useRef, useMemo } from 'react';
import {
  Alert,
  Button,
  Card,
  Chip,
  Input,
  SearchField,
  Spinner,
  Surface,
  Tabs
} from '@heroui/react';
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
  Terminal,
  Video
} from 'lucide-react';
import { apiFetch, AuthUser, hasPerm, openAuthenticatedWebSocket } from '../api';
import { BanlistOption, QuickCopyPill, RoomConfigurator } from '../RoomConfigurator';
import { ReplayReviewPanel } from '../portal/ReplayReviewPanel';
import { LpVersusBar, MobileBottomSheet, StatBox } from '../portal/portalTypes';

export function CopyButton({ text, label }: { text: string; label?: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <span onClick={(e) => e.stopPropagation()} className="inline-flex shrink-0">
      <Button
        size="sm"
        variant={copied ? 'secondary' : 'ghost'}
        isIconOnly={!label}
        aria-label={label || '复制'}
        className="h-7 min-w-0 px-2 text-xs text-muted data-[hovered=true]:text-foreground"
        onPress={() => {
          navigator.clipboard?.writeText(text);
          setCopied(true);
          setTimeout(() => setCopied(false), 1500);
        }}
      >
        {copied ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
        {label && <span className="text-[11px]">{copied ? '已复制' : label}</span>}
      </Button>
    </span>
  );
}

export function DashboardView({ user, notify }: { user: AuthUser; notify: (t: string, k?: 'ok' | 'err') => void }) {
  const [status, setStatus] = useState<any>(null);
  const [logs, setLogs] = useState<any[]>([]);
  const [logRedacted, setLogRedacted] = useState(false);
  const [logFilter, setLogFilter] = useState<'all' | 'warn' | 'err'>('all');
  const [logKeyword, setLogKeyword] = useState('');
  const [shoutText, setShoutText] = useState('');
  const [maintReason, setMaintReason] = useState('');
  const wsRef = useRef<WebSocket | null>(null);

  const load = useCallback(async () => {
    try {
      const [st, lg] = await Promise.all([
        apiFetch('/api/v1/server/status'),
        apiFetch('/api/v1/system/logs?limit=150')
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
        setLogs((prev) => [...prev.slice(-179), evt.data]);
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

  const filteredLogs = useMemo(() => {
    return logs.filter((l) => {
      if (logFilter === 'err' && l.level !== 'Error' && l.level !== 'Critical') return false;
      if (logFilter === 'warn' && l.level !== 'Warning' && l.level !== 'Error' && l.level !== 'Critical') return false;
      if (logKeyword.trim()) {
        const kw = logKeyword.trim().toLowerCase();
        return (
          String(l.message || '').toLowerCase().includes(kw) ||
          String(l.category || '').toLowerCase().includes(kw)
        );
      }
      return true;
    });
  }, [logs, logFilter, logKeyword]);

  if (!status) {
    return (
      <div className="flex items-center justify-center gap-2 text-xs text-muted py-12">
        <Spinner size="sm" />
        <span>正在读取服务器运行指标...</span>
      </div>
    );
  }

  return (
    <div className="space-y-4 animate-fade-in">
      <div className="grid grid-cols-2 xl:grid-cols-4 gap-2.5">
        <StatBox
          title="游戏服务状态"
          value={status.maintenanceMode ? '维护拦截中' : status.isRunning ? `运行中 :${status.port}` : '已停止'}
          sub={`在线连接 ${status.clientCount} · 房间 ${status.roomCount}`}
          icon={<Gamepad2 className="w-3.5 h-3.5" />}
          accent={status.maintenanceMode ? 'amber' : 'emerald'}
        />
        <StatBox
          title="运营数据库健康"
          value={`${status.database.provider} (${status.database.healthy ? '正常' : '异常'})`}
          sub={`探针 ${status.database.latencyMs} ms · ${status.database.journalMode || 'relational'}`}
          icon={<Database className="w-3.5 h-3.5" />}
          accent={status.database.healthy ? 'sky' : 'rose'}
        />
        <StatBox
          title="HybridCache 命中率"
          value={`${status.cache.hitRatioPercent}%`}
          sub={`L1:${status.cache.l1Hits} · Redis:${status.cache.l2RedisHits} · Miss:${status.cache.misses}`}
          icon={<Activity className="w-3.5 h-3.5" />}
          accent="indigo"
        />
        <StatBox
          title="卡库与随机池快照"
          value={`Catalog v${status.catalogVersion}`}
          sub={`盲盒 ${status.presetDeckCount} · DIY ${status.enabledDiyPackageCount}/${status.diyPackageCount} · 禁限 ${status.banlistCount}`}
          icon={<Layers className="w-3.5 h-3.5" />}
          accent="purple"
        />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4 text-xs">
        <Card className="rounded-xl p-4 space-y-3">
          <div className="flex items-center justify-between">
            <h2 className="text-sm font-bold flex items-center gap-2">
              <Database className="w-4 h-4 text-indigo-400" /> 缓存与热重载控制
            </h2>
            <Button size="sm" variant="ghost" className="h-7 px-2 text-xs" onPress={load}>
              <RefreshCw className="w-3.5 h-3.5" /> 刷新
            </Button>
          </div>
          <Surface variant="secondary" className="rounded-lg p-3 border border-border space-y-1 text-muted">
            <div>
              Redis L2：<span className="text-foreground">{status.cache.redisConnected ? '已连接共享缓存' : '单机 L1 内存模式'}</span>
            </div>
            <div>
              平均延迟：<span className="font-mono text-foreground">{status.cache.averageLatencyMs} ms</span> · 活跃票据：
              <span className="font-mono text-foreground">{status.cache.activeTickets}</span>
            </div>
          </Surface>
          <div className="flex flex-wrap gap-2 pt-0.5">
            {hasPerm(user, 'diy:reload') && (
              <Button size="sm" variant="primary" onPress={triggerReload}>
                原子热重载 DIY & 卡组池
              </Button>
            )}
            {hasPerm(user, 'system:cache_flush') && (
              <>
                <Button size="sm" variant="secondary" onPress={() => flushCache('cards')}>
                  清卡库缓存
                </Button>
                <Button size="sm" variant="danger-soft" onPress={() => flushCache()}>
                  清空全部缓存
                </Button>
              </>
            )}
          </div>
        </Card>

        <Card className="rounded-xl p-4 space-y-3">
          <h2 className="text-sm font-bold flex items-center gap-2">
            <Send className="w-4 h-4 text-amber-400" /> 全服黄字公告 & 维护开关
          </h2>
          <form onSubmit={sendBroadcast} className="flex gap-2">
            <Input
              variant="secondary"
              value={shoutText}
              onChange={(e) => setShoutText(e.target.value)}
              placeholder="向所有活动房间发送黄色系统广播..."
              disabled={!hasPerm(user, 'room:shout')}
              className="flex-1 text-xs"
            />
            <Button type="submit" size="sm" variant="primary" isDisabled={!hasPerm(user, 'room:shout')} className="shrink-0">
              广播
            </Button>
          </form>

          {hasPerm(user, 'server:stop') && (
            <div className="pt-2.5 border-t border-separator flex flex-col sm:flex-row items-stretch sm:items-center gap-2">
              <Input
                variant="secondary"
                value={maintReason}
                onChange={(e) => setMaintReason(e.target.value)}
                placeholder="维护说明（可选）"
                className="flex-1 text-xs"
              />
              <Button
                size="sm"
                variant={status.maintenanceMode ? 'primary' : 'danger'}
                onPress={toggleMaintenance}
                className="shrink-0"
              >
                {status.maintenanceMode ? '恢复迎客' : '停服维护'}
              </Button>
            </div>
          )}
        </Card>

        <Card className="rounded-xl p-4 space-y-2.5">
          <h2 className="text-sm font-bold">当前会话权限摘要</h2>
          <Surface variant="secondary" className="rounded-lg p-3 border border-border space-y-2">
            <div className="flex items-center justify-between gap-2">
              <span className="text-muted">登录身份</span>
              <span className="font-semibold text-foreground">
                @{user.username}{' '}
                <Chip size="sm" variant="soft" color="accent" className="h-5 px-1.5 font-mono text-[10px] ml-1">
                  {user.roleCodes.join(', ')}
                </Chip>
              </span>
            </div>
            <div className="flex items-center justify-between gap-2">
              <span className="text-muted">选手 IP 可见性</span>
              <span className={user.canViewIp ? 'text-emerald-400' : 'text-amber-400'}>
                {user.canViewIp ? '允许查看' : '直播自动脱敏'}
              </span>
            </div>
            <div className="flex items-center justify-between gap-2">
              <span className="text-muted">可签发 MCP 角色</span>
              <span className="font-mono text-foreground">{user.allowedMcpRolesToIssue.join(', ') || '无'}</span>
            </div>
          </Surface>
        </Card>
      </div>

      {/* T3 Code 终端风格结构化实时日志台 */}
      <Card className="rounded-xl p-4 space-y-3">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2.5">
          <div className="flex items-center gap-2">
            <Terminal className="w-4 h-4 text-indigo-400" />
            <h2 className="text-sm font-bold">实时结构化日志流 (WebSocket)</h2>
            <Chip size="sm" variant="soft" className="h-5 px-2 font-mono text-[10px]">
              {filteredLogs.length} / {logs.length}
            </Chip>
          </div>

          {!logRedacted && (
            <div className="flex items-center gap-2 flex-wrap">
              <Tabs
                selectedKey={logFilter}
                onSelectionChange={(k) => setLogFilter(String(k) as 'all' | 'warn' | 'err')}
              >
                <Tabs.ListContainer>
                  <Tabs.List aria-label="日志级别过滤">
                    <Tabs.Tab id="all" className="text-xs px-2.5">
                      全部
                      <Tabs.Indicator />
                    </Tabs.Tab>
                    <Tabs.Tab id="warn" className="text-xs px-2.5">
                      Warning+
                      <Tabs.Indicator />
                    </Tabs.Tab>
                    <Tabs.Tab id="err" className="text-xs px-2.5">
                      Error
                      <Tabs.Indicator />
                    </Tabs.Tab>
                  </Tabs.List>
                </Tabs.ListContainer>
              </Tabs>

              <SearchField
                aria-label="筛选系统日志"
                variant="secondary"
                value={logKeyword}
                onChange={setLogKeyword}
                className="w-44 sm:w-56"
              >
                <SearchField.Group>
                  <SearchField.SearchIcon />
                  <SearchField.Input placeholder="搜分类或日志内容..." className="text-xs" />
                  <SearchField.ClearButton />
                </SearchField.Group>
              </SearchField>
            </div>
          )}
        </div>

        {logRedacted ? (
          <Alert status="warning">
            <Alert.Indicator />
            <Alert.Content>
              <Alert.Description className="text-xs">
                当前处于主播只读模式 (streamer)，已自动屏蔽底层连接日志以防直播画面泄露玩家 IP 与私密房口令。
              </Alert.Description>
            </Alert.Content>
          </Alert>
        ) : (
          <div className="h-64 overflow-y-auto font-mono text-[11px] bg-[var(--console-bg)] rounded-xl border border-border p-3 space-y-1">
            {filteredLogs.map((l) => (
              <div key={l.sequence} className="leading-relaxed break-all">
                <span className="text-muted">{new Date(l.timestampUtc).toLocaleTimeString('zh-CN', { hour12: false })}</span>{' '}
                <span
                  className={
                    l.level === 'Error' || l.level === 'Critical'
                      ? 'text-rose-400 font-semibold'
                      : l.level === 'Warning'
                        ? 'text-amber-400 font-semibold'
                        : 'text-indigo-400'
                  }
                >
                  [{l.level}]
                </span>{' '}
                <span className="text-muted">{l.category}:</span>{' '}
                <span className="text-foreground/90">{l.message}</span>
              </div>
            ))}
            {filteredLogs.length === 0 && (
              <div className="py-10 text-center text-muted font-sans text-xs">没有匹配当前筛选条件的日志条目</div>
            )}
          </div>
        )}
      </Card>
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
    <div className="space-y-3 text-xs">
      <div className="flex items-center justify-between gap-2 border-b border-separator pb-2.5">
        <div className="min-w-0">
          <h2 className="font-bold text-sm text-indigo-400 truncate">{summary.roomName}</h2>
          <div className="text-[11px] text-muted truncate">
            MR{summary.masterRule} · 禁限表: {summary.banlistName || '无'} · 观战 {summary.observerCount}
          </div>
        </div>
        {(hasPerm(user, 'room:close') || hasPerm(user, 'room:create_own')) && (
          <Button size="sm" variant="danger" onPress={() => closeRoom(summary.roomName)} className="shrink-0 h-7 px-2.5 text-xs">
            关闭房间
          </Button>
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
        <Surface variant="secondary" className="rounded-xl p-3 space-y-2 border border-border">
          <div className="flex items-center justify-between gap-2 flex-wrap">
            <span className="text-muted truncate">
              主机密码: <code className="font-mono text-foreground font-semibold">{summary.hostPassword}</code>
            </span>
            <QuickCopyPill
              text={summary.hostPassword}
              label="复制密码"
              onCopied={() => notify(`已复制主机密码：${summary.hostPassword}`)}
            />
          </div>
          {summary.fullHostPassword && summary.fullHostPassword !== summary.hostPassword && (
            <div className="flex items-center justify-between gap-2 flex-wrap pt-1.5 border-t border-separator">
              <span className="text-muted truncate">
                完整口令: <code className="font-mono text-indigo-400">{summary.fullHostPassword}</code>
              </span>
              <QuickCopyPill
                text={summary.fullHostPassword}
                label="复制规则串"
                onCopied={() => notify(`已复制规则口令：${summary.fullHostPassword}`)}
              />
            </div>
          )}
        </Surface>
      )}

      {hasPerm(user, 'bot:spawn') && summary.state === 'Lobby' && (
        <Surface variant="secondary" className="space-y-2 p-3 rounded-xl border border-border">
          <div className="text-xs font-semibold text-foreground">召唤内置 WindBot</div>
          <div className="flex flex-col sm:flex-row gap-2">
            <select
              value={selectedBotDeck}
              onChange={(e) => setSelectedBotDeck(e.target.value)}
              className="flex-1 min-w-0 h-8 px-2.5 text-xs input-modern input-modern-indigo"
            >
              <option value="random">随机 AI 卡组 (random)</option>
              {botDecks.map((d) => (
                <option key={d} value={d}>
                  {d}
                </option>
              ))}
            </select>
            <div className="flex gap-1.5 shrink-0">
              <Button size="sm" variant="primary" className="h-8 px-2.5 text-xs" onPress={() => spawnBot(summary.roomName, false)}>
                +1 AI
              </Button>
              <Button size="sm" variant="secondary" className="h-8 px-2.5 text-xs" onPress={() => spawnBot(summary.roomName, true)}>
                补满空席
              </Button>
            </div>
          </div>
        </Surface>
      )}

      {hasPerm(user, 'room:start_death') && (
        <Surface variant="secondary" className="flex items-center justify-between p-3 rounded-xl border border-border">
          <span className="font-medium text-foreground">额外回合猝死规则 (ED)</span>
          <Button
            size="sm"
            variant={summary.isSuddenDeath ? 'secondary' : 'danger-soft'}
            className="h-7 px-2.5 text-xs"
            onPress={() => toggleDeath(summary.roomName, !summary.isSuddenDeath)}
          >
            {summary.isSuddenDeath ? '取消 ED' : '开启 ED'}
          </Button>
        </Surface>
      )}

      <div className="space-y-1.5">
        <div className="font-semibold text-foreground">席位管理</div>
        {summary.slots.map((s: any) => (
          <Surface
            key={s.seatIndex}
            variant="secondary"
            className="flex items-center justify-between gap-2 px-3 py-2 rounded-lg border border-border"
          >
            <span className="truncate">
              <span className="font-mono text-muted mr-1">#{s.seatIndex}</span>
              <span className="text-foreground">{s.playerName || '<空>'}</span>{' '}
              {s.clientIp && <span className="font-mono text-[11px] text-muted">({s.clientIp})</span>}
            </span>
            {s.playerName && hasPerm(user, 'room:kick') && (
              <Button
                size="sm"
                variant="danger-soft"
                className="h-6 px-2 text-[11px] shrink-0"
                onPress={() => kickSlot(summary.roomName, s.seatIndex)}
              >
                踢出
              </Button>
            )}
          </Surface>
        ))}
      </div>

      <div className="space-y-1.5">
        <div className="font-semibold text-sky-400 flex items-center gap-1">
          <Eye className="w-3.5 h-3.5" /> 观战观众列表 ({(summary.observers || []).length} 人)
        </div>
        {(summary.observers || []).length > 0 ? (
          <div className="space-y-1 max-h-32 overflow-y-auto">
            {(summary.observers || []).map((obs: any, idx: number) => (
              <Surface
                key={`${obs.playerName}-${idx}`}
                variant="secondary"
                className="flex items-center justify-between gap-2 px-3 py-1.5 rounded-lg border border-border"
              >
                <span className="truncate">
                  {obs.playerName} {obs.clientIp ? `(${obs.clientIp})` : ''}
                </span>
                {hasPerm(user, 'room:kick') && (
                  <Button
                    size="sm"
                    variant="ghost"
                    className="h-6 px-2 text-[11px] text-rose-400 shrink-0"
                    onPress={() => kickByName(summary.roomName, obs.playerName)}
                  >
                    踢出观众
                  </Button>
                )}
              </Surface>
            ))}
          </div>
        ) : (
          <div className="text-[11px] text-muted bg-surface-secondary px-3 py-2 rounded-lg border border-border">
            当前暂无观战观众
          </div>
        )}
      </div>

      <div className="space-y-2">
        <div className="font-semibold text-foreground">公开事件与房间聊天</div>
        <div className="h-44 overflow-y-auto bg-[var(--console-bg)] rounded-xl border border-border p-3 space-y-1.5">
          {(selectedRoom.publicEvents || []).length === 0 && (selectedRoom.chats || []).length === 0 && (
            <div className="text-muted text-center py-8">暂无公开事件或聊天消息</div>
          )}
          {(selectedRoom.publicEvents || []).map((ev: any, idx: number) => (
            <div key={`ev-${idx}`} className="text-indigo-400 leading-relaxed font-mono text-[11px]">
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
                  : { text: '观战', cls: 'bg-surface-tertiary text-muted' };
            return (
              <div key={`ch-${idx}`} className="text-foreground flex items-baseline gap-1.5 flex-wrap leading-relaxed">
                <span className={`px-1.5 py-0.2 rounded text-[10px] font-semibold ${badge.cls}`}>{badge.text}</span>
                <span className="text-amber-400 font-medium">{c.senderName}:</span>
                <span>{c.message}</span>
              </div>
            );
          })}
        </div>
        {hasPerm(user, 'room:shout') && (
          <form onSubmit={sendRoomChat} className="flex gap-2">
            <Input
              variant="secondary"
              value={roomChatMsg}
              onChange={(e) => setRoomChatMsg(e.target.value)}
              placeholder="发送裁判黄字消息..."
              className="flex-1 text-xs"
            />
            <Button type="submit" size="sm" variant="primary" className="shrink-0">
              发送
            </Button>
          </form>
        )}
      </div>
    </div>
  );
}

export function RoomsView({ user, notify }: { user: AuthUser; notify: (t: string, k?: 'ok' | 'err') => void }) {
  const [sectionTab, setSectionTab] = useState<'live' | 'replays'>('live');
  const [rooms, setRooms] = useState<any[]>([]);
  const [roomSearch, setRoomSearch] = useState('');
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
        const rep = await apiFetch('/api/v1/replays?limit=30');
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

  const filteredRooms = useMemo(() => {
    if (!roomSearch.trim()) return rooms;
    const kw = roomSearch.trim().toLowerCase();
    return rooms.filter(
      (r) =>
        String(r.roomName || '').toLowerCase().includes(kw) ||
        (r.slots || []).some((s: any) => String(s.playerName || '').toLowerCase().includes(kw))
    );
  }, [rooms, roomSearch]);

  return (
    <div className="space-y-4 animate-fade-in">
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

      {/* 顶部二级 Tabs + 快捷建房操作 */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2.5">
        <Tabs
          selectedKey={sectionTab}
          onSelectionChange={(k) => setSectionTab(String(k) as 'live' | 'replays')}
        >
          <Tabs.ListContainer>
            <Tabs.List aria-label="裁判巡房与历史战报切换">
              <Tabs.Tab id="live" className="text-xs px-3 gap-1.5">
                <Swords className="w-3.5 h-3.5" />
                <span>实时房间 & 裁判巡房 ({rooms.length})</span>
                <Tabs.Indicator />
              </Tabs.Tab>
              <Tabs.Tab id="replays" className="text-xs px-3 gap-1.5">
                <Video className="w-3.5 h-3.5" />
                <span>历史战报 & 云录像 ({replays.length})</span>
                <Tabs.Indicator />
              </Tabs.Tab>
            </Tabs.List>
          </Tabs.ListContainer>
        </Tabs>

        <div className="flex items-center gap-2 shrink-0">
          {sectionTab === 'live' && (
            <SearchField
              aria-label="筛选活动房间"
              variant="secondary"
              value={roomSearch}
              onChange={setRoomSearch}
              className="w-40 sm:w-48"
            >
              <SearchField.Group>
                <SearchField.SearchIcon />
                <SearchField.Input placeholder="搜房名或选手..." className="text-xs" />
                <SearchField.ClearButton />
              </SearchField.Group>
            </SearchField>
          )}

          <Button size="sm" variant="secondary" onPress={loadRooms}>
            <RefreshCw className="w-3.5 h-3.5" />
            <span className="hidden sm:inline">刷新</span>
          </Button>

          {(hasPerm(user, 'room:create') || hasPerm(user, 'room:create_own')) && (
            <Button
              size="sm"
              variant="primary"
              onPress={() => {
                setSectionTab('live');
                setCreateOpen((v) => !v);
              }}
            >
              <Plus className="w-3.5 h-3.5" />
              <span>{createOpen ? '收起建房' : '新建房间'}</span>
            </Button>
          )}
        </div>
      </div>

      {sectionTab === 'live' && (
        <>
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

          <div className="grid grid-cols-1 lg:grid-cols-12 gap-4 items-start">
            <div className="lg:col-span-8 grid grid-cols-1 md:grid-cols-2 gap-3">
              {filteredRooms.length === 0 && (
                <Card className="md:col-span-2 rounded-xl p-10 text-center items-center space-y-2">
                  <Swords className="w-7 h-7 text-muted mx-auto" />
                  <Card.Title className="text-sm font-semibold">当前暂无匹配的活动房间</Card.Title>
                  <Card.Description className="text-xs">
                    点击右上角「新建房间」或从游戏客户端直接连入建房；所有后台巡房严格遵守战争迷雾保护。
                  </Card.Description>
                </Card>
              )}

              {filteredRooms.map((r) => {
                const observers: any[] = r.observers || [];
                const copyPw = r.hostPassword || r.joinPassword;
                const isSelected = selectedRoom?.summary?.roomName === r.roomName;
                const isTag = r.mode === 2;

                return (
                  <div
                    key={r.roomName}
                    onClick={() => loadRoomDetail(r.roomName, true)}
                    className="cursor-pointer transition active:scale-[0.996]"
                  >
                    <Card
                      className={`rounded-xl p-3.5 space-y-2.5 transition ${
                        isSelected ? 'border-accent ring-1 ring-accent/35' : 'hover:border-foreground/25'
                      }`}
                    >
                      <div className="flex items-center justify-between gap-2">
                        <span className="font-bold text-sm text-foreground truncate">{r.roomName}</span>
                        <div className="flex items-center gap-1 shrink-0 flex-wrap justify-end">
                          <Chip size="sm" variant="soft" color="accent" className="h-5 px-2 text-[10px]">
                            {isTag ? '2v2 Tag' : r.mode === 1 ? 'Match' : 'Single'}
                          </Chip>
                          {r.randomDeckMode !== 'None' && (
                            <Chip size="sm" variant="soft" className="h-5 px-2 text-[10px]">
                              {r.randomDeckMode}
                            </Chip>
                          )}
                          {r.isSuddenDeath && (
                            <Chip size="sm" variant="soft" color="danger" className="h-5 px-1.5 text-[10px]">
                              ED
                            </Chip>
                          )}
                          <Chip
                            size="sm"
                            variant="soft"
                            color={r.state === 'Lobby' ? 'warning' : 'success'}
                            className="h-5 px-2 text-[10px]"
                          >
                            {r.state}
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

                      {!user.isStreamerOnly && copyPw && (
                        <Surface variant="secondary" className="flex items-center justify-between gap-2 px-2.5 py-1.5 rounded-lg text-xs border border-border">
                          <span className="text-muted truncate">
                            密码: <code className="font-mono text-indigo-400 font-semibold">{copyPw}</code>
                          </span>
                          <QuickCopyPill text={copyPw} label="复制" onCopied={() => notify(`已复制主机密码：${copyPw}`)} />
                        </Surface>
                      )}

                      <div className="space-y-1 text-xs">
                        {r.slots.map((s: any) => (
                          <div key={s.seatIndex} className="flex items-center justify-between gap-2 text-foreground/90">
                            <span className="truncate">
                              <span className="font-mono text-muted mr-1">#{s.seatIndex}</span>
                              {s.playerName || '<空位>'}{' '}
                              {s.isHost && <span className="text-amber-400 text-[10px]">[房主]</span>}
                            </span>
                            <span className="text-[11px] text-muted shrink-0 font-mono">
                              {s.clientIp ? `${s.clientIp} · ` : ''}
                              {s.playerName ? (s.isReady ? '已准备' : s.seatOrigin) : ''}
                            </span>
                          </div>
                        ))}
                      </div>

                      <div className="pt-2 border-t border-separator flex items-center justify-between gap-2 text-[11px] text-muted">
                        <div className="flex items-center gap-1 truncate">
                          <Eye className="w-3.5 h-3.5 text-sky-400 shrink-0" />
                          <span>观众 ({r.observerCount ?? observers.length})：</span>
                          <span className="text-foreground/80 truncate">
                            {observers.length > 0 ? observers.map((o: any) => o.playerName).join('、') : '暂无观众'}
                          </span>
                        </div>
                        <span className="lg:hidden text-indigo-400 font-medium shrink-0">裁判面板 →</span>
                      </div>
                    </Card>
                  </div>
                );
              })}
            </div>

            {/* 桌面端右侧常驻裁判分栏 */}
            <Card className="hidden lg:flex lg:col-span-4 lg:sticky lg:top-4 rounded-xl p-4">
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
                <div className="text-xs text-muted py-14 text-center space-y-2 w-full">
                  <Swords className="w-7 h-7 text-muted/50 mx-auto" />
                  <div>点击左侧任意房间卡片</div>
                  <div className="text-[11px]">查看公开战况、召唤 WindBot 或执行裁判指令</div>
                </div>
              )}
            </Card>
          </div>
        </>
      )}

      {sectionTab === 'replays' && (
        <div className="space-y-4 animate-fade-in">
          <div ref={replayPanelRef}>
            {selectedReplayDetail && (
              <ReplayReviewPanel
                detail={selectedReplayDetail}
                onClose={() => setSelectedReplayDetail(null)}
                notify={notify}
              />
            )}
          </div>

          <Card className="rounded-xl p-4 sm:p-5 space-y-3.5">
            <h2 className="text-sm font-bold flex items-center gap-2">
              <Video className="w-4 h-4 text-sky-400 shrink-0" /> 历史对局战报与云录像 (客户端输入 R#编号 即可回放)
            </h2>

            {/* 手机端卡片列表 */}
            <div className="md:hidden space-y-2.5">
              {replays.map((r) => (
                <Surface
                  key={r.id}
                  variant="secondary"
                  className={`rounded-xl p-3.5 space-y-2 text-xs border ${
                    selectedReplayDetail?.duelId === r.id ? 'border-accent' : 'border-border'
                  }`}
                >
                  <div onClick={() => openReplayDetail(r.id)} className="space-y-1.5 cursor-pointer">
                    <div className="flex items-center justify-between gap-2">
                      <div className="flex items-center gap-2">
                        <span className="font-mono font-bold text-indigo-400 text-sm">{r.replayCode}</span>
                        <Chip size="sm" variant="soft" className="h-5 px-2 text-[10px]">
                          {r.mode === 2 ? '2v2 Tag' : r.mode === 1 ? 'Match' : 'Single'} · {r.turnCount}T
                        </Chip>
                      </div>
                      <span className="text-[11px] text-muted">{new Date(r.endedAt).toLocaleDateString()}</span>
                    </div>
                    <div className="text-foreground font-medium">
                      {(r.players || []).map((p: any) => `${p.playerName}${p.isWinner ? '(胜)' : ''}`).join(' vs ')}
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
                      {loadingReplayId === r.id ? '加载中...' : 'AI 复盘详情'}
                    </Button>
                    <QuickCopyPill text={r.replayCode} label="复制" onCopied={() => notify(`已复制 ${r.replayCode}`)} />
                    {r.hasYrp && hasPerm(user, 'replay:download') && (
                      <a
                        href={`/api/v1/replays/${r.id}.yrp`}
                        download
                        className="px-3 py-1.5 rounded-lg bg-surface-tertiary text-sky-400 font-medium border border-border"
                      >
                        .yrp
                      </a>
                    )}
                  </div>
                </Surface>
              ))}
              {replays.length === 0 && <div className="py-8 text-center text-xs text-muted">暂无历史战报记录</div>}
            </div>

            {/* 桌面端表格 */}
            <div className="hidden md:block overflow-x-auto">
              <table className="w-full text-xs text-left">
                <thead className="text-muted border-b border-separator">
                  <tr>
                    <th className="py-2.5">回放口令</th>
                    <th>房间</th>
                    <th>模式 / 随机</th>
                    <th>参战席位</th>
                    <th>回合数</th>
                    <th>时间</th>
                    <th className="text-right">录像</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-separator">
                  {replays.map((r) => (
                    <tr
                      key={r.id}
                      onClick={() => openReplayDetail(r.id)}
                      className={`cursor-pointer transition hover:bg-surface-secondary/60 ${
                        selectedReplayDetail?.duelId === r.id ? 'bg-surface-secondary' : ''
                      }`}
                    >
                      <td className="py-2.5 font-mono text-indigo-400" onClick={(e) => e.stopPropagation()}>
                        <QuickCopyPill text={r.replayCode} label={r.replayCode} onCopied={() => notify(`已复制 ${r.replayCode}`)} />
                      </td>
                      <td className="font-semibold text-foreground">{r.roomName}</td>
                      <td>
                        {r.mode === 2 ? '2v2 Tag' : r.mode === 1 ? 'Match' : 'Single'} · {r.randomDeckMode}
                      </td>
                      <td>
                        {(r.players || []).map((p: any) => `${p.playerName}${p.isWinner ? '(胜)' : ''}`).join(' vs ')}
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
                      <td colSpan={7} className="py-8 text-center text-muted">
                        暂无历史战报记录
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </Card>
        </div>
      )}
    </div>
  );
}
