import React, { useState, useMemo } from 'react';
import { Check, ChevronDown, ChevronUp, Copy, KeyRound, Plus, Sliders, Sparkles, X } from 'lucide-react';
import { apiFetch } from './api';

export interface BanlistOption {
  index: number;
  name: string;
  whitelistOnly?: boolean;
}

export interface RoomConfiguratorProps {
  apiEndpoint: '/api/portal/rooms' | '/api/v1/rooms';
  theme?: 'emerald' | 'indigo';
  canCreateOnServer: boolean;
  canIssueAiTokens: boolean;
  banlists?: BanlistOption[];
  onRequireAuth?: () => void;
  onCreated?: (res: any) => void;
  onClose?: () => void;
  notify: (text: string, kind?: 'ok' | 'err') => void;
}

interface PresetItem {
  id: string;
  badge: string;
  label: string;
  desc: string;
  apply: () => Partial<RoomFormState>;
}

interface RoomFormState {
  roomName: string;
  mode: number;
  region: number;
  masterRule: number;
  randomDeckMode: string;
  banlistChoice: string;
  startLp: number | '';
  startHand: number | '';
  drawCount: number | '';
  gameTimer: number;
  noCheckDeck: boolean;
  noShuffleDeck: boolean;
  suddenDeath: boolean;
  customRulePrefix: string;
  issuePlayerTokens: boolean;
  aiTokenCount: number;
  aiPlayerNames: string[];
}

const PRESETS: PresetItem[] = [
  {
    id: 'std_1v1',
    badge: '经典',
    label: '标准 1v1 单局',
    desc: '常规 8000LP 构筑单挑',
    apply: () => ({
      mode: 0,
      region: 0,
      masterRule: 5,
      randomDeckMode: 'None',
      banlistChoice: 'default',
      startLp: 8000,
      startHand: 5,
      drawCount: 1,
      gameTimer: 240,
      noCheckDeck: false,
      noShuffleDeck: false,
      suddenDeath: false
    })
  },
  {
    id: 'rd_box',
    badge: '免组卡',
    label: '盲盒预组随机 (RD)',
    desc: '进房点准备即随机派发卡组',
    apply: () => ({
      mode: 0,
      masterRule: 5,
      randomDeckMode: 'PresetRandom',
      startLp: 8000,
      noCheckDeck: true
    })
  },
  {
    id: 'rdm_mirror',
    badge: '绝对公平',
    label: '镜像同卡组 (RDM)',
    desc: '双方/两队抽取对称卡组',
    apply: () => ({
      mode: 0,
      masterRule: 5,
      randomDeckMode: 'MirrorRandom',
      startLp: 8000,
      noCheckDeck: true
    })
  },
  {
    id: 'tag_2v2',
    badge: '卡片力量',
    label: '2v2 双打合作',
    desc: '4人双打 · 16000LP 共享场面',
    apply: () => ({
      mode: 2,
      region: 0,
      masterRule: 5,
      randomDeckMode: 'None',
      startLp: 16000,
      gameTimer: 240
    })
  },
  {
    id: 'chaos_40',
    badge: '大乱斗',
    label: '平衡混沌抽卡',
    desc: '全卡池黄金怪魔陷配比 40+15',
    apply: () => ({
      mode: 0,
      masterRule: 5,
      randomDeckMode: 'ChaosDraft',
      startLp: 8000,
      noCheckDeck: true
    })
  },
  {
    id: 'ai_cricket_1v1',
    badge: 'AI 互搏',
    label: '1v1 AI 斗蛐蛐房',
    desc: '300s思考 + 镜像随机 + 2枚令牌',
    apply: () => ({
      mode: 0,
      masterRule: 5,
      randomDeckMode: 'MirrorRandom',
      startLp: 8000,
      gameTimer: 300,
      noCheckDeck: true,
      issuePlayerTokens: true,
      aiTokenCount: 2
    })
  },
  {
    id: 'ai_cricket_2v2',
    badge: '2v2 战队',
    label: '2v2 AI 战队斗蛐蛐',
    desc: '16000LP + 镜像盲盒 + 4枚令牌',
    apply: () => ({
      mode: 2,
      masterRule: 5,
      randomDeckMode: 'MirrorRandom',
      startLp: 16000,
      gameTimer: 300,
      noCheckDeck: true,
      issuePlayerTokens: true,
      aiTokenCount: 4
    })
  },
  {
    id: 'match_bo3',
    badge: '竞技赛制',
    label: 'BO3 比赛局 (Match)',
    desc: '三局两胜标准备牌赛制',
    apply: () => ({
      mode: 1,
      region: 0,
      masterRule: 5,
      randomDeckMode: 'None',
      banlistChoice: 'default',
      startLp: 8000,
      gameTimer: 240,
      noCheckDeck: false
    })
  },
  {
    id: 'diy_fun',
    badge: '扩展包',
    label: 'DIY 新卡娱乐房',
    desc: '开放 DIY 扩展卡池 + 无禁限表',
    apply: () => ({
      mode: 0,
      region: 3,
      masterRule: 5,
      randomDeckMode: 'None',
      banlistChoice: 'none',
      startLp: 8000,
      noCheckDeck: true
    })
  }
];

