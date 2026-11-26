import { useCallback, useEffect, useState } from 'react';
import { Sparkles } from 'lucide-react';
import { apiFetch } from '../api';
import { QuickCopyPill } from '../RoomConfigurator';

const SIX_DIM_AXES: { key: string; label: string }[] = [
  { key: 'burst_power', label: '爆发强度' },
  { key: 'resilience', label: '续航韧性' },
  { key: 'stun_toxicity', label: '阴间指数' },
  { key: 'fun_factor', label: '娱乐整活' },
  { key: 'complexity', label: '操作复杂' },
  { key: 'meta_adaptability', label: '环境适应' }
];

function SixDimRadarSvg({ dimensions }: { dimensions: Record<string, number> | null | undefined }) {
  const cx = 145;
  const cy = 135;
  const radius = 88;
  const levels = [25, 50, 75, 100];

  const pointAt = (axisIdx: number, val0To100: number) => {
    const angle = -Math.PI / 2 + (axisIdx * 2 * Math.PI) / 6;
    const r = (Math.max(0, Math.min(100, val0To100)) / 100) * radius;
    return {
      x: cx + r * Math.cos(angle),
      y: cy + r * Math.sin(angle)
    };
  };

  const dataValues = SIX_DIM_AXES.map((a) => Number(dimensions?.[a.key] ?? 50));
  const dataPoints = dataValues.map((v, i) => {
    const p = pointAt(i, v);
    return `${p.x.toFixed(1)},${p.y.toFixed(1)}`;
  });

  return (
    <svg viewBox="0 0 290 270" className="w-full max-w-[290px] mx-auto select-none">
      {levels.map((lv) => {
        const ring = SIX_DIM_AXES.map((_, i) => {
          const p = pointAt(i, lv);
          return `${p.x.toFixed(1)},${p.y.toFixed(1)}`;
        }).join(' ');
        return (
          <polygon
            key={lv}
            points={ring}
            fill="none"
            stroke={lv === 100 ? '#334155' : '#1e293b'}
            strokeWidth={lv === 100 ? 1.4 : 1}
          />
        );
      })}

      {SIX_DIM_AXES.map((axis, i) => {
        const outer = pointAt(i, 100);
        return <line key={axis.key} x1={cx} y1={cy} x2={outer.x} y2={outer.y} stroke="#334155" strokeWidth="1" />;
      })}

      <polygon
        points={dataPoints.join(' ')}
        fill={dimensions ? 'rgba(16, 185, 129, 0.28)' : 'rgba(100, 116, 139, 0.16)'}
        stroke={dimensions ? '#34d399' : '#64748b'}
        strokeWidth="2"
      />

      {SIX_DIM_AXES.map((axis, i) => {
        const val = dataValues[i];
        const dot = pointAt(i, val);
        const labelPos = pointAt(i, 124);
        return (
          <g key={axis.key}>
            <circle cx={dot.x} cy={dot.y} r="3.5" fill={dimensions ? '#6ee7b7' : '#94a3b8'} />
            <text
              x={labelPos.x}
              y={labelPos.y - 4}
              textAnchor="middle"
              dominantBaseline="middle"
              className="fill-slate-200 text-[10px] font-semibold"
            >
              {axis.label}
            </text>
            <text
              x={labelPos.x}
              y={labelPos.y + 9}
              textAnchor="middle"
              dominantBaseline="middle"
              className="fill-emerald-300 text-[10px] font-mono font-bold"
            >
              {dimensions ? `${val}` : '--'}
            </text>
          </g>
        );
      })}
    </svg>
  );
}

