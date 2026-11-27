import { useMemo, useState } from 'react';
import { Button, Card, Chip, SearchField, Surface, Tabs } from '@heroui/react';
import { Award, Clock, Layers, Sparkles, X } from 'lucide-react';
import { QuickCopyPill } from '../RoomConfigurator';

function scoreGrade(score: number): { grade: string; color: 'warning' | 'success' | 'accent' | 'default' } {
  if (score >= 92) return { grade: 'S+', color: 'warning' };
  if (score >= 84) return { grade: 'S', color: 'success' };
  if (score >= 70) return { grade: 'A', color: 'accent' };
  return { grade: score >= 60 ? 'B' : 'C', color: 'default' };
}

type TheaterTab = 'reviews' | 'timeline' | 'decks';

export function ReplayReviewPanel({
  detail,
  onClose,
  notify
}: {
  detail: any;
  onClose: () => void;
  notify: (t: string, k?: 'ok' | 'err') => void;
}) {
  const reviews: any[] = detail.reviews || [];
  const timeline: any[] = Array.isArray(detail.timeline) ? detail.timeline : [];
  const lpCurve: any[] = Array.isArray(detail.lpCurve) ? detail.lpCurve : [];

  const [activeTab, setActiveTab] = useState<TheaterTab>(() => (reviews.length > 0 ? 'reviews' : 'timeline'));
  const [selectedReviewIdx, setSelectedReviewIdx] = useState(0);
  const [timelineQuery, setTimelineQuery] = useState('');

  const activeReview = reviews[selectedReviewIdx] || reviews[0] || null;

  const filteredTimeline = useMemo(() => {
    if (!timelineQuery.trim()) return timeline;
    const kw = timelineQuery.trim().toLowerCase();
    return timeline.filter(
      (item) =>
        String(item.text || '').toLowerCase().includes(kw) ||
        String(item.actor || '').toLowerCase().includes(kw) ||
        `t${item.turn ?? 0}`.includes(kw)
    );
  }, [timeline, timelineQuery]);

  const mcpReviewPrompt = `请调用 duel_get_replay_timeline(duelId="${detail.replayCode}") 分析本局双方完整卡组、LP曲线与逐回合时间轴，并用 duel_submit_review 提交包含 0~100 发挥评分、胜负手转折点与改进建议的赛后复盘报告。`;

  return (
    <Card className="border-accent/45 rounded-xl p-4 sm:p-5 space-y-4 text-xs animate-fade-in">
      {/* 顶部工作台标题栏 */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 border-b border-separator pb-3.5">
        <div className="space-y-1 min-w-0">
          <div className="text-sm sm:text-base font-bold text-foreground flex items-center gap-2 flex-wrap">
            <Award className="w-4 h-4 text-amber-400 shrink-0" />
            <span className="font-mono text-emerald-400">{detail.replayCode}</span>
            <span>· 赛后全视角复盘剧场</span>
            <Chip size="sm" variant="soft" className="h-5 px-2 text-[10px]">
              {detail.isTag2v2 ? '2v2 双打' : detail.mode === 1 ? 'Match BO3' : '1v1 单局'} · {detail.turnCount} 回合
            </Chip>
          </div>
          <div className="text-[11px] text-muted">
            房间 <span className="text-foreground font-medium">{detail.roomName}</span> · 初始 LP {detail.startLp} · 获胜方：
            <span className="text-emerald-400 font-semibold ml-1">
              {detail.winnerTeam === 0 ? 'A 队 (0席侧)' : detail.winnerTeam === 1 ? 'B 队 (1席侧)' : '平局 / 未决'}
            </span>
          </div>
        </div>

        <div className="flex items-center gap-2 shrink-0 flex-wrap">
          <QuickCopyPill
            text={mcpReviewPrompt}
            label="复制 AI 复盘指令"
            onCopied={() => notify('已复制 MCP 赛后复盘提示词，发给外部 AI 即可生成报告')}
          />
          <Button size="sm" variant="secondary" onPress={onClose} className="h-7 px-2.5 text-xs">
            <X className="w-3.5 h-3.5" />
            <span>收起</span>
          </Button>
        </div>
      </div>

      {/* 三视图导航 Tabs */}
      <Tabs
        selectedKey={activeTab}
        onSelectionChange={(k) => setActiveTab(String(k) as TheaterTab)}
        className="w-full"
      >
        <Tabs.ListContainer>
          <Tabs.List aria-label="复盘剧场视图切换" className="w-full sm:w-auto">
            <Tabs.Tab id="reviews" className="text-xs px-3 gap-1.5">
              <Sparkles className="w-3.5 h-3.5" />
              <span>AI 教练评分报告 ({reviews.length})</span>
              <Tabs.Indicator />
            </Tabs.Tab>
            <Tabs.Tab id="timeline" className="text-xs px-3 gap-1.5">
              <Clock className="w-3.5 h-3.5" />
              <span>时间轴 & LP 曲线 ({timeline.length})</span>
              <Tabs.Indicator />
            </Tabs.Tab>
            <Tabs.Tab id="decks" className="text-xs px-3 gap-1.5">
              <Layers className="w-3.5 h-3.5" />
              <span>选手构筑快照 ({(detail.players || []).length})</span>
              <Tabs.Indicator />
            </Tabs.Tab>
          </Tabs.List>
        </Tabs.ListContainer>
      </Tabs>

      {/* 视图 1：AI 教练赛后复盘与 0~100 评分报告 */}
      {activeTab === 'reviews' && (
        <div className="space-y-3.5 animate-fade-in">
          {reviews.length > 1 && (
            <div className="flex items-center gap-1.5 overflow-x-auto no-scrollbar">
              {reviews.map((rev: any, idx: number) => (
                <button
                  key={rev.id || idx}
                  type="button"
                  onClick={() => setSelectedReviewIdx(idx)}
                  className={`px-2.5 py-1 rounded-md border text-[11px] shrink-0 transition cursor-pointer ${
                    idx === selectedReviewIdx
                      ? 'bg-surface-secondary border-accent text-foreground font-semibold'
                      : 'bg-background border-border text-muted hover:text-foreground'
                  }`}
                >
                  #{idx + 1} {rev.reviewerName}
                </button>
              ))}
            </div>
          )}

          {activeReview ? (
            <Surface variant="secondary" className="rounded-xl p-4 space-y-3.5 border border-border">
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-1.5">
                <div className="font-bold text-sm text-amber-400">{activeReview.headline}</div>
                <div className="text-[11px] text-muted">
                  分析师: <span className="text-emerald-400 font-semibold">{activeReview.reviewerName}</span> [{activeReview.reviewerRole}] ·{' '}
                  {new Date(activeReview.createdAt).toLocaleString()}
                </div>
              </div>

              <div className="text-foreground/90 leading-relaxed whitespace-pre-line bg-background p-3.5 rounded-lg border border-border">
                {activeReview.summary}
              </div>

              {/* 选手 0~100 发挥评分条 */}
              {Array.isArray(activeReview.playerRatings) && activeReview.playerRatings.length > 0 && (
                <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                  {activeReview.playerRatings.map((pr: any, idx: number) => {
                    const score = Number(pr.playScore ?? pr.score ?? 0);
                    const twScore = pr.teamworkScore != null ? Number(pr.teamworkScore) : null;
                    const badge = scoreGrade(score);
                    return (
                      <div key={idx} className="bg-background border border-border rounded-lg p-3.5 space-y-2">
                        <div className="flex items-center justify-between gap-2 flex-wrap">
                          <span className="font-semibold text-foreground flex items-center gap-1.5">
                            <Chip size="sm" variant="soft" color={badge.color} className="h-5 px-1.5 font-mono text-[10px] font-bold">
                              {badge.grade}
                            </Chip>
                            <span>{pr.playerName}</span>
                            {pr.mvp && (
                              <Chip size="sm" variant="soft" color="warning" className="h-5 px-1.5 text-[10px]">
                                MVP
                              </Chip>
                            )}
                          </span>
                          <div className="font-mono font-bold text-emerald-400">
                            操作 {score}
                            {twScore !== null && <span className="text-sky-400 ml-2">配合 {twScore}</span>}
                          </div>
                        </div>

                        <div className="w-full h-1.5 rounded-full bg-surface-tertiary overflow-hidden">
                          <div
                            className={`h-full rounded-full transition-all duration-500 ${
                              score >= 85 ? 'bg-emerald-400' : score >= 65 ? 'bg-sky-400' : 'bg-amber-400'
                            }`}
                            style={{ width: `${Math.max(3, Math.min(100, score))}%` }}
                          />
                        </div>

                        {Array.isArray(pr.highlights) && pr.highlights.length > 0 && (
                          <div className="text-[11px] text-emerald-400 leading-relaxed">✦ 亮点：{pr.highlights.join('；')}</div>
                        )}
                        {Array.isArray(pr.mistakes) && pr.mistakes.length > 0 && (
                          <div className="text-[11px] text-rose-400 leading-relaxed">⚠ 失误：{pr.mistakes.join('；')}</div>
                        )}
                        {Array.isArray(pr.betterLines) && pr.betterLines.length > 0 && (
                          <div className="text-[11px] text-amber-400 leading-relaxed">→ 改进路线：{pr.betterLines.join('；')}</div>
                        )}
                      </div>
                    );
                  })}
                </div>
              )}

              {/* 关键胜负手转折点 */}
              {Array.isArray(activeReview.turningPoints) && activeReview.turningPoints.length > 0 && (
                <div className="space-y-2">
                  <div className="font-semibold text-foreground">关键胜负手转折点</div>
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-2">
                    {activeReview.turningPoints.map((tp: any, idx: number) => (
                      <div key={idx} className="bg-background border border-border rounded-lg p-3 flex gap-2.5">
                        <Chip size="sm" variant="soft" color="accent" className="h-5 px-2 font-mono text-[10px] shrink-0">
                          T{tp.turn ?? '?'} {tp.phase || ''}
                        </Chip>
                        <div className="space-y-0.5 min-w-0">
                          <div className="font-semibold text-foreground">
                            {tp.actor || '局势节点'}{' '}
                            {tp.impact && <span className="text-[10px] text-amber-400">[{tp.impact}]</span>}
                          </div>
                          <div className="text-[11px] text-muted leading-relaxed">{tp.analysis}</div>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* 实战套路标签 */}
              {Array.isArray(activeReview.observedPatterns) && activeReview.observedPatterns.length > 0 && (
                <div className="flex flex-wrap gap-1.5">
                  {activeReview.observedPatterns.map((pat: any, idx: number) => (
                    <Chip key={idx} size="sm" variant="soft" color="success" className="px-2.5 text-[11px]">
                      {pat.playerName ? `${pat.playerName} · ` : ''}
                      {pat.patternName}
                      {pat.effectiveness ? ` (${pat.effectiveness})` : ''}
                    </Chip>
                  ))}
                </div>
              )}
            </Surface>
          ) : (
            <Surface variant="secondary" className="rounded-xl p-6 text-center space-y-2.5 border border-border">
              <div className="font-semibold text-foreground">该场录像尚未提交 AI 教练复盘评分报告</div>
              <div className="text-[11px] text-muted max-w-xl mx-auto leading-relaxed">
                点击右上角「复制 AI 复盘指令」发送给任意已连接 MCP 的外部 AI，即可生成选手 0~100 评分、胜负手节点与套路总结；也可以先切换至「时间轴 & LP 曲线」查看完整对局过程。
              </div>
              <div className="pt-1">
                <Button size="sm" variant="secondary" onPress={() => setActiveTab('timeline')}>
                  查看逐回合时间轴 →
                </Button>
              </div>
            </Surface>
          )}
        </div>
      )}

      {/* 视图 2：LP 变化轨迹 & 可检索时间轴 */}
      {activeTab === 'timeline' && (
        <div className="space-y-3.5 animate-fade-in">
          {lpCurve.length > 0 && (
            <div className="space-y-1.5">
              <div className="font-semibold text-foreground">LP 攻防演化轨迹 (A队 : B队)</div>
              <div className="flex items-center gap-1.5 overflow-x-auto no-scrollbar pb-1">
                {lpCurve.map((pt: any, idx: number) => (
                  <Surface
                    key={idx}
                    variant="secondary"
                    className="px-2.5 py-1.5 rounded-lg border border-border shrink-0 font-mono text-[11px]"
                  >
                    <span className="text-muted mr-1">T{pt.turn ?? 0}</span>
                    <span className="text-emerald-400 font-bold">{pt.team0Lp}</span>
                    <span className="text-muted mx-1">:</span>
                    <span className="text-sky-400 font-bold">{pt.team1Lp}</span>
                  </Surface>
                ))}
              </div>
            </div>
          )}

          <div className="space-y-2">
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
              <span className="font-semibold text-foreground">归档公开动作与 AI 思考时间轴 ({filteredTimeline.length} 条)</span>
              <SearchField
                aria-label="筛选时间轴事件"
                variant="secondary"
                value={timelineQuery}
                onChange={setTimelineQuery}
                className="sm:w-56"
              >
                <SearchField.Group>
                  <SearchField.SearchIcon />
                  <SearchField.Input placeholder="搜回合 (如 t2) 或卡名..." className="text-xs" />
                  <SearchField.ClearButton />
                </SearchField.Group>
              </SearchField>
            </div>

            <div className="max-h-72 overflow-y-auto bg-[var(--console-bg)] rounded-xl border border-border p-3 space-y-1.5 font-mono text-[11px]">
              {filteredTimeline.length > 0 ? (
                filteredTimeline.map((item: any, idx: number) => (
                  <div key={idx} className="text-foreground/90 flex items-baseline gap-1.5 flex-wrap leading-relaxed">
                    <span className="text-muted shrink-0">[T{item.turn ?? 0}]</span>
                    {item.team0Lp != null && item.team1Lp != null && (
                      <span className="px-1.5 py-0.2 rounded bg-surface border border-border text-[10px] text-emerald-400 shrink-0">
                        {item.team0Lp}:{item.team1Lp}
                      </span>
                    )}
                    {item.actor && <span className="text-amber-400 font-semibold">{item.actor}:</span>}
                    <span>{item.text}</span>
                  </div>
                ))
              ) : (
                <div className="text-muted py-6 text-center font-sans">暂无匹配的时间轴记录</div>
              )}
            </div>
          </div>
        </div>
      )}

      {/* 视图 3：参赛选手赛后公开构筑概览 */}
      {activeTab === 'decks' && (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-2.5 animate-fade-in">
          {(detail.players || []).map((p: any) => (
            <Surface
              key={p.seatIndex}
              variant="secondary"
              className={`p-3.5 rounded-xl border space-y-1.5 ${
                p.isWinner ? 'border-emerald-500/50' : 'border-border'
              }`}
            >
              <div className="flex items-center justify-between gap-2 font-semibold">
                <span className="truncate text-foreground">
                  #{p.seatIndex} {p.playerName} ({p.teamId === 0 ? 'A队' : 'B队'})
                </span>
                {p.isWinner && (
                  <Chip size="sm" variant="soft" color="success" className="h-5 px-2 text-[10px] shrink-0">
                    WIN
                  </Chip>
                )}
              </div>
              <div className="text-[11px] text-muted font-mono">
                {p.seatType} · 主 {p.mainCount} / 额外 {p.extraCount}
              </div>
              {(p.sampleCards || []).length > 0 && (
                <div className="text-[11px] text-foreground/85 leading-relaxed pt-0.5">
                  代表卡：{p.sampleCards.slice(0, 6).map((c: any) => `${c.name}×${c.copies}`).join('、')}
                </div>
              )}
            </Surface>
          ))}
        </div>
      )}
    </Card>
  );
}
