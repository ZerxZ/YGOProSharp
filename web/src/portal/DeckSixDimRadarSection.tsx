import { useCallback, useEffect, useState } from 'react';
import { Button, Card, Chip, Spinner, Surface, Tabs } from '@heroui/react';
import { Radar, Sparkles } from 'lucide-react';
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
  const radius = 84;
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
    <svg viewBox="0 0 290 270" className="w-full max-w-[276px] mx-auto select-none">
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
            stroke="var(--border)"
            strokeOpacity={lv === 100 ? 0.95 : 0.55}
            strokeWidth={lv === 100 ? 1.3 : 1}
          />
        );
      })}

      {SIX_DIM_AXES.map((axis, i) => {
        const outer = pointAt(i, 100);
        return <line key={axis.key} x1={cx} y1={cy} x2={outer.x} y2={outer.y} stroke="var(--border)" strokeWidth="1" />;
      })}

      <polygon
        points={dataPoints.join(' ')}
        fill={dimensions ? 'color-mix(in oklab, var(--accent) 26%, transparent)' : 'color-mix(in oklab, var(--muted) 14%, transparent)'}
        stroke={dimensions ? 'var(--accent)' : 'var(--muted)'}
        strokeWidth="2"
      />

      {SIX_DIM_AXES.map((axis, i) => {
        const val = dataValues[i];
        const dot = pointAt(i, val);
        const labelPos = pointAt(i, 125);
        return (
          <g key={axis.key}>
            <circle cx={dot.x} cy={dot.y} r="3.8" fill={dimensions ? 'var(--accent)' : 'var(--muted)'} />
            <text
              x={labelPos.x}
              y={labelPos.y - 4}
              textAnchor="middle"
              dominantBaseline="middle"
              fill="var(--foreground)"
              className="text-[10px] font-semibold"
            >
              {axis.label}
            </text>
            <text
              x={labelPos.x}
              y={labelPos.y + 9}
              textAnchor="middle"
              dominantBaseline="middle"
              fill={dimensions ? 'var(--accent)' : 'var(--muted)'}
              className="text-[10px] font-mono font-bold"
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
    <Card className="rounded-xl p-4 sm:p-5 space-y-4 text-xs">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2.5">
        <div>
          <h2 className="text-sm sm:text-base font-bold flex items-center gap-2 text-foreground">
            <Radar className="w-4 h-4 text-accent" /> 卡组静态特征剖析器 & 0~100 六维能力雷达
          </h2>
          <p className="text-[11px] text-muted mt-0.5">
            确定性统计怪魔陷曲线、字段与效果词频，结合外部 MCP AI 提交的 0~100 六维评分渲染雷达图与套路图鉴
          </p>
        </div>

        <div className="flex items-center gap-2 flex-wrap">
          <Tabs
            selectedKey={inputMode}
            onSelectionChange={(k) => setInputMode(String(k) as 'preset' | 'ydk')}
          >
            <Tabs.ListContainer>
              <Tabs.List aria-label="构筑输入来源切换">
                <Tabs.Tab id="preset" className="text-xs px-3">
                  预设卡组库 ({presets.length})
                  <Tabs.Indicator />
                </Tabs.Tab>
                <Tabs.Tab id="ydk" className="text-xs px-3">
                  粘贴 .ydk 文本
                  <Tabs.Indicator />
                </Tabs.Tab>
              </Tabs.List>
            </Tabs.ListContainer>
          </Tabs>

          {chaosYdk && (
            <Button
              size="sm"
              variant="secondary"
              onPress={() => {
                setInputMode('ydk');
                setYdkInput(chaosYdk);
                runAnalyze(undefined, chaosYdk);
              }}
            >
              <Sparkles className="w-3.5 h-3.5 text-purple-400" />
              剖析刚抽的混沌卡组
            </Button>
          )}
        </div>
      </div>

      <div className="flex flex-col sm:flex-row gap-2.5 items-stretch sm:items-end">
        <div className="flex-1">
          {inputMode === 'preset' ? (
            <select
              value={selectedPreset}
              onChange={(e) => setSelectedPreset(e.target.value)}
              className="w-full h-9 px-3 text-xs input-modern"
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

        <Button
          variant="primary"
          isPending={loading}
          onPress={() => runAnalyze()}
          className="shrink-0"
        >
          {loading && <Spinner color="current" size="sm" />}
          <span>{loading ? '剖析中...' : '静态剖析 & 点亮雷达'}</span>
        </Button>
      </div>

      {recentEvals.length > 0 && (
        <div className="flex items-center gap-1.5 overflow-x-auto no-scrollbar pb-0.5">
          <span className="text-[11px] text-muted shrink-0">近期高光评测:</span>
          {recentEvals.map((ev) => (
            <button
              key={ev.id}
              type="button"
              onClick={() => runAnalyze(undefined, undefined, ev.deckHash)}
              className="px-2.5 py-1 rounded-md bg-surface-secondary border border-border hover:border-accent/50 text-[11px] flex items-center gap-1.5 shrink-0 cursor-pointer transition"
            >
              <span className="font-bold text-amber-400 font-mono">[{ev.overallTier}]</span>
              <span className="text-foreground">{ev.archetypeTitle || ev.deckName}</span>
            </button>
          ))}
        </div>
      )}

      {analysis && (
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-4 pt-3 border-t border-separator animate-fade-in">
          <Surface variant="secondary" className="lg:col-span-7 rounded-xl p-4 space-y-3.5 border border-border">
            {profile ? (
              <>
                <div className="flex items-center justify-between gap-2 flex-wrap">
                  <div>
                    <span className="font-bold text-sm text-foreground">{profile.deckName}</span>
                    <span className="ml-2 text-muted">
                      主 {profile.mainCount} / 额外 {profile.extraCount} / 副 {profile.sideCount}（去重 {profile.distinctCardCount} 种）
                    </span>
                  </div>
                  <span className="font-mono text-[10px] text-muted">Hash: {profile.deckHash.slice(0, 14)}...</span>
                </div>

                <div className="grid grid-cols-3 gap-2 text-center">
                  <div className="bg-background p-2.5 rounded-lg border border-border">
                    <div className="text-muted text-[11px]">怪兽配比</div>
                    <div className="font-mono font-bold text-amber-400 text-base">
                      {(profile.lowLevelMonsterCopies ?? 0) + (profile.highLevelMonsterCopies ?? 0)}
                    </div>
                    <div className="text-[10px] text-muted">
                      下级 {profile.lowLevelMonsterCopies} · 高星 {profile.highLevelMonsterCopies}
                    </div>
                  </div>
                  <div className="bg-background p-2.5 rounded-lg border border-border">
                    <div className="text-muted text-[11px]">魔法配比</div>
                    <div className="font-mono font-bold text-emerald-400 text-base">
                      {(profile.normalSpellCopies ?? 0) +
                        (profile.quickPlaySpellCopies ?? 0) +
                        (profile.continuousSpellCopies ?? 0) +
                        (profile.fieldSpellCopies ?? 0) +
                        (profile.equipSpellCopies ?? 0) +
                        (profile.ritualSpellCopies ?? 0)}
                    </div>
                    <div className="text-[10px] text-muted">
                      通常 {profile.normalSpellCopies} · 速攻 {profile.quickPlaySpellCopies}
                    </div>
                  </div>
                  <div className="bg-background p-2.5 rounded-lg border border-border">
                    <div className="text-muted text-[11px]">陷阱 & DIY</div>
                    <div className="font-mono font-bold text-pink-400 text-base">
                      {(profile.normalTrapCopies ?? 0) + (profile.continuousTrapCopies ?? 0) + (profile.counterTrapCopies ?? 0)}
                    </div>
                    <div className="text-[10px] text-muted">DIY 卡 {profile.diyCardCopies} 张</div>
                  </div>
                </div>

                <div className="text-[11px] text-muted bg-background px-3 py-1.5 rounded-lg border border-border">
                  额外轴分布：融合 <b className="text-foreground">{profile.fusionCopies}</b> · 同调 <b className="text-foreground">{profile.synchroCopies}</b> · 超量 <b className="text-foreground">{profile.xyzCopies}</b> · 连接 <b className="text-foreground">{profile.linkCopies}</b> · 调整怪兽 <b className="text-foreground">{profile.tunerCopies}</b>
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
                        className="px-2.5 py-1 rounded-md bg-background border border-border text-[11px] text-muted"
                      >
                        {label}: <b className="text-foreground font-mono">{cnt}</b>
                      </span>
                    ))}
                  </div>
                )}
              </>
            ) : (
              <div className="text-muted py-6 text-center">当前展示按 DeckHash 归档的六维评测百科</div>
            )}

            {evaluation?.routines && (
              <div className="space-y-2 pt-2.5 border-t border-separator leading-relaxed">
                {Array.isArray(evaluation.routines.starters) && evaluation.routines.starters.length > 0 && (
                  <div className="text-[11px] text-foreground/90">
                    <span className="font-semibold text-amber-400">起手初动：</span>
                    {evaluation.routines.starters.join(' ｜ ')}
                  </div>
                )}
                {Array.isArray(evaluation.routines.coreRoutines) && evaluation.routines.coreRoutines.length > 0 && (
                  <div className="text-[11px] text-foreground/90">
                    <span className="font-semibold text-emerald-400">核心套路：</span>
                    {evaluation.routines.coreRoutines.join(' ｜ ')}
                  </div>
                )}
                {Array.isArray(evaluation.routines.targetEndboard) && evaluation.routines.targetEndboard.length > 0 && (
                  <div className="text-[11px] text-foreground/90">
                    <span className="font-semibold text-indigo-400">标准终场：</span>
                    {evaluation.routines.targetEndboard.join(' ｜ ')}
                  </div>
                )}
                {Array.isArray(evaluation.routines.grindLoop) && evaluation.routines.grindLoop.length > 0 && (
                  <div className="text-[11px] text-foreground/90">
                    <span className="font-semibold text-teal-400">续航回收：</span>
                    {evaluation.routines.grindLoop.join('；')}
                  </div>
                )}
                {Array.isArray(evaluation.routines.chokepoints) && evaluation.routines.chokepoints.length > 0 && (
                  <div className="text-[11px] text-foreground/90">
                    <span className="font-semibold text-orange-400">怕吃坑点：</span>
                    {evaluation.routines.chokepoints.join('；')}
                  </div>
                )}
                {Array.isArray(evaluation.routines.strengths) && evaluation.routines.strengths.length > 0 && (
                  <div className="text-[11px] text-foreground/90">
                    <span className="font-semibold text-sky-400">构筑优势：</span>
                    {evaluation.routines.strengths.join('；')}
                  </div>
                )}
                {Array.isArray(evaluation.routines.weaknesses) && evaluation.routines.weaknesses.length > 0 && (
                  <div className="text-[11px] text-foreground/90">
                    <span className="font-semibold text-rose-400">构筑短板：</span>
                    {evaluation.routines.weaknesses.join('；')}
                  </div>
                )}
                {Array.isArray(evaluation.routines.upgradeSuggestions) && evaluation.routines.upgradeSuggestions.length > 0 && (
                  <div className="text-[11px] text-foreground/90">
                    <span className="font-semibold text-purple-400">改构建议：</span>
                    {evaluation.routines.upgradeSuggestions.join('；')}
                  </div>
                )}
              </div>
            )}
          </Surface>

          <Surface variant="secondary" className="lg:col-span-5 rounded-xl p-4 border border-border flex flex-col justify-between space-y-3">
            <div className="flex items-center justify-between gap-2">
              <div className="min-w-0">
                <div className="font-bold text-sm text-foreground truncate">
                  {evaluation ? evaluation.archetypeTitle : '待外部 AI 提交六维评分'}
                </div>
                {evaluation && <div className="text-[11px] text-muted">评测员: {evaluation.evaluatorName}</div>}
              </div>
              {evaluation && (
                <Chip size="sm" variant="soft" color="warning" className="h-6 px-2.5 font-bold font-mono text-xs shrink-0">
                  Tier {evaluation.overallTier}
                </Chip>
              )}
            </div>

            <SixDimRadarSvg dimensions={evaluation?.dimensions} />

            {evaluation?.commentary ? (
              <p className="text-[11px] text-foreground/90 bg-background p-3 rounded-lg border border-border leading-relaxed">
                {evaluation.commentary}
              </p>
            ) : (
              evalPrompt && (
                <div className="flex items-center justify-between gap-2 bg-background p-2.5 rounded-lg border border-border">
                  <span className="text-[11px] text-muted truncate">发给外部 MCP AI 生成六维评分</span>
                  <QuickCopyPill text={evalPrompt} label="复制评测指令" onCopied={() => notify('已复制六维评测提示词')} />
                </div>
              )
            )}
          </Surface>
        </div>
      )}
    </Card>
  );
}