export function DeckSixDimRadarSection({
  chaosYdk,
  notify
}: {
  chaosYdk?: string;
  notify: (t: string, k?: 'ok' | 'err') => void;
}) {
  const [presets, setPresets] = useState<any[]>([]);
  const [recentEvals, setRecentEvals] = useState<any[]>([]);
  const [selectedPreset, setSelectedPreset] = useState<string>('');
  const [ydkInput, setYdkInput] = useState<string>('');
  const [analysis, setAnalysis] = useState<{ profile: any; evaluation: any } | null>(null);
  const [loading, setLoading] = useState<boolean>(false);

  const loadInitialLists = useCallback(async () => {
    try {
      const [pr, ev] = await Promise.all([
        apiFetch('/api/portal/decks/presets'),
        apiFetch('/api/portal/decks/evaluations?limit=12')
      ]);
      const presetList = pr.presets || [];
      setPresets(presetList);
      setRecentEvals(ev.evaluations || []);
      if (presetList.length > 0) {
        setSelectedPreset((prev) => prev || presetList[0].id);
      }
    } catch {
      // ignore initial empty state
    }
  }, []);

  useEffect(() => {
    loadInitialLists();
  }, [loadInitialLists]);

  const runAnalyze = async (overridePreset?: string, overrideYdk?: string, overrideHash?: string) => {
    setLoading(true);
    try {
      const ydk = (overrideYdk ?? ydkInput).trim();
      const preset = overridePreset ?? selectedPreset;
      const body = overrideHash
        ? { deckHash: overrideHash }
        : ydk
        ? { ydkContent: ydk }
        : { presetName: preset };
      const res = await apiFetch('/api/portal/decks/analyze', {
        method: 'POST',
        body: JSON.stringify(body)
      });
      setAnalysis({ profile: res.profile, evaluation: res.evaluation });
    } catch (err) {
      notify(err instanceof Error ? err.message : '卡组静态剖析失败', 'err');
    } finally {
      setLoading(false);
    }
  };

  const profile = analysis?.profile;
  const evaluation = analysis?.evaluation;
  const evalPrompt = profile
    ? `请调用 deck_analyze_features(${
        ydkInput.trim() ? 'ydkContent=<我粘贴的构筑>' : `presetName="${selectedPreset}"`
      }) 分析卡组（DeckHash: ${profile.deckHash}），然后调用 deck_submit_evaluation 为爆发强度/续航韧性/阴间指数/娱乐整活度/操作复杂度/环境适应性给出 0~100 整数分与核心套路。`
    : '';

  return (
    <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-4 text-xs">
      <div className="flex items-center justify-between gap-2 flex-wrap">
        <div>
          <h2 className="text-sm font-semibold flex items-center gap-2 text-emerald-300">
            <Sparkles className="w-4 h-4" /> 卡组静态特征剖析器 & 0~100 六维能力雷达
          </h2>
          <p className="text-[11px] text-slate-400">
            服务端零大模型出站依赖：本地确定性提取怪魔陷曲线、字段与效果词频，结合外部 MCP AI 提交的 0~100 六维评分点亮雷达图
          </p>
        </div>
        {chaosYdk && (
          <button
            type="button"
            onClick={() => {
              setYdkInput(chaosYdk);
              runAnalyze(undefined, chaosYdk);
            }}
            className="px-2.5 py-1 rounded bg-purple-600/30 border border-purple-500/40 text-purple-200 cursor-pointer"
          >
            剖析刚才试抽的混沌卡组
          </button>
        )}
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-12 gap-3 items-end">
        <div className="lg:col-span-4">
          <label className="block text-slate-400 mb-1">选择预设/内置 WindBot 卡组</label>
          <select
            value={selectedPreset}
            onChange={(e) => {
              setSelectedPreset(e.target.value);
              setYdkInput('');
            }}
            className="w-full px-3 py-1.5 rounded bg-slate-950 border border-slate-700"
          >
            {presets.length === 0 && <option value="">（暂无预设卡组，请在右侧粘贴 .ydk）</option>}
            {presets.map((d) => (
              <option key={d.id} value={d.id}>
                {d.name} (主{d.mainCount}+额{d.extraCount}{d.hasDiyCards ? ' · 含DIY' : ''})
              </option>
            ))}
          </select>
        </div>
        <div className="lg:col-span-6">
          <label className="block text-slate-400 mb-1">或直接粘贴 .ydk 卡组文本（优先于预设下拉框）</label>
          <textarea
            rows={2}
            value={ydkInput}
            onChange={(e) => setYdkInput(e.target.value)}
            placeholder="#main&#10;89631143&#10;..."
            className="w-full px-3 py-1 rounded bg-slate-950 border border-slate-700 font-mono text-[11px]"
          />
        </div>
        <div className="lg:col-span-2">
          <button
            type="button"
            disabled={loading}
            onClick={() => runAnalyze()}
            className="w-full py-2 rounded-lg bg-emerald-600 hover:bg-emerald-500 font-semibold text-white cursor-pointer"
          >
            {loading ? '剖析中...' : '静态剖析 & 雷达'}
          </button>
        </div>
      </div>

      {recentEvals.length > 0 && (
        <div className="flex items-center gap-2 flex-wrap pt-1">
          <span className="text-[11px] text-slate-400">全服近期已评分卡组：</span>
          {recentEvals.map((ev) => (
            <button
              key={ev.id}
              type="button"
              onClick={() => runAnalyze(undefined, undefined, ev.deckHash)}
              className="px-2.5 py-1 rounded-full bg-slate-950 hover:bg-slate-800 border border-slate-700 text-[11px] flex items-center gap-1.5 cursor-pointer"
            >
              <span className="font-bold text-amber-300">[{ev.overallTier}]</span>
              <span>{ev.archetypeTitle || ev.deckName}</span>
            </button>
          ))}
        </div>
      )}

      {analysis && (
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-4 pt-2 border-t border-slate-800">
          <div className="lg:col-span-7 bg-slate-950 border border-slate-800 rounded-xl p-4 space-y-3">
            {profile ? (
              <>
                <div className="flex items-center justify-between gap-2 flex-wrap">
                  <div>
                    <span className="font-bold text-sm text-slate-100">{profile.deckName}</span>
                    <span className="ml-2 text-slate-400">
                      主 {profile.mainCount} / 额外 {profile.extraCount} / 副 {profile.sideCount}（去重 {profile.distinctCardCount} 种）
                    </span>
                  </div>
                  <span className="font-mono text-[10px] text-slate-500">Hash: {profile.deckHash.slice(0, 16)}...</span>
                </div>

                <div className="grid grid-cols-3 gap-2 text-center">
                  <div className="bg-slate-900 p-2 rounded border border-slate-800">
                    <div className="text-slate-400 text-[11px]">主卡组怪兽</div>
                    <div className="font-mono font-bold text-amber-300 text-sm">
                      {(profile.lowLevelMonsterCopies ?? 0) + (profile.highLevelMonsterCopies ?? 0)}
                    </div>
                    <div className="text-[10px] text-slate-500">
                      下级 {profile.lowLevelMonsterCopies} · 高星 {profile.highLevelMonsterCopies} · 调整 {profile.tunerCopies}
                    </div>
                  </div>
                  <div className="bg-slate-900 p-2 rounded border border-slate-800">
                    <div className="text-slate-400 text-[11px]">魔法卡</div>
                    <div className="font-mono font-bold text-emerald-300 text-sm">
                      {(profile.normalSpellCopies ?? 0) +
                        (profile.quickPlaySpellCopies ?? 0) +
                        (profile.continuousSpellCopies ?? 0) +
                        (profile.fieldSpellCopies ?? 0) +
                        (profile.equipSpellCopies ?? 0) +
                        (profile.ritualSpellCopies ?? 0)}
                    </div>
                    <div className="text-[10px] text-slate-500">
                      通常 {profile.normalSpellCopies} · 速攻 {profile.quickPlaySpellCopies} · 场地 {profile.fieldSpellCopies}
                    </div>
                  </div>
                  <div className="bg-slate-900 p-2 rounded border border-slate-800">
                    <div className="text-slate-400 text-[11px]">陷阱 & DIY</div>
                    <div className="font-mono font-bold text-pink-300 text-sm">
                      {(profile.normalTrapCopies ?? 0) + (profile.continuousTrapCopies ?? 0) + (profile.counterTrapCopies ?? 0)}
                    </div>
                    <div className="text-[10px] text-slate-500">DIY 卡 {profile.diyCardCopies} 张</div>
                  </div>
                </div>

                <div className="text-[11px] text-slate-300">
                  额外构成：融合 {profile.fusionCopies} · 同调 {profile.synchroCopies} · 超量 {profile.xyzCopies} · 连接 {profile.linkCopies}
                </div>

                {profile.keywordStats && (
                  <div className="flex flex-wrap gap-1.5">
                    {[
                      ['检索/加手', profile.keywordStats.searchOrAddHand],
                      ['特召展开', profile.keywordStats.specialSummon],
                      ['无效/三康', profile.keywordStats.negateOrDisable],
                      ['破坏解场', profile.keywordStats.destroy],
                      ['除外干扰', profile.keywordStats.banish],
                      ['墓地互动', profile.keywordStats.graveInteraction],
                      ['抽卡滤抽', profile.keywordStats.drawCards],
                      ['贴纸封锁', profile.keywordStats.floodgateOrLock]
                    ].map(([label, cnt]) => (
                      <span
                        key={String(label)}
                        className="px-2 py-0.5 rounded bg-slate-900 border border-slate-800 text-[11px] text-slate-300"
                      >
                        {label}: <b className="text-emerald-300 font-mono">{cnt}</b>
                      </span>
                    ))}
                  </div>
                )}
              </>
            ) : (
              <div className="text-slate-400 py-8 text-center">当前仅展示按 DeckHash 归档的六维评测卡片</div>
            )}

            {evaluation?.routines && (
              <div className="space-y-1.5 pt-2 border-t border-slate-800/80">
                {Array.isArray(evaluation.routines.coreRoutines) && evaluation.routines.coreRoutines.length > 0 && (
                  <div className="text-[11px] text-emerald-200">
                    <span className="font-semibold text-emerald-400">核心套路 Combo：</span>
                    {evaluation.routines.coreRoutines.join(' ｜ ')}
                  </div>
                )}
                {Array.isArray(evaluation.routines.strengths) && evaluation.routines.strengths.length > 0 && (
                  <div className="text-[11px] text-sky-200">
                    <span className="font-semibold text-sky-400">构筑优势：</span>
                    {evaluation.routines.strengths.join('；')}
                  </div>
                )}
                {Array.isArray(evaluation.routines.weaknesses) && evaluation.routines.weaknesses.length > 0 && (
                  <div className="text-[11px] text-rose-200">
                    <span className="font-semibold text-rose-400">针对弱点：</span>
                    {evaluation.routines.weaknesses.join('；')}
                  </div>
                )}
              </div>
            )}
          </div>

          <div className="lg:col-span-5 bg-slate-950 border border-slate-800 rounded-xl p-4 flex flex-col justify-between space-y-2">
            <div className="flex items-center justify-between">
              <div>
                <div className="font-bold text-sm text-emerald-300">
                  {evaluation ? evaluation.archetypeTitle : '待外部 AI 提交六维评分'}
                </div>
                {evaluation && (
                  <div className="text-[11px] text-slate-400">评测员: {evaluation.evaluatorName}</div>
                )}
              </div>
              {evaluation && (
                <div className="px-3 py-1 rounded-lg bg-amber-500/20 border border-amber-400/50 text-amber-300 font-bold text-base font-mono">
                  Tier {evaluation.overallTier}
                </div>
              )}
            </div>

            <SixDimRadarSvg dimensions={evaluation?.dimensions} />

            {evaluation?.commentary ? (
              <p className="text-[11px] text-slate-300 bg-slate-900 p-2.5 rounded border border-slate-800 leading-relaxed">
                {evaluation.commentary}
              </p>
            ) : (
              evalPrompt && (
                <div className="flex items-center justify-between gap-2 bg-slate-900 p-2 rounded border border-slate-800">
                  <span className="text-[11px] text-slate-400 truncate">发给外部 MCP AI 即可生成 0~100 六维雷达评分</span>
                  <QuickCopyPill
                    text={evalPrompt}
                    label="复制评测指令"
                    onCopied={() => notify('已复制六维评测提示词')}
                  />
                </div>
              )
            )}
          </div>
        </div>
      )}
    </div>
  );
}
