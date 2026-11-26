import { Award, X } from 'lucide-react';
import { QuickCopyPill } from '../RoomConfigurator';

function scoreGrade(score: number): { grade: string; cls: string } {
  if (score >= 92) return { grade: 'S+', cls: 'bg-amber-500/25 text-amber-200 border-amber-400/50' };
  if (score >= 84) return { grade: 'S', cls: 'bg-emerald-500/25 text-emerald-200 border-emerald-400/50' };
  if (score >= 74) return { grade: 'A', cls: 'bg-sky-500/25 text-sky-200 border-sky-400/50' };
  if (score >= 60) return { grade: 'B', cls: 'bg-indigo-500/25 text-indigo-200 border-indigo-400/50' };
  return { grade: 'C', cls: 'bg-slate-800 text-slate-300 border-slate-700' };
}

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
  const mcpReviewPrompt = `请调用 duel_get_replay_timeline(duelId="${detail.replayCode}") 分析本局双方完整卡组、LP曲线与逐回合时间轴，并用 duel_submit_review 提交包含 0~100 发挥评分、胜负手转折点与改进建议的赛后复盘报告。`;

  return (
    <div className="glass-card border border-emerald-500/40 rounded-2xl p-4 sm:p-5 space-y-4 text-xs animate-fade-in">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 border-b border-slate-800/80 pb-3.5">
        <div className="space-y-1">
          <div className="text-sm sm:text-base font-bold text-emerald-300 flex items-center gap-2 flex-wrap">
            <Award className="w-4 h-4 text-amber-400 shrink-0" />
            <span>赛后全视角复盘剧场 · {detail.replayCode}</span>
            <span className="px-2 py-0.5 rounded-full text-[11px] bg-slate-800 text-slate-200">
              {detail.isTag2v2 ? '2v2 双打' : detail.mode === 1 ? 'Match BO3' : '1v1 单局'} · {detail.turnCount} 回合
            </span>
          </div>
          <div className="text-[11px] text-slate-400">
            房间 {detail.roomName} · 初始 LP {detail.startLp} · 获胜方：
            <span className="text-emerald-300 font-semibold ml-1">
              {detail.winnerTeam === 0 ? 'A 队 (0席侧)' : detail.winnerTeam === 1 ? 'B 队 (1席侧)' : '平局/未决'}
            </span>
          </div>
        </div>
        <div className="flex items-center gap-2 flex-wrap">
          <QuickCopyPill
            text={mcpReviewPrompt}
            label="复制 AI 教练复盘指令"
            onCopied={() => notify('已复制 MCP 赛后复盘提示词，发给外部 AI 即可生成报告')}
          />
          <button
            type="button"
            onClick={onClose}
            className="p-2 sm:px-3 sm:py-1.5 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-300 flex items-center gap-1 cursor-pointer"
          >
            <X className="w-3.5 h-3.5" />
            <span>收起</span>
          </button>
        </div>
      </div>

      {/* 参赛选手与赛后公开构筑概览 */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-2.5">
        {(detail.players || []).map((p: any) => (
          <div
            key={p.seatIndex}
            className={`p-3.5 rounded-xl border space-y-1.5 ${
              p.isWinner ? 'bg-emerald-950/35 border-emerald-500/40' : 'glass-subcard'
            }`}
          >
            <div className="flex items-center justify-between gap-2 font-semibold">
              <span className="truncate text-slate-100">
                #{p.seatIndex} {p.playerName} ({p.teamId === 0 ? 'A队' : 'B队'})
              </span>
              {p.isWinner && (
                <span className="px-2 py-0.5 rounded-full bg-emerald-500/20 border border-emerald-500/40 text-emerald-300 text-[10px] shrink-0">
                  WIN
                </span>
              )}
            </div>
            <div className="text-[11px] text-slate-400">
              {p.seatType} · 主卡组 {p.mainCount} / 额外 {p.extraCount}
            </div>
            {(p.sampleCards || []).length > 0 && (
              <div className="text-[11px] text-slate-300 line-clamp-2 leading-relaxed">
                代表卡：{p.sampleCards.slice(0, 6).map((c: any) => `${c.name}×${c.copies}`).join('、')}
              </div>
            )}
          </div>
        ))}
      </div>

      {/* AI 教练赛后复盘与 0~100 评分报告 */}
      {reviews.length > 0 ? (
        <div className="space-y-4">
          {reviews.map((rev: any) => (
            <div key={rev.id} className="glass-subcard rounded-2xl p-4 space-y-3.5">
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-1.5">
                <div className="font-bold text-sm text-amber-300">🏆 {rev.headline}</div>
                <div className="text-[11px] text-slate-400">
                  分析师: <span className="text-emerald-300 font-semibold">{rev.reviewerName}</span> [{rev.reviewerRole}] ·{' '}
                  {new Date(rev.createdAt).toLocaleString()}
                </div>
              </div>

              <p className="text-slate-200 leading-relaxed whitespace-pre-line bg-slate-900/70 p-3 rounded-xl border border-slate-800/80">
                {rev.summary}
              </p>

              {/* 选手 0~100 发挥评分条 */}
              {Array.isArray(rev.playerRatings) && rev.playerRatings.length > 0 && (
                <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                  {rev.playerRatings.map((pr: any, idx: number) => {
                    const score = Number(pr.playScore ?? pr.score ?? 0);
                    const twScore = pr.teamworkScore != null ? Number(pr.teamworkScore) : null;
                    const badge = scoreGrade(score);
                    return (
                      <div key={idx} className="bg-slate-900/90 border border-slate-800 rounded-xl p-3.5 space-y-2">
                        <div className="flex items-center justify-between gap-2 flex-wrap">
                          <span className="font-semibold text-slate-100 flex items-center gap-1.5">
                            <span className={`px-1.5 py-0.5 rounded border font-mono text-[10px] font-bold ${badge.cls}`}>
                              {badge.grade}
                            </span>
                            <span>{pr.playerName}</span>
                            {pr.mvp && (
                              <span className="px-1.5 py-0.5 rounded-full bg-amber-500/25 border border-amber-400/50 text-amber-200 text-[10px]">
                                MVP
                              </span>
                            )}
                          </span>
                          <div className="font-mono font-bold text-emerald-300">
                            操作 {score}
                            {twScore !== null && <span className="text-sky-300 ml-2">配合 {twScore}</span>}
                          </div>
                        </div>
                        <div className="w-full h-2 rounded-full bg-slate-800 overflow-hidden">
                          <div
                            className={`h-full rounded-full transition-all duration-500 ${
                              score >= 85
                                ? 'bg-gradient-to-r from-emerald-500 to-teal-300'
                                : score >= 65
                                  ? 'bg-gradient-to-r from-sky-500 to-cyan-300'
                                  : 'bg-gradient-to-r from-amber-500 to-orange-400'
                            }`}
                            style={{ width: `${Math.max(3, Math.min(100, score))}%` }}
                          />
                        </div>
                        {Array.isArray(pr.highlights) && pr.highlights.length > 0 && (
                          <div className="text-[11px] text-emerald-300 leading-relaxed">✦ 亮点：{pr.highlights.join('；')}</div>
                        )}
                        {Array.isArray(pr.mistakes) && pr.mistakes.length > 0 && (
                          <div className="text-[11px] text-rose-300 leading-relaxed">⚠ 失误：{pr.mistakes.join('；')}</div>
                        )}
                        {Array.isArray(pr.betterLines) && pr.betterLines.length > 0 && (
                          <div className="text-[11px] text-amber-200 leading-relaxed">💡 改进路线：{pr.betterLines.join('；')}</div>
                        )}
                      </div>
                    );
                  })}
                </div>
              )}

              {/* 关键胜负手转折点 */}
              {Array.isArray(rev.turningPoints) && rev.turningPoints.length > 0 && (
                <div className="space-y-2">
                  <div className="font-semibold text-slate-300">关键胜负手转折点</div>
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-2">
                    {rev.turningPoints.map((tp: any, idx: number) => (
                      <div key={idx} className="bg-slate-900/80 border border-slate-800 rounded-xl p-3 flex gap-2.5">
                        <span className="px-2 py-0.5 h-fit rounded-lg bg-purple-500/20 border border-purple-500/30 text-purple-200 font-mono text-[10px] shrink-0">
                          T{tp.turn ?? '?'} {tp.phase || ''}
                        </span>
                        <div className="space-y-0.5 min-w-0">
                          <div className="font-medium text-slate-200">
                            {tp.actor || '局势节点'}{' '}
                            {tp.impact && <span className="text-[10px] text-amber-300">[{tp.impact}]</span>}
                          </div>
                          <div className="text-[11px] text-slate-400 leading-relaxed">{tp.analysis}</div>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* 观测到的实战套路 */}
              {Array.isArray(rev.observedPatterns) && rev.observedPatterns.length > 0 && (
                <div className="flex flex-wrap gap-2">
                  {rev.observedPatterns.map((pat: any, idx: number) => (
                    <span
                      key={idx}
                      className="px-2.5 py-1 rounded-full bg-emerald-950/60 border border-emerald-500/30 text-emerald-200 text-[11px]"
                    >
                      {pat.playerName ? `${pat.playerName} · ` : ''}
                      {pat.patternName}
                      {pat.effectiveness ? ` (${pat.effectiveness})` : ''}
                    </span>
                  ))}
                </div>
              )}
            </div>
          ))}
        </div>
      ) : (
        <div className="glass-subcard rounded-2xl p-5 text-center text-slate-400 space-y-2">
          <div className="font-medium text-slate-300">该场录像尚未提交 AI 教练复盘打分报告</div>
          <div className="text-[11px] text-slate-500 max-w-xl mx-auto">
            点击上方「复制 AI 教练复盘指令」发送给任意已连接 MCP 的外部 AI，即可生成选手 0~100 评分、胜负手节点与套路总结。
          </div>
        </div>
      )}

      {/* LP 变化轨迹条 */}
      {lpCurve.length > 0 && (
        <div className="space-y-1.5">
          <div className="font-semibold text-slate-300">LP 攻防演化轨迹 (A队 : B队)</div>
          <div className="flex items-center gap-1.5 overflow-x-auto no-scrollbar pb-1">
            {lpCurve.map((pt: any, idx: number) => (
              <div
                key={idx}
                title={pt.summary || ''}
                className="px-2.5 py-1.5 rounded-xl glass-subcard shrink-0 font-mono text-[11px]"
              >
                <span className="text-slate-500 mr-1">T{pt.turn ?? 0}</span>
                <span className="text-emerald-300 font-bold">{pt.team0Lp}</span>
                <span className="text-slate-600 mx-1">:</span>
                <span className="text-sky-300 font-bold">{pt.team1Lp}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* 归档公开事件与 AI 思考时间轴 */}
      <div className="space-y-1.5">
        <div className="font-semibold text-slate-300">本局归档公开动作与思考时间轴 ({timeline.length} 条)</div>
        <div className="max-h-60 overflow-y-auto glass-subcard rounded-xl p-3 space-y-1.5 font-mono text-[11px]">
          {timeline.length > 0 ? (
            timeline.map((item: any, idx: number) => (
              <div key={idx} className="text-slate-300 flex items-baseline gap-1.5 flex-wrap leading-relaxed">
                <span className="text-slate-500">[T{item.turn ?? 0}]</span>
                {item.team0Lp != null && item.team1Lp != null && (
                  <span className="px-1.5 py-0.2 rounded bg-slate-900 text-[10px] text-emerald-300 border border-slate-800">
                    {item.team0Lp}:{item.team1Lp}
                  </span>
                )}
                {item.actor && <span className="text-amber-300 font-semibold">{item.actor}:</span>}
                <span>{item.text}</span>
              </div>
            ))
          ) : (
            <div className="text-slate-500 py-4 text-center font-sans">暂无时间轴记录</div>
          )}
        </div>
      </div>
    </div>
  );
}