export function RoomConfigurator({
  apiEndpoint,
  theme = 'emerald',
  canCreateOnServer,
  canIssueAiTokens,
  banlists = [],
  onRequireAuth,
  onCreated,
  onClose,
  notify
}: RoomConfiguratorProps) {
  const [activePreset, setActivePreset] = useState('std_1v1');
  const [showAdvanced, setShowAdvanced] = useState<boolean>(false);
  const [form, setForm] = useState<RoomFormState>({
    roomName: '',
    mode: 0,
    region: 0,
    masterRule: 5,
    randomDeckMode: 'None',
    banlistChoice: 'default',
    startLp: 8000,
    startHand: 5,
    drawCount: 1,
    gameTimer: 240,
    noCheckDeck: false,
    noShuffleDeck: false,
    suddenDeath: false,
    customRulePrefix: '',
    issuePlayerTokens: false,
    aiTokenCount: 2,
    aiPlayerNames: ['', '', '', '']
  });

  const [submitting, setSubmitting] = useState(false);
  const [createdResult, setCreatedResult] = useState<any | null>(null);

  const isEmerald = theme === 'emerald';
  const accentBtn = isEmerald
    ? 'bg-gradient-to-r from-emerald-600 to-teal-500 hover:from-emerald-500 hover:to-teal-400 text-white shadow-lg shadow-emerald-950/50'
    : 'bg-gradient-to-r from-indigo-600 to-violet-500 hover:from-indigo-500 hover:to-violet-400 text-white shadow-lg shadow-indigo-950/50';
  const accentBorder = isEmerald ? 'border-emerald-500/40' : 'border-indigo-500/40';
  const accentText = isEmerald ? 'text-emerald-300' : 'text-indigo-300';
  const inputCls = `w-full px-3 py-2 text-xs input-modern ${!isEmerald ? 'input-modern-indigo' : ''}`;

  const updateField = <K extends keyof RoomFormState>(key: K, val: RoomFormState[K]) => {
    setForm((prev) => {
      const next = { ...prev, [key]: val };
      if (key === 'mode') {
        if (val === 2 && prev.startLp === 8000) next.startLp = 16000;
        if (val !== 2 && prev.startLp === 16000) next.startLp = 8000;
        if (val === 2 && prev.aiTokenCount === 2) next.aiTokenCount = 4;
        if (val !== 2 && prev.aiTokenCount === 4) next.aiTokenCount = 2;
      }
      return next;
    });
  };

  const applyPreset = (p: PresetItem) => {
    setActivePreset(p.id);
    const patch = p.apply();
    setForm((prev) => ({
      ...prev,
      ...patch,
      issuePlayerTokens: canIssueAiTokens ? Boolean(patch.issuePlayerTokens ?? prev.issuePlayerTokens) : false
    }));
  };

  const previewHostPassword = useMemo(() => {
    const tokens: string[] = [];
    if (form.mode === 1) tokens.push('M');
    else if (form.mode === 2) tokens.push('2V2');

    if (form.region === 1) tokens.push('TCG');
    else if (form.region === 2) tokens.push('OT');
    else if (form.region === 3) tokens.push('DIY');

    if (form.masterRule !== 5) tokens.push(`MR${form.masterRule}`);

    switch (form.randomDeckMode) {
      case 'PresetRandom':
        tokens.push('RD');
        break;
      case 'MirrorRandom':
        tokens.push('RDM');
        break;
      case 'ChaosDraft':
        tokens.push('CHAOS');
        break;
      case 'Chaos60':
        tokens.push('CHAOS60');
        break;
      case 'PureChaos':
        tokens.push('PURECHAOS');
        break;
      case 'DiyRandom':
        tokens.push('DIYRD');
        break;
      case 'DiyChaos':
        tokens.push('DIYCHAOS');
        break;
    }

    if (form.banlistChoice === 'none') {
      tokens.push('NF');
    } else if (form.banlistChoice !== 'default') {
      const idx = Number(form.banlistChoice);
      if (!Number.isNaN(idx) && idx >= 0) tokens.push(`LF${idx + 1}`);
    }

    const defaultLp = form.mode === 2 ? 16000 : 8000;
    const resolvedLp = form.startLp === '' ? defaultLp : form.startLp;
    const resolvedHand = form.startHand === '' ? 5 : form.startHand;
    const resolvedDraw = form.drawCount === '' ? 1 : form.drawCount;

    if (resolvedLp !== defaultLp) tokens.push(`LP${resolvedLp}`);
    if (resolvedHand !== 5) tokens.push(`ST${resolvedHand}`);
    if (resolvedDraw !== 1) tokens.push(`DR${resolvedDraw}`);
    if (form.gameTimer !== 240) tokens.push(`TM${form.gameTimer}`);
    if (form.noCheckDeck) tokens.push('NC');
    if (form.noShuffleDeck) tokens.push('NS');
    if (form.suddenDeath) tokens.push('ED');
    if (form.customRulePrefix.trim()) tokens.push(form.customRulePrefix.trim());

    const targetRoom = form.roomName.trim() || 'Room01';
    return tokens.length === 0 ? targetRoom : `${tokens.join(',')}#${targetRoom}`;
  }, [form]);

  const handleCreateOnServer = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canCreateOnServer) {
      onRequireAuth?.();
      return;
    }

    const defaultLp = form.mode === 2 ? 16000 : 8000;
    const resolvedLp = Math.min(999999, Math.max(1, form.startLp === '' ? defaultLp : Math.trunc(Number(form.startLp))));
    const resolvedHand = Math.min(40, Math.max(0, form.startHand === '' ? 5 : Math.trunc(Number(form.startHand))));
    const resolvedDraw = Math.min(35, Math.max(0, form.drawCount === '' ? 1 : Math.trunc(Number(form.drawCount))));

    setSubmitting(true);
    try {
      const noBanlist = form.banlistChoice === 'none';
      const banlistIndex =
        form.banlistChoice !== 'default' && form.banlistChoice !== 'none' ? Number(form.banlistChoice) : undefined;

      const res = await apiFetch(apiEndpoint, {
        method: 'POST',
        body: JSON.stringify({
          roomName: form.roomName.trim() || undefined,
          rulePrefix: form.customRulePrefix.trim() || undefined,
          mode: form.mode,
          region: form.region,
          masterRule: form.masterRule,
          banlistIndex,
          noBanlist,
          startLp: resolvedLp,
          startHand: resolvedHand,
          drawCount: resolvedDraw,
          gameTimer: Number(form.gameTimer),
          randomDeckMode: form.randomDeckMode,
          noCheckDeck: form.noCheckDeck,
          noShuffleDeck: form.noShuffleDeck,
          suddenDeath: form.suddenDeath,
          issuePlayerTokens: canIssueAiTokens && form.issuePlayerTokens,
          aiTokenCount: Number(form.aiTokenCount),
          aiPlayerNames:
            canIssueAiTokens && form.issuePlayerTokens
              ? form.aiPlayerNames.slice(0, Number(form.aiTokenCount)).map((n) => n.trim())
              : undefined
        })
      });

      setCreatedResult(res);
      notify(`房间 ${res.room?.roomName || ''} 已创建！可直接复制主机密码进入游戏。`);
      onCreated?.(res);
    } catch (err) {
      notify(err instanceof Error ? err.message : '创建房间失败', 'err');
    } finally {
      setSubmitting(false);
    }
  };

  const resolvedSummaryLp = form.startLp === '' ? (form.mode === 2 ? 16000 : 8000) : form.startLp;

  return (
    <div className={`glass-card border ${accentBorder} rounded-2xl p-4 sm:p-5 space-y-4 text-xs animate-fade-in`}>
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2.5 min-w-0">
          <div
            className={`w-8 h-8 rounded-xl flex items-center justify-center shrink-0 ${
              isEmerald ? 'bg-emerald-500/15 text-emerald-300' : 'bg-indigo-500/15 text-indigo-300'
            }`}
          >
            <Sliders className="w-4 h-4" />
          </div>
          <div className="min-w-0">
            <div className="font-bold text-sm text-slate-100 truncate">快速创建房间 & 生成主机密码</div>
            <div className="text-[11px] text-slate-400 truncate hidden sm:block">
              点选玩法模板即可一键开房，也可直接复制口令粘贴到 MDPro3 / YGOPro 客户端建房
            </div>
          </div>
        </div>
        {onClose && (
          <button
            type="button"
            onClick={onClose}
            className="p-2 sm:px-3 sm:py-1.5 rounded-xl bg-slate-800/80 hover:bg-slate-700 text-slate-300 flex items-center gap-1 shrink-0 cursor-pointer"
          >
            <X className="w-3.5 h-3.5" />
            <span className="hidden sm:inline">收起</span>
          </button>
        )}
      </div>

      {/* 1. 玩法模板卡片（手机端支持顺滑左右滑动，桌面端自适应网格） */}
      <div className="space-y-2">
        <div className="text-slate-300 font-medium flex items-center justify-between">
          <span className="flex items-center gap-1.5">
            <Sparkles className="w-3.5 h-3.5 text-amber-400" /> 选择玩法模式
          </span>
          <span className="text-[11px] text-slate-500 sm:hidden">左右滑动更多 →</span>
        </div>
        <div className="flex sm:grid sm:grid-cols-3 lg:grid-cols-5 gap-2 overflow-x-auto no-scrollbar snap-x pb-1">
          {PRESETS.map((p) => {
            const selected = activePreset === p.id;
            return (
              <button
                key={p.id}
                type="button"
                onClick={() => applyPreset(p)}
                className={`snap-start shrink-0 w-44 sm:w-auto text-left p-2.5 rounded-xl border transition cursor-pointer ${
                  selected
                    ? `${accentBorder} bg-slate-800/95 text-white shadow-md`
                    : 'border-slate-800/90 bg-slate-950/60 text-slate-300 hover:border-slate-700 hover:bg-slate-900/70'
                }`}
              >
                <div className="flex items-center justify-between gap-1 mb-1">
                  <span
                    className={`px-1.5 py-0.5 rounded text-[10px] font-semibold ${
                      selected
                        ? isEmerald
                          ? 'bg-emerald-500/25 text-emerald-200'
                          : 'bg-indigo-500/25 text-indigo-200'
                        : 'bg-slate-800 text-slate-400'
                    }`}
                  >
                    {p.badge}
                  </span>
                  {selected && <span className={`w-2 h-2 rounded-full ${isEmerald ? 'bg-emerald-400' : 'bg-indigo-400'}`} />}
                </div>
                <div className="font-semibold text-xs truncate">{p.label}</div>
                <div className="text-[11px] text-slate-400 truncate mt-0.5">{p.desc}</div>
              </button>
            );
          })}
        </div>
      </div>

      {/* 2. 核心高频项（始终展示，清爽不拥挤） */}
      <form onSubmit={handleCreateOnServer} className="space-y-3">
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
          <div>
            <label className="block text-slate-400 mb-1">房间名称（选填，留空自动分配）</label>
            <input
              value={form.roomName}
              onChange={(e) => updateField('roomName', e.target.value)}
              placeholder="如 Duel01（不含 # 号）"
              maxLength={24}
              className={inputCls}
            />
          </div>

          <div>
            <label className="block text-slate-400 mb-1">对局人数与赛制</label>
            <select
              value={form.mode}
              onChange={(e) => updateField('mode', Number(e.target.value))}
              className={inputCls}
            >
              <option value={0}>单局对决 (Single 1v1)</option>
              <option value={1}>三局两胜比赛 (Match BO3)</option>
              <option value={2}>四人双打合作 (2v2 Tag Duel)</option>
            </select>
          </div>

          <div>
            <label className="block text-slate-400 mb-1">随机卡组玩法</label>
            <select
              value={form.randomDeckMode}
              onChange={(e) => updateField('randomDeckMode', e.target.value)}
              className={inputCls}
            >
              <option value="None">玩家自备构筑卡组</option>
              <option value="PresetRandom">盲盒预组随机 (RD)</option>
              <option value="MirrorRandom">对称镜像同卡组 (RDM)</option>
              <option value="ChaosDraft">40张平衡混沌抽卡 (CHAOS)</option>
              <option value="Chaos60">60张割草混沌抽卡 (CHAOS60)</option>
              <option value="PureChaos">纯随机混沌无保底 (PURECHAOS)</option>
              <option value="DiyRandom">DIY 专属主题随机 (DIYRD)</option>
              <option value="DiyChaos">DIY 混沌大乱斗 (DIYCHAOS)</option>
            </select>
          </div>
        </div>

        {/* AI 陪练 / 斗蛐蛐令牌快捷开关 */}
        <div className="glass-subcard rounded-xl px-3.5 py-2.5 flex flex-wrap items-center justify-between gap-2.5">
          <div className="flex flex-wrap items-center gap-3">
            <label
              className={`inline-flex items-center gap-2 font-medium cursor-pointer ${
                canCreateOnServer && canIssueAiTokens ? 'text-amber-300' : 'text-slate-300'
              }`}
            >
              <input
                type="checkbox"
                checked={form.issuePlayerTokens}
                onChange={(e) => {
                  if (!canCreateOnServer) {
                    onRequireAuth?.();
                    return;
                  }
                  updateField('issuePlayerTokens', e.target.checked);
                }}
                className="w-4 h-4 rounded accent-emerald-500"
              />
              <KeyRound className="w-3.5 h-3.5 text-amber-400" />
              <span>顺带签发本房专属外部 AI 决斗 Token</span>
            </label>

            {form.issuePlayerTokens && (
              <select
                value={form.aiTokenCount}
                onChange={(e) => updateField('aiTokenCount', Number(e.target.value))}
                className="px-2.5 py-1 rounded-lg bg-slate-900 border border-amber-500/40 text-amber-200 text-xs"
              >
                <option value={1}>1 枚 (人机对战/双排搭档)</option>
                <option value={2}>2 枚 (1v1 双 AI 斗蛐蛐)</option>
                <option value={3}>3 枚 (1 人类 + 3 AI 双打)</option>
                <option value={4}>4 枚 (2v2 四 AI 战队赛)</option>
              </select>
            )}
          </div>

          <button
            type="button"
            onClick={() => setShowAdvanced((v) => !v)}
            className="inline-flex items-center gap-1.5 text-slate-300 hover:text-white px-2.5 py-1 rounded-lg bg-slate-900/90 border border-slate-800 cursor-pointer"
          >
            <span>高级规则微调</span>
            <span className="font-mono text-[10px] text-slate-400">
              (MR{form.masterRule} · {resolvedSummaryLp}LP · {form.gameTimer}s)
            </span>
            {showAdvanced ? <ChevronUp className="w-3.5 h-3.5" /> : <ChevronDown className="w-3.5 h-3.5" />}
          </button>
        </div>

        {form.issuePlayerTokens && (
          <div className="bg-amber-950/20 border border-amber-500/30 rounded-xl p-3.5 space-y-2.5 animate-fade-in">
            <div className="text-[11px] text-amber-200/90">
              可提前为 AI 指定帅气昵称；留空则由外部 AI 首次进房时自主取名（自动拦截 AI1/Bot 等生硬编号）：
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-2">
              {Array.from({ length: form.aiTokenCount }).map((_, idx) => (
                <div key={idx}>
                  <label className="block text-[11px] text-slate-400 mb-1">第 {idx + 1} 席 AI 昵称</label>
                  <input
                    value={form.aiPlayerNames[idx] || ''}
                    onChange={(e) => {
                      const nextNames = [...form.aiPlayerNames];
                      nextNames[idx] = e.target.value;
                      updateField('aiPlayerNames', nextNames);
                    }}
                    maxLength={18}
                    placeholder="留空交由 AI 自取名"
                    className={inputCls}
                  />
                </div>
              ))}
            </div>
          </div>
        )}

        {/* 3. 渐进式展开的高级规则微调（手机端默认收起，保持首屏清爽） */}
        {showAdvanced && (
          <div className="glass-subcard rounded-xl p-3.5 space-y-3 animate-fade-in">
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
              <div>
                <label className="block text-slate-400 mb-1">卡池范围 (Region)</label>
                <select
                  value={form.region}
                  onChange={(e) => updateField('region', Number(e.target.value))}
                  className={inputCls}
                >
                  <option value={0}>OCG 卡池</option>
                  <option value={1}>TCG 卡池</option>
                  <option value={2}>OCG + TCG 全卡池 (OT)</option>
                  <option value={3}>开放本服 DIY 扩展卡 (DIY)</option>
                </select>
              </div>

              <div>
                <label className="block text-slate-400 mb-1">大师规则 (Master Rule)</label>
                <select
                  value={form.masterRule}
                  onChange={(e) => updateField('masterRule', Number(e.target.value))}
                  className={inputCls}
                >
                  <option value={5}>大师规则 2020 (MR5 现行)</option>
                  <option value={4}>新大师规则 (MR4 连接格限制)</option>
                  <option value={3}>大师规则 3 (MR3 灵摆时代)</option>
                  <option value={2}>大师规则 2 (MR2)</option>
                  <option value={1}>大师规则 1 (MR1 初代先攻抽卡)</option>
                </select>
              </div>

              <div>
                <label className="block text-slate-400 mb-1">禁限卡表 (Banlist)</label>
                <select
                  value={form.banlistChoice}
                  onChange={(e) => updateField('banlistChoice', e.target.value)}
                  className={inputCls}
                >
                  <option value="default">服务器默认最新禁限表</option>
                  <option value="none">无禁限卡表 (NF)</option>
                  {banlists.map((b) => (
                    <option key={b.index} value={String(b.index)}>
                      {b.name} {b.whitelistOnly ? '(白名单)' : ''}
                    </option>
                  ))}
                </select>
              </div>

              <div>
                <div className="flex items-center justify-between mb-1">
                  <label className="text-slate-400">初始基本分 (LP)</label>
                  <span className="space-x-1 text-[10px]">
                    {[4000, 8000, 16000].map((v) => (
                      <button
                        key={v}
                        type="button"
                        onClick={() => updateField('startLp', v)}
                        className="px-1.5 py-0.5 rounded bg-slate-800 hover:bg-slate-700 text-slate-300 cursor-pointer"
                      >
                        {v}
                      </button>
                    ))}
                  </span>
                </div>
                <input
                  type="number"
                  min={1}
                  max={999999}
                  step={1}
                  value={form.startLp}
                  onChange={(e) => updateField('startLp', e.target.value === '' ? '' : Number(e.target.value))}
                  onBlur={() => {
                    if (form.startLp === '' || Number(form.startLp) < 1) {
                      updateField('startLp', form.mode === 2 ? 16000 : 8000);
                    }
                  }}
                  className={`${inputCls} font-mono`}
                />
              </div>
            </div>

            <div className="grid grid-cols-3 sm:grid-cols-6 gap-2.5 items-end">
              <div>
                <label className="block text-slate-400 mb-1">回合限时</label>
                <select
                  value={form.gameTimer}
                  onChange={(e) => updateField('gameTimer', Number(e.target.value))}
                  className={`${inputCls} font-mono`}
                >
                  <option value={180}>180s</option>
                  <option value={240}>240s</option>
                  <option value={300}>300s</option>
                  <option value={600}>600s</option>
                </select>
              </div>
              <div>
                <label className="block text-slate-400 mb-1">起手张数</label>
                <input
                  type="number"
                  min={0}
                  max={40}
                  value={form.startHand}
                  onChange={(e) => updateField('startHand', e.target.value === '' ? '' : Number(e.target.value))}
                  onBlur={() => {
                    if (form.startHand === '') updateField('startHand', 5);
                  }}
                  className={`${inputCls} font-mono`}
                />
              </div>
              <div>
                <label className="block text-slate-400 mb-1">每回抽卡</label>
                <input
                  type="number"
                  min={0}
                  max={35}
                  value={form.drawCount}
                  onChange={(e) => updateField('drawCount', e.target.value === '' ? '' : Number(e.target.value))}
                  onBlur={() => {
                    if (form.drawCount === '') updateField('drawCount', 1);
                  }}
                  className={`${inputCls} font-mono`}
                />
              </div>
              <div className="col-span-3 flex flex-wrap items-center gap-3 py-1.5">
                <label className="inline-flex items-center gap-1.5 text-slate-300 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={form.noCheckDeck}
                    onChange={(e) => updateField('noCheckDeck', e.target.checked)}
                    className="w-4 h-4 rounded accent-emerald-500"
                  />
                  免检卡组 (NC)
                </label>
                <label className="inline-flex items-center gap-1.5 text-slate-300 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={form.noShuffleDeck}
                    onChange={(e) => updateField('noShuffleDeck', e.target.checked)}
                    className="w-4 h-4 rounded accent-emerald-500"
                  />
                  不洗牌 (NS)
                </label>
                <label className="inline-flex items-center gap-1.5 text-slate-300 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={form.suddenDeath}
                    onChange={(e) => updateField('suddenDeath', e.target.checked)}
                    className="w-4 h-4 rounded accent-emerald-500"
                  />
                  额外回合猝死 (ED)
                </label>
              </div>
            </div>
          </div>
        )}

        {/* 4. 实时主机密码预览与一键建房主按钮 */}
        <div className="glass-subcard rounded-2xl p-3 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
          <div className="flex items-center justify-between sm:justify-start gap-2 min-w-0">
            <div className="min-w-0">
              <div className="text-[11px] text-slate-400">客户端主机密码预览</div>
              <code className={`block font-mono font-bold text-sm truncate ${accentText}`}>{previewHostPassword}</code>
            </div>
            <QuickCopyPill
              text={previewHostPassword}
              label="复制口令"
              onCopied={() => notify(`已复制主机密码：${previewHostPassword}`)}
            />
          </div>

          <div className="w-full sm:w-auto">
            {canCreateOnServer ? (
              <button
                type="submit"
                disabled={submitting}
                className={`w-full sm:w-auto min-h-[40px] px-5 py-2.5 rounded-xl font-semibold flex items-center justify-center gap-1.5 transition active:scale-[0.99] cursor-pointer disabled:opacity-50 ${accentBtn}`}
              >
                <Plus className="w-4 h-4" />
                {submitting ? '正在创建...' : '立即在服务器创建房间'}
              </button>
            ) : (
              <button
                type="button"
                onClick={onRequireAuth}
                className={`w-full sm:w-auto min-h-[40px] px-5 py-2.5 rounded-xl font-semibold transition active:scale-[0.99] cursor-pointer ${accentBtn}`}
              >
                登录 / 免费注册后一键建房 & 领 AI 令牌
              </button>
            )}
          </div>
        </div>
      </form>

      {/* 5. 建房结果回执卡片 */}
      {createdResult && (
        <div className="p-4 rounded-2xl bg-emerald-950/45 border border-emerald-500/40 space-y-3 animate-fade-in">
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2.5">
            <div className="font-semibold text-emerald-200 leading-relaxed">
              ✓ 房间 <span className="font-mono text-white px-1.5 py-0.5 rounded bg-slate-900">{createdResult.room?.roomName}</span> 已在服务器就绪！打开游戏粘贴口令即可进房：
            </div>
            <div className="flex flex-wrap items-center gap-2">
              {createdResult.hostPassword && (
                <QuickCopyPill
                  text={createdResult.hostPassword}
                  label={`复制房名: ${createdResult.hostPassword}`}
                  onCopied={() => notify(`已复制进房主机密码：${createdResult.hostPassword}`)}
                />
              )}
              {createdResult.fullHostPassword && createdResult.fullHostPassword !== createdResult.hostPassword && (
                <QuickCopyPill
                  text={createdResult.fullHostPassword}
                  label="复制完整规则口令"
                  onCopied={() => notify(`已复制完整主机密码：${createdResult.fullHostPassword}`)}
                />
              )}
            </div>
          </div>

          {Array.isArray(createdResult.issuedPlayerTokens) && createdResult.issuedPlayerTokens.length > 0 && (
            <div className="space-y-2 pt-2.5 border-t border-emerald-800/50">
              <div className="text-emerald-300 font-semibold">
                已签发本房专属 AI 令牌（仅展示一次！复制「MCP 直连 URL」粘贴到外部 AI 客户端即可上桌）：
              </div>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-2">
                {createdResult.issuedPlayerTokens.map((t: any, idx: number) => {
                  const directMcpUrl = `${window.location.origin}/mcp?key=${encodeURIComponent(t.rawToken)}`;
                  const nickBadge = t.preferredPlayerName ? `昵称: ${t.preferredPlayerName}` : '待 AI 进房自取名';
                  return (
                    <div
                      key={t.rawToken || `${t.name}-${idx}`}
                      className="bg-slate-950/90 p-3 rounded-xl border border-slate-800 space-y-2 font-mono"
                    >
                      <div className="flex items-center justify-between gap-2 flex-wrap">
                        <span className="text-amber-300 font-semibold font-sans">席位令牌 #{idx + 1}</span>
                        <span className="px-2 py-0.5 rounded-full text-[10px] font-sans bg-amber-500/15 border border-amber-500/30 text-amber-200">
                          {nickBadge}
                        </span>
                      </div>
                      <div className="text-[11px] text-emerald-300 break-all bg-slate-900/90 p-2 rounded-lg border border-slate-800">
                        {directMcpUrl}
                      </div>
                      <div className="flex items-center gap-1.5 flex-wrap">
                        <QuickCopyPill
                          text={directMcpUrl}
                          label="复制 MCP 直连 URL"
                          onCopied={() => notify(`已复制令牌 #${idx + 1} 的直连 URL`)}
                        />
                        <QuickCopyPill
                          text={t.rawToken}
                          label="复制纯 Token"
                          onCopied={() => notify(`已复制令牌 #${idx + 1} 纯 Token`)}
                        />
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

export function QuickCopyPill({
  text,
  label = '复制主机密码',
  onCopied
}: {
  text: string;
  label?: string;
  onCopied?: () => void;
}) {
  const [copied, setCopied] = useState(false);
  return (
    <button
      type="button"
      onClick={(e) => {
        e.stopPropagation();
        navigator.clipboard?.writeText(text);
        setCopied(true);
        onCopied?.();
        setTimeout(() => setCopied(false), 1400);
      }}
      className={`inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg border text-[11px] font-medium transition active:scale-95 shrink-0 cursor-pointer ${
        copied
          ? 'bg-emerald-500/20 border-emerald-500/50 text-emerald-200'
          : 'bg-slate-800/90 hover:bg-slate-700/90 border-slate-700/80 text-slate-200'
      }`}
      title={`点击复制: ${text}`}
    >
      {copied ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5 text-emerald-400" />}
      <span>{copied ? '已复制' : label}</span>
    </button>
  );
}
