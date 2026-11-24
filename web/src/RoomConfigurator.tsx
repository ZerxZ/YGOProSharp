import React, { useState, useMemo } from 'react';
import { Check, Copy, KeyRound, Plus, Sliders, Sparkles } from 'lucide-react';
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
  banlistChoice: string; // 'default' | 'none' | String(index)
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
}

const PRESETS: PresetItem[] = [
  {
    id: 'std_1v1',
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
    id: 'match_bo3',
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
    id: 'tag_2v2',
    label: '2v2 双打合作 (Tag)',
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
    id: 'rd_box',
    label: '盲盒预组随机 (RD)',
    desc: '免组卡准备，随机派发主题卡组',
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
    label: '镜像同卡组对决 (RDM)',
    desc: '双方/两队抽取完全对称卡组',
    apply: () => ({
      mode: 0,
      masterRule: 5,
      randomDeckMode: 'MirrorRandom',
      startLp: 8000,
      noCheckDeck: true
    })
  },
  {
    id: 'chaos_40',
    label: '平衡混沌抽卡 (CHAOS)',
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
    label: '1v1 AI 斗蛐蛐房',
    desc: '300秒长思考 + 镜像随机 + 2枚AI令牌',
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
    label: '2v2 AI 战队斗蛐蛐',
    desc: '双打16000LP + 镜像盲盒 + 4枚AI令牌',
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
    id: 'diy_fun',
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
    aiTokenCount: 2
  });

  const [submitting, setSubmitting] = useState(false);
  const [createdResult, setCreatedResult] = useState<any | null>(null);

  const accentBtn =
    theme === 'emerald'
      ? 'bg-emerald-600 hover:bg-emerald-500 text-white'
      : 'bg-indigo-600 hover:bg-indigo-500 text-white';
  const accentBorder = theme === 'emerald' ? 'border-emerald-500/40' : 'border-indigo-500/40';
  const accentText = theme === 'emerald' ? 'text-emerald-300' : 'text-indigo-300';

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
          aiTokenCount: Number(form.aiTokenCount)
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

  return (
    <div className={`bg-slate-900 border ${accentBorder} rounded-xl p-4 space-y-4 text-xs`}>
      <div className="flex items-center justify-between gap-2 flex-wrap">
        <div className="flex items-center gap-2 font-semibold text-sm">
          <Sliders className={`w-4 h-4 ${accentText}`} />
          <span>选择创建房间配置 & 快速复制主机密码</span>
        </div>
        {onClose && (
          <button
            type="button"
            onClick={onClose}
            className="px-2.5 py-1 rounded bg-slate-800 hover:bg-slate-700 text-slate-300 cursor-pointer"
          >
            收起配置器
          </button>
        )}
      </div>

      {/* 1. 快捷预设卡片 */}
      <div className="space-y-1.5">
        <div className="text-slate-400 font-medium flex items-center gap-1">
          <Sparkles className="w-3.5 h-3.5 text-amber-400" /> 快捷模式模板（点击一键填入参数，下方可继续微调）：
        </div>
        <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-2">
          {PRESETS.map((p) => (
            <button
              key={p.id}
              type="button"
              onClick={() => applyPreset(p)}
              className={`text-left p-2 rounded-lg border transition cursor-pointer ${
                activePreset === p.id
                  ? `${accentBorder} bg-slate-800/90 text-white`
                  : 'border-slate-800 bg-slate-950/70 text-slate-300 hover:border-slate-700'
              }`}
            >
              <div className="font-semibold truncate">{p.label}</div>
              <div className="text-[10px] text-slate-400 truncate mt-0.5">{p.desc}</div>
            </button>
          ))}
        </div>
      </div>

      {/* 2. 可视化详细参数配置 */}
      <form onSubmit={handleCreateOnServer} className="space-y-3">
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
          <div>
            <label className="block text-slate-400 mb-1">房间名称（留空自动生成）</label>
            <input
              value={form.roomName}
              onChange={(e) => updateField('roomName', e.target.value)}
              placeholder="如 Duel01（不含#号）"
              maxLength={24}
              className="w-full px-3 py-1.5 rounded bg-slate-950 border border-slate-700"
            />
          </div>

          <div>
            <label className="block text-slate-400 mb-1">对局模式 (Mode)</label>
            <select
              value={form.mode}
              onChange={(e) => updateField('mode', Number(e.target.value))}
              className="w-full px-3 py-1.5 rounded bg-slate-950 border border-slate-700"
            >
              <option value={0}>单局对决 (Single 1v1)</option>
              <option value={1}>比赛模式 (Match BO3)</option>
              <option value={2}>双打模式 (2v2 Tag Duel)</option>
            </select>
          </div>

          <div>
            <label className="block text-slate-400 mb-1">卡池范围 (Region)</label>
            <select
              value={form.region}
              onChange={(e) => updateField('region', Number(e.target.value))}
              className="w-full px-3 py-1.5 rounded bg-slate-950 border border-slate-700"
            >
              <option value={0}>OCG 卡池</option>
              <option value={1}>TCG 卡池</option>
              <option value={2}>OCG + TCG 简繁全卡池 (OT)</option>
              <option value={3}>开放本服 DIY 扩展卡 (DIY)</option>
            </select>
          </div>

          <div>
            <label className="block text-slate-400 mb-1">随机卡组玩法</label>
            <select
              value={form.randomDeckMode}
              onChange={(e) => updateField('randomDeckMode', e.target.value)}
              className="w-full px-3 py-1.5 rounded bg-slate-950 border border-slate-700"
            >
              <option value="None">不启用（玩家自备卡组）</option>
              <option value="PresetRandom">盲盒预组随机 (RD)</option>
              <option value="MirrorRandom">对称镜像同卡组 (RDM)</option>
              <option value="ChaosDraft">40张平衡混沌抽卡 (CHAOS)</option>
              <option value="Chaos60">60张割草混沌抽卡 (CHAOS60)</option>
              <option value="PureChaos">纯随机混沌无保底 (PURECHAOS)</option>
              <option value="DiyRandom">DIY 专属主题随机 (DIYRD)</option>
              <option value="DiyChaos">DIY 混沌大乱斗 (DIYCHAOS)</option>
            </select>
          </div>

          <div>
            <label className="block text-slate-400 mb-1">大师规则 (Master Rule)</label>
            <select
              value={form.masterRule}
              onChange={(e) => updateField('masterRule', Number(e.target.value))}
              className="w-full px-3 py-1.5 rounded bg-slate-950 border border-slate-700"
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
              className="w-full px-3 py-1.5 rounded bg-slate-950 border border-slate-700"
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
                    className="px-1.5 py-0.2 rounded bg-slate-800 hover:bg-slate-700 text-slate-300 cursor-pointer"
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
              className="w-full px-3 py-1.5 rounded bg-slate-950 border border-slate-700 font-mono"
            />
          </div>

          <div className="grid grid-cols-3 gap-1.5">
            <div>
              <label className="block text-slate-400 mb-1">限时(秒)</label>
              <select
                value={form.gameTimer}
                onChange={(e) => updateField('gameTimer', Number(e.target.value))}
                className="w-full px-2 py-1.5 rounded bg-slate-950 border border-slate-700 font-mono"
              >
                <option value={180}>180s</option>
                <option value={240}>240s</option>
                <option value={300}>300s</option>
                <option value={600}>600s</option>
              </select>
            </div>
            <div>
              <label className="block text-slate-400 mb-1">起手牌</label>
              <input
                type="number"
                min={0}
                max={40}
                step={1}
                value={form.startHand}
                onChange={(e) => updateField('startHand', e.target.value === '' ? '' : Number(e.target.value))}
                onBlur={() => {
                  if (form.startHand === '') updateField('startHand', 5);
                }}
                className="w-full px-2 py-1.5 rounded bg-slate-950 border border-slate-700 font-mono"
              />
            </div>
            <div>
              <label className="block text-slate-400 mb-1">抽卡数</label>
              <input
                type="number"
                min={0}
                max={35}
                step={1}
                value={form.drawCount}
                onChange={(e) => updateField('drawCount', e.target.value === '' ? '' : Number(e.target.value))}
                onBlur={() => {
                  if (form.drawCount === '') updateField('drawCount', 1);
                }}
                className="w-full px-2 py-1.5 rounded bg-slate-950 border border-slate-700 font-mono"
              />
            </div>
          </div>
        </div>

        {/* 3. 复选开关与 AI Token 联动 */}
        <div className="flex flex-wrap items-center justify-between gap-3 pt-1">
          <div className="flex flex-wrap items-center gap-4 text-slate-300">
            <label className="inline-flex items-center gap-1.5 cursor-pointer">
              <input
                type="checkbox"
                checked={form.noCheckDeck}
                onChange={(e) => updateField('noCheckDeck', e.target.checked)}
              />
              不检查卡组 (NC)
            </label>
            <label className="inline-flex items-center gap-1.5 cursor-pointer">
              <input
                type="checkbox"
                checked={form.noShuffleDeck}
                onChange={(e) => updateField('noShuffleDeck', e.target.checked)}
              />
              不洗切卡组 (NS)
            </label>
            <label className="inline-flex items-center gap-1.5 cursor-pointer">
              <input
                type="checkbox"
                checked={form.suddenDeath}
                onChange={(e) => updateField('suddenDeath', e.target.checked)}
              />
              额外回合猝死 (ED)
            </label>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <label
              className={`inline-flex items-center gap-1.5 cursor-pointer ${
                canCreateOnServer && canIssueAiTokens ? 'text-amber-300' : 'text-slate-400'
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
              />
              <KeyRound className="w-3.5 h-3.5" /> 同时签发本房专属 AI Token
            </label>
            {form.issuePlayerTokens && (
              <select
                value={form.aiTokenCount}
                onChange={(e) => updateField('aiTokenCount', Number(e.target.value))}
                className="px-2 py-1 rounded bg-slate-950 border border-amber-500/40 text-amber-200"
              >
                <option value={1}>1 个 (人机对战)</option>
                <option value={2}>2 个 (1v1 双AI斗蛐蛐)</option>
                <option value={3}>3 个 (1人类+3AI双打)</option>
                <option value={4}>4 个 (2v2 四AI战队赛)</option>
              </select>
            )}
          </div>
        </div>

        {/* 4. 实时主机密码预览与操作栏 */}
        <div className="bg-slate-950 border border-slate-800 rounded-xl p-3 flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-2 flex-wrap">
            <span className="text-slate-400">客户端主机密码预览：</span>
            <code className={`px-2.5 py-1 rounded bg-slate-900 border border-slate-700 font-mono font-bold text-sm ${accentText}`}>
              {previewHostPassword}
            </code>
            <QuickCopyPill text={previewHostPassword} label="快速复制主机密码" onCopied={() => notify(`已复制主机密码：${previewHostPassword}`)} />
          </div>

          <div className="flex items-center gap-2">
            {canCreateOnServer ? (
              <button
                type="submit"
                disabled={submitting}
                className={`px-4 py-2 rounded-lg font-semibold flex items-center gap-1.5 cursor-pointer disabled:opacity-50 ${accentBtn}`}
              >
                <Plus className="w-4 h-4" />
                {submitting ? '正在创建...' : '立即在服务器创建房间'}
              </button>
            ) : (
              <button
                type="button"
                onClick={onRequireAuth}
                className={`px-4 py-2 rounded-lg font-semibold cursor-pointer ${accentBtn}`}
              >
                登录 / 注册账号后一键在服务器建房 & 生成 AI Token
              </button>
            )}
          </div>
        </div>
      </form>

      {/* 5. 建房结果与签发的 AI Token */}
      {createdResult && (
        <div className="p-3.5 rounded-xl bg-emerald-950/50 border border-emerald-500/40 space-y-2.5">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="font-semibold text-emerald-200">
              房间 <span className="font-mono text-white">{createdResult.room?.roomName}</span> 已在服务器就绪！在游戏客户端「主机密码」框粘贴右侧口令即可进入：
            </div>
            <div className="flex flex-wrap items-center gap-2">
              {createdResult.hostPassword && (
                <QuickCopyPill
                  text={createdResult.hostPassword}
                  label={`复制房名口令: ${createdResult.hostPassword}`}
                  onCopied={() => notify(`已复制进房主机密码：${createdResult.hostPassword}`)}
                />
              )}
              {createdResult.fullHostPassword && createdResult.fullHostPassword !== createdResult.hostPassword && (
                <QuickCopyPill
                  text={createdResult.fullHostPassword}
                  label={`复制完整规则口令: ${createdResult.fullHostPassword}`}
                  onCopied={() => notify(`已复制完整主机密码：${createdResult.fullHostPassword}`)}
                />
              )}
            </div>
          </div>

          {Array.isArray(createdResult.issuedPlayerTokens) && createdResult.issuedPlayerTokens.length > 0 && (
            <div className="space-y-1.5 pt-2 border-t border-emerald-800/50">
              <div className="text-emerald-300 font-semibold">
                已自动生成房间专属外部 AI 对战 Token（仅展示一次！可直接复制「MCP 直连 URL」粘贴到外部客户端的 streamable-http URL 框）：
              </div>
              {createdResult.issuedPlayerTokens.map((t: any) => {
                const directMcpUrl = `${window.location.origin}/mcp?key=${encodeURIComponent(t.rawToken)}`;
                return (
                  <div
                    key={t.name}
                    className="flex flex-wrap items-center justify-between gap-2 bg-slate-950/90 px-3 py-2 rounded border border-slate-800 font-mono"
                  >
                    <div className="space-y-0.5 break-all">
                      <div>
                        <span className="text-amber-300 font-semibold">{t.name}</span> 直连 URL:{' '}
                        <span className="text-emerald-300">{directMcpUrl}</span>
                      </div>
                      <div className="text-[11px] text-slate-400">纯 Token: {t.rawToken}</div>
                    </div>
                    <div className="flex items-center gap-1.5">
                      <QuickCopyPill
                        text={directMcpUrl}
                        label="复制 MCP 直连 URL"
                        onCopied={() => notify(`已复制 ${t.name} 的 streamable-http 直连 URL`)}
                      />
                      <QuickCopyPill text={t.rawToken} label="复制纯 Token" onCopied={() => notify(`已复制 ${t.name} Token`)} />
                    </div>
                  </div>
                );
              })}
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
      className="inline-flex items-center gap-1 px-2.5 py-1 rounded-md bg-slate-800 hover:bg-slate-700 border border-slate-700 text-[11px] font-medium text-slate-200 transition cursor-pointer"
      title={`点击复制: ${text}`}
    >
      {copied ? <Check className="w-3 h-3 text-emerald-400" /> : <Copy className="w-3 h-3 text-emerald-400" />}
      <span>{copied ? '已复制' : label}</span>
    </button>
  );
}
