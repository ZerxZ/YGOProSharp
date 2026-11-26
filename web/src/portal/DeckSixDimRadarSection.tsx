import { useCallback, useEffect, useState } from 'react';
import { Sparkles } from 'lucide-react';
import { apiFetch } from '../api';
import { QuickCopyPill } from '../RoomConfigurator';
import { CodeMirrorEditor } from '../components/CodeMirrorEditor';

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
  const radius = 86;
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
    <svg viewBox="0 0 290 270" className="w-full max-w-[280px] mx-auto select-none">
      <defs>
        <linearGradient id="radarGrad" x1="0%" y1="0%" x2="100%" y2="100%">
          <stop offset="0%" stopColor="#10b981" stopOpacity="0.42" />
          <stop offset="100%" stopColor="#06b6d4" stopOpacity="0.22" />
        </linearGradient>
      </defs>

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
        fill={dimensions ? 'url(#radarGrad)' : 'rgba(100, 116, 139, 0.16)'}
        stroke={dimensions ? '#34d399' : '#64748b'}
        strokeWidth="2.2"
      />

      {SIX_DIM_AXES.map((axis, i) => {
        const val = dataValues[i];
        const dot = pointAt(i, val);
        const labelPos = pointAt(i, 125);
        return (
          <g key={axis.key}>
            <circle cx={dot.x} cy={dot.y} r="4" fill={dimensions ? '#6ee7b7' : '#94a3b8'} />
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
  apiPrefix = '/api/portal',
  notify
}: {
  chaosYdk?: string;
  apiPrefix?: '/api/portal' | '/api/v1';
  notify: (t: string, k?: 'ok' | 'err') => void;
}) {
  const [presets, setPresets] = useState<any[]>([]);
  const [recentEvals, setRecentEvals] = useState<any[]>([]);
  const [inputMode, setInputMode] = useState<'preset' | 'ydk'>('preset');
  const [selectedPreset, setSelectedPreset] = useState<string>('');
  const [ydkInput, setYdkInput] = useState<string>('');
  const [analysis, setAnalysis] = useState<{ profile: any; evaluation: any } | null>(null);
  const [loading, setLoading] = useState<boolean>(false);

  const loadInitialLists = useCallback(async () => {
    try {
      const [pr, ev] = await Promise.all([
        apiFetch(`${apiPrefix}/decks/presets`),
        apiFetch(`${apiPrefix}/decks/evaluations?limit=12`)
      ]);
      const presetList = pr.presets || [];
      setPresets(presetList);
      setRecentEvals(ev.evaluations || []);
      if (presetList.length > 0) {
        setSelectedPreset((prev) => prev || presetList[0].id);
      } else {
        setInputMode('ydk');
      }
    } catch {
      // ignore initial empty state
    }
  }, [apiPrefix]);

  useEffect(() => {
    loadInitialLists();
  }, [loadInitialLists]);

  const runAnalyze = async (overridePreset?: string, overrideYdk?: string, overrideHash?: string) => {
    setLoading(true);
    try {
      const ydk = overrideYdk !== undefined ? overrideYdk.trim() : inputMode === 'ydk' ? ydkInput.trim() : '';
      const preset = overridePreset ?? selectedPreset;
      const body = overrideHash
        ? { deckHash: overrideHash }
        : ydk
          ? { ydkContent: ydk }
          : { presetName: preset };
      const res = await apiFetch(`${apiPrefix}/decks/analyze`, {
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
        inputMode === 'ydk' && ydkInput.trim() ? 'ydkContent=<我粘贴的构筑>' : `presetName="${selectedPreset}"`
      }) 分析卡组（DeckHash: ${profile.deckHash}），然后调用 deck_submit_evaluation 为爆发强度/续航韧性/阴间指数/娱乐整活度/操作复杂度/环境适应性给出 0~100 整数分，并填写 starters、coreRoutines、targetEndboard、chokepoints 与 upgradeSuggestions。`
    : '';

  return (
    <div className="glass-card rounded-2xl p-4 sm:p-5 space-y-4 text-xs">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2.5">
        <div>
          <h2 className="text-sm sm:text-base font-bold flex items-center gap-2 text-emerald-300">
            <Sparkles className="w-4 h-4" /> 卡组静态特征剖析器 & 0~100 六维能力雷达
          </h2>
          <p className="text-[11px] text-slate-400 mt-0.5">
            确定性统计怪魔陷曲线、字段与效果词频，结合外部 MCP AI 提交的 0~100 六维评分渲染雷达图与套路图鉴
          </p>
        </div>
        <div className="flex items-center gap-2 flex-wrap">
          <div className="inline-flex p-0.5 rounded-xl bg-slate-950 border border-slate-800">
            <button
              type="button"
              onClick={() => setInputMode('preset')}
              className={`px-3 py-1.5 rounded-lg font-medium transition cursor-pointer ${
                inputMode === 'preset' ? 'bg-emerald-600 text-white' : 'text-slate-400 hover:text-slate-200'
              }`}
            >
              预设卡组库 ({presets.length})
            </button>
            <button
              type="button"
              onClick={() => setInputMode('ydk')}
              className={`px-3 py-1.5 rounded-lg font-medium transition cursor-pointer ${
                inputMode === 'ydk' ? 'bg-emerald-600 text-white' : 'text-slate-400 hover:text-slate-200'
              }`}
            >
              粘贴 .ydk 文本
            </button>
          </div>
          {chaosYdk && (
            <button
              type="button"
              onClick={() => {
                setInputMode('ydk');
                setYdkInput(chaosYdk);
                runAnalyze(undefined, chaosYdk);
              }}
              className="px-3 py-1.5 rounded-xl bg-purple-600/25 border border-purple-500/40 text-purple-200 font-medium cursor-pointer"
            >
              剖析刚抽的混沌卡组
            </button>
          )}
        </div>
      </div>

      <div className="flex flex-col sm:flex-row gap-2.5 items-stretch sm:items-end">
        <div className="flex-1">
          {inputMode === 'preset' ? (
            <select
              value={selectedPreset}
              onChange={(e) => setSelectedPreset(e.target.value)}
              className="w-full px-3.5 py-2.5 input-modern"
            >
              {presets.length === 0 && <option value="">（暂无预设卡组，请切换至「粘贴 .ydk 文本」）</option>}
              {presets.map((d) => (
                <option key={d.id} value={d.id}>
                  {d.name} (主{d.mainCount} + 额{d.extraCount}
                  {d.hasDiyCards ? ' · 含DIY' : ''})
                </option>
              ))}
            </select>
          ) : (
            <CodeMirrorEditor
              value={ydkInput}
              onChange={setYdkInput}
              language="ydk"
              accent={apiPrefix === '/api/v1' ? 'indigo' : 'emerald'}
              placeholder={'#main\n89631143\n...'}
              minHeight="104px"
              maxHeight="220px"
              compact
            />
          )}
        </div>
        <button
          type="button"
          disabled={loading}
          onClick={() => runAnalyze()}
          className="min-h-[42px] px-6 py-2.5 rounded-xl bg-gradient-to-r from-emerald-600 to-teal-500 hover:from-emerald-500 hover:to-teal-400 font-semibold text-white shadow-lg shadow-emerald-950/50 shrink-0 cursor-pointer"
        >
          {loading ? '剖析中...' : '静态剖析 & 点亮雷达'}
        </button>
      </div>

      {recentEvals.length > 0 && (
        <div className="flex items-center gap-2 overflow-x-auto no-scrollbar pb-0.5">
          <span className="text-[11px] text-slate-400 shrink-0">近期高光评测：</span>
          {recentEvals.map((ev) => (
            <button
              key={ev.id}
              type="button"
              onClick={() => runAnalyze(undefined, undefined, ev.deckHash)}
              className="px-2.5 py-1 rounded-full glass-subcard hover:border-emerald-500/40 text-[11px] flex items-center gap-1.5 shrink-0 cursor-pointer"
            >
              <span className="font-bold text-amber-300 font-mono">[{ev.overallTier}]</span>
              <span className="text-slate-200">{ev.archetypeTitle || ev.deckName}</span>
            </button>
          ))}
        </div>
      )}

      {analysis && (
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-4 pt-3 border-t border-slate-800/80 animate-fade-in">
          <div className="lg:col-span-7 glass-subcard rounded-2xl p-4 space-y-3.5">
            {profile ? (
              <>
                <div className="flex items-center justify-between gap-2 flex-wrap">
                  <div>
                    <span className="font-bold text-sm text-slate-100">{profile.deckName}</span>
                    <span className="ml-2 text-slate-400">
                      主 {profile.mainCount} / 额外 {profile.extraCount} / 副 {profile.sideCount}（去重 {profile.distinctCardCount} 种）
                    </span>
                  </div>
                  <span className="font-mono text-[10px] text-slate-500">Hash: {profile.deckHash.slice(0, 14)}...</span>
                </div>

                <div className="grid grid-cols-3 gap-2 text-center">
                  <div className="bg-slate-900/90 p-2.5 rounded-xl border border-slate-800">
                    <div className="text-slate-400 text-[11px]">怪兽配比</div>
                    <div className="font-mono font-bold text-amber-300 text-base">
                      {(profile.lowLevelMonsterCopies ?? 0) + (profile.highLevelMonsterCopies ?? 0)}
                    </div>
                    <div className="text-[10px] text-slate-500">
                      下级 {profile.lowLevelMonsterCopies} · 高星 {profile.highLevelMonsterCopies}
                    </div>
                  </div>
                  <div className="bg-slate-900/90 p-2.5 rounded-xl border border-slate-800">
                    <div className="text-slate-400 text-[11px]">魔法配比</div>
                    <div className="font-mono font-bold text-emerald-300 text-base">
                      {(profile.normalSpellCopies ?? 0) +
                        (profile.quickPlaySpellCopies ?? 0) +
                        (profile.continuousSpellCopies ?? 0) +
                        (profile.fieldSpellCopies ?? 0) +
                        (profile.equipSpellCopies ?? 0) +
                        (profile.ritualSpellCopies ?? 0)}
                    </div>
                    <div className="text-[10px] text-slate-500">
                      通常 {profile.normalSpellCopies} · 速攻 {profile.quickPlaySpellCopies}
                    </div>
                  </div>
                  <div className="bg-slate-900/90 p-2.5 rounded-xl border border-slate-800">
                    <div className="text-slate-400 text-[11px]">陷阱 & DIY</div>
                    <div className="font-mono font-bold text-pink-300 text-base">
                      {(profile.normalTrapCopies ?? 0) + (profile.continuousTrapCopies ?? 0) + (profile.counterTrapCopies ?? 0)}
                    </div>
                    <div className="text-[10px] text-slate-500">DIY 卡 {profile.diyCardCopies} 张</div>
                  </div>
                </div>

                <div className="text-[11px] text-slate-300 bg-slate-900/60 px-3 py-1.5 rounded-lg border border-slate-800/80">
                  额外轴分布：融合 <b>{profile.fusionCopies}</b> · 同调 <b>{profile.synchroCopies}</b> · 超量 <b>{profile.xyzCopies}</b> · 连接 <b>{profile.linkCopies}</b> · 调整怪兽 <b>{profile.tunerCopies}</b>
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
                        className="px-2.5 py-1 rounded-lg bg-slate-900/90 border border-slate-800 text-[11px] text-slate-300"
                      >
                        {label}: <b className="text-emerald-300 font-mono">{cnt}</b>
                      </span>
                    ))}
                  </div>
                )}
              </>
            ) : (
              <div className="text-slate-400 py-6 text-center">当前展示按 DeckHash 归档的六维评测百科</div>
            )}

            {evaluation?.routines && (
              <div className="space-y-2 pt-2.5 border-t border-slate-800/80 leading-relaxed">
                {Array.isArray(evaluation.routines.starters) && evaluation.routines.starters.length > 0 && (
                  <div className="text-[11px] text-amber-200">
                    <span className="font-semibold text-amber-400">起手初动：</span>
                    {evaluation.routines.starters.join(' ｜ ')}
                  </div>
                )}
                {Array.isArray(evaluation.routines.coreRoutines) && evaluation.routines.coreRoutines.length > 0 && (
                  <div className="text-[11px] text-emerald-200">
                    <span className="font-semibold text-emerald-400">核心套路：</span>
                    {evaluation.routines.coreRoutines.join(' ｜ ')}
                  </div>
                )}
                {Array.isArray(evaluation.routines.targetEndboard) && evaluation.routines.targetEndboard.length > 0 && (
                  <div className="text-[11px] text-indigo-200">
                    <span className="font-semibold text-indigo-400">标准终场：</span>
                    {evaluation.routines.targetEndboard.join(' ｜ ')}
                  </div>
                )}
                {Array.isArray(evaluation.routines.grindLoop) && evaluation.routines.grindLoop.length > 0 && (
                  <div className="text-[11px] text-teal-200">
                    <span className="font-semibold text-teal-400">续航回收：</span>
                    {evaluation.routines.grindLoop.join('；')}
                  </div>
                )}
                {Array.isArray(evaluation.routines.chokepoints) && evaluation.routines.chokepoints.length > 0 && (
                  <div className="text-[11px] text-orange-200">
                    <span className="font-semibold text-orange-400">怕吃坑点：</span>
                    {evaluation.routines.chokepoints.join('；')}
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
                    <span className="font-semibold text-rose-400">构筑短板：</span>
                    {evaluation.routines.weaknesses.join('；')}
                  </div>
                )}
                {Array.isArray(evaluation.routines.upgradeSuggestions) && evaluation.routines.upgradeSuggestions.length > 0 && (
                  <div className="text-[11px] text-purple-200">
                    <span className="font-semibold text-purple-400">改构建议：</span>
                    {evaluation.routines.upgradeSuggestions.join('；')}
                  </div>
                )}
              </div>
            )}
          </div>

          <div className="lg:col-span-5 glass-subcard rounded-2xl p-4 flex flex-col justify-between space-y-3">
            <div className="flex items-center justify-between gap-2">
              <div className="min-w-0">
                <div className="font-bold text-sm text-emerald-300 truncate">
                  {evaluation ? evaluation.archetypeTitle : '待外部 AI 提交六维评分'}
                </div>
                {evaluation && <div className="text-[11px] text-slate-400">评测员: {evaluation.evaluatorName}</div>}
              </div>
              {evaluation && (
                <div className="px-3 py-1 rounded-xl bg-amber-500/20 border border-amber-400/50 text-amber-300 font-bold text-sm font-mono shrink-0">
                  Tier {evaluation.overallTier}
                </div>
              )}
            </div>

            <SixDimRadarSvg dimensions={evaluation?.dimensions} />

            {evaluation?.commentary ? (
              <p className="text-[11px] text-slate-200 bg-slate-900/90 p-3 rounded-xl border border-slate-800 leading-relaxed">
                {evaluation.commentary}
              </p>
            ) : (
              evalPrompt && (
                <div className="flex items-center justify-between gap-2 bg-slate-900/90 p-2.5 rounded-xl border border-slate-800">
                  <span className="text-[11px] text-slate-400 truncate">发给外部 MCP AI 即可生成 0~100 六维雷达评分</span>
                  <QuickCopyPill text={evalPrompt} label="复制评测指令" onCopied={() => notify('已复制六维评测提示词')} />
                </div>
              )
            )}
          </div>
        </div>
      )}
    </div>
  );
}
