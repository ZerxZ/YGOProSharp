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
  Search
} from 'lucide-react';
import { apiFetch } from './api';

type PortalTab = 'lobby' | 'replays' | 'catalog' | 'selfservice';

export interface SurfaceInfo {
  surface: 'admin' | 'portal';
  gamePort: number;
  adminPort?: number;
  portalPort?: number;
  publicGameHost?: string;
}

export function UserPortalApp({ surfaceInfo }: { surfaceInfo: SurfaceInfo }) {
  const [tab, setTab] = useState<PortalTab>('lobby');
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

  useEffect(() => {
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
  }, [loadLobby]);

  const openRoomDetail = async (roomName: string) => {
    try {
      const res = await apiFetch(`/api/portal/rooms/${encodeURIComponent(roomName)}`);
      setSelectedRoom(res.room);
    } catch (err) {
      notify(err instanceof Error ? err.message : '房间已结束', 'err');
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
          authorName: propAuthor,
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
        <div className="max-w-7xl mx-auto px-4 h-14 flex items-center justify-between gap-4">
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
              <div className="text-[11px] text-slate-400">公开房间围观 · 云录像回放 · DIY 扩展下载 · 赛事交牌</div>
            </div>
          </div>

          <nav className="flex items-center gap-1 overflow-x-auto">
            {(
              [
                ['lobby', '实时大厅 & 联机口令', <Gamepad2 className="w-4 h-4" />],
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

          <div className="hidden md:flex items-center gap-2 text-xs bg-slate-950 px-3 py-1.5 rounded-lg border border-slate-800">
            <span className="text-slate-400">联机地址:</span>
            <span className="font-mono text-emerald-300 font-semibold">
              {gameHost}:{gamePort}
            </span>
            <SmallCopy text={`${gameHost}:${gamePort}`} />
          </div>
        </div>
      </header>

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

            {overview?.ruleGuides && (
              <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-3">
                <div className="text-xs font-semibold text-emerald-300">客户端建房口令速查（复制后粘贴到游戏「主机密码」即可）</div>
                <div className="grid grid-cols-1 md:grid-cols-3 lg:grid-cols-4 gap-2.5">
                  {overview.ruleGuides.map((g: any) => (
                    <div key={g.prefix} className="bg-slate-950 border border-slate-800 rounded-lg p-2.5 text-xs space-y-1">
                      <div className="flex items-center justify-between">
                        <span className="font-mono font-bold text-emerald-300">{g.prefix}</span>
                        <SmallCopy text={g.prefix} />
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
                    <Swords className="w-4 h-4 text-emerald-400" /> 实时公开房间大厅（已自动保护玩家隐私 IP 与暗牌）
                  </h2>
                  <button onClick={loadLobby} className="px-2.5 py-1 rounded bg-slate-800 text-xs cursor-pointer">
                    刷新
                  </button>
                </div>

                {rooms.length === 0 && (
                  <div className="bg-slate-900 border border-slate-800 rounded-xl p-8 text-center text-sm text-slate-400">
                    当前暂无活动房间，打开游戏客户端连入 <span className="font-mono text-emerald-300">{gameHost}:{gamePort}</span> 输入房号即可创建！
                  </div>
                )}

                <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                  {rooms.map((r) => (
                    <div
                      key={r.roomName}
                      onClick={() => openRoomDetail(r.roomName)}
                      className="bg-slate-900 border border-slate-800 hover:border-emerald-500/50 rounded-xl p-4 space-y-2.5 cursor-pointer transition"
                    >
                      <div className="flex items-center justify-between">
                        <span className="font-bold text-sm">{r.roomName}</span>
                        <span className="px-2 py-0.5 rounded text-[11px] bg-emerald-500/20 text-emerald-300">
                          {r.mode === 2 ? '2v2 Tag' : r.mode === 1 ? 'Match' : 'Single'} · {r.state}
                        </span>
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
                              #{s.seatIndex} {s.playerName || '<等待加入>'}
                            </span>
                            <span className="text-slate-500">{s.playerName ? (s.isReady ? '已准备' : '在席') : ''}</span>
                          </div>
                        ))}
                      </div>
                    </div>
                  ))}
                </div>
              </div>

              <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-3">
                <h3 className="text-sm font-semibold">公开观战实况</h3>
                {selectedRoom ? (
                  <div className="space-y-2 text-xs">
                    <div className="font-bold text-emerald-300">{selectedRoom.summary.roomName}</div>
                    <div className="h-64 overflow-y-auto bg-slate-950 border border-slate-800 rounded p-2.5 space-y-1">
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
                ) : (
                  <div className="text-xs text-slate-500 py-12 text-center">点击左侧任意房间查看公开对局事件与聊天</div>
                )}
              </div>
            </div>
          </>
        )}

        {tab === 'replays' && (
          <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-3">
            <h2 className="text-sm font-semibold">全服历史对局战报与云录像（游戏客户端输入 R#编号 直接观看回放）</h2>
            <table className="w-full text-xs text-left">
              <thead className="text-slate-400 border-b border-slate-800">
                <tr>
                  <th className="py-2">云回放口令</th>
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
                    <td className="py-2 font-mono text-emerald-300 flex items-center gap-1">
                      {r.replayCode} <SmallCopy text={r.replayCode} />
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
                      <SmallCopy text={chaosDeck.ydk} />
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
              <p className="text-slate-400">
                首次绑定后，他人若不携带正确口令将无法使用你的昵称进入服务器。
              </p>
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
                  placeholder="投稿人署名"
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

function StatBox({ title, value, sub }: { title: string; value: string; sub: string }) {
  return (
    <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-1">
      <div className="text-xs text-slate-400">{title}</div>
      <div className="text-lg font-bold text-emerald-300 font-mono">{value}</div>
      <div className="text-[11px] text-slate-500">{sub}</div>
    </div>
  );
}

function SmallCopy({ text }: { text: string }) {
  const [ok, setOk] = useState(false);
  return (
    <button
      type="button"
      onClick={() => {
        navigator.clipboard?.writeText(text);
        setOk(true);
        setTimeout(() => setOk(false), 1200);
      }}
      className="p-1 rounded hover:bg-slate-800 text-slate-400 hover:text-white cursor-pointer"
      title="复制"
    >
      {ok ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
    </button>
  );
}
