import { QuickCopyPill } from '../RoomConfigurator';

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
  const mcpReviewPrompt = `请调用 duel_get_replay_timeline(duelId="${detail.replayCode}") 分析本局双方完整卡组与逐回合时间轴，并用 duel_submit_review 提交包含 0~100 发挥评分、胜负手转折点与改进建议的赛后复盘报告。`;

  return (
    <div className="bg-slate-900 border border-emerald-500/40 rounded-xl p-5 space-y-4 text-xs">
      <div className="flex items-center justify-between gap-2 flex-wrap border-b border-slate-800 pb-3">
        <div>
          <div className="text-sm font-bold text-emerald-300 flex items-center gap-2">
            <span>赛后全视角复盘剧场 · {detail.replayCode}</span>
            <span className="px-2 py-0.5 rounded text-[11px] bg-slate-800 text-slate-200">
              {detail.isTag2v2 ? '2v2 双打' : detail.mode === 1 ? 'Match' : '1v1 单局'} · {detail.turnCount} 回合
            </span>
          </div>
          <div className="text-[11px] text-slate-400 mt-0.5">
            房间 {detail.roomName} · 起始 LP {detail.startLp} · 获胜阵营:{' '}
            {detail.winnerTeam === 0 ? 'A 队 (0席侧)' : detail.winnerTeam === 1 ? 'B 队 (1席侧)' : '平局/未决'}
          </div>
        </div>
        <div className="flex items-center gap-2">
          <QuickCopyPill
            text={mcpReviewPrompt}
            label="复制 AI 教练复盘指令"
            onCopied={() => notify('已复制 MCP 赛后复盘提示词，发给外部 AI 即可生成报告')}
          />
          <button
            type="button"
            onClick={onClose}
            className="px-2.5 py-1 rounded bg-slate-800 hover:bg-slate-700 text-slate-300 cursor-pointer"
          >
            收起面板
          </button>
        </div>
      </div>

      {/* 参赛选手与赛后公开构筑概览 */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-3">
        {(detail.players || []).map((p: any) => (
          <div
            key={p.seatIndex}
            className={`p-3 rounded-lg border space-y-1.5 ${
              p.isWinner ? 'bg-emerald-950/30 border-emerald-500/40' : 'bg-slate-950 border-slate-800'
            }`}
          >
            <div className="flex items-center justify-between font-semibold">
              <span>
                #{p.seatIndex} {p.playerName} ({p.teamId === 0 ? 'A队' : 'B队'})
              </span>
              {p.isWinner && <span className="px-1.5 py-0.5 rounded bg-emerald-500/20 text-emerald-300 text-[10px]">胜方</span>}
            </div>
            <div className="text-[11px] text-slate-400">
              席位来源: {p.seatType} · 主卡组 {p.mainCount} / 额外 {p.extraCount}
            </div>
            {p.deckHash && (
              <div className="font-mono text-[10px] text-slate-500 truncate" title={p.deckHash}>
                DeckHash: {p.deckHash.slice(0, 16)}...
              </div>
            )}
            {(p.sampleCards || []).length > 0 && (
              <div className="text-[11px] text-slate-300 line-clamp-2">
                核心卡: {p.sampleCards.slice(0, 6).map((c: any) => `${c.name}×${c.copies}`).join('、')}
              </div>
            )}
          </div>
        ))}
      </div>

      {/* AI 教练赛后复盘与 0~100 评分报告 */}
      {reviews.length > 0 ? (
        <div className="space-y-4">
          {reviews.map((rev: any) => (
            <div key={rev.id} className="bg-slate-950 border border-slate-800 rounded-xl p-4 space-y-3">
              <div className="flex items-center justify-between gap-2 flex-wrap">
                <div className="font-bold text-sm text-amber-300">🏆 {rev.headline}</div>
                <div className="text-[11px] text-slate-400">
                  复盘分析师: <span className="text-emerald-300 font-semibold">{rev.reviewerName}</span> [{rev.reviewerRole}] ·{' '}
                  {new Date(rev.createdAt).toLocaleString()}
                </div>
              </div>

              <p className="text-slate-200 leading-relaxed whitespace-pre-line">{rev.summary}</p>

              {/* 选手 0~100 发挥评分条 */}
              {Array.isArray(rev.playerRatings) && rev.playerRatings.length > 0 && (
                <div className="grid grid-cols-1 md:grid-cols-2 gap-3 pt-1">
                  {rev.playerRatings.map((pr: any, idx: number) => {
                    const score = Number(pr.playScore ?? pr.score ?? 0);
                    const twScore = pr.teamworkScore != null ? Number(pr.teamworkScore) : null;
                    return (
                      <div key={idx} className="bg-slate-900 border border-slate-800 rounded-lg p-3 space-y-2">
                        <div className="flex items-center justify-between">
                          <span className="font-semibold text-slate-100 flex items-center gap-1.5">
                            {pr.playerName}
                            {pr.mvp && (
                              <span className="px-1.5 py-0.5 rounded bg-amber-500/25 border border-amber-400/40 text-amber-200 text-[10px]">
                                MVP
                              </span>
                            )}
                          </span>
                          <div className="font-mono font-bold text-emerald-300">
                            发挥 {score}/100
                            {twScore !== null && <span className="text-sky-300 ml-2">双打协同 {twScore}/100</span>}
                          </div>
                        </div>
                        <div className="w-full h-2 rounded-full bg-slate-800 overflow-hidden">
                          <div
                            className={`h-full rounded-full ${
                              score >= 85 ? 'bg-emerald-400' : score >= 65 ? 'bg-sky-400' : 'bg-amber-400'
                            }`}
                            style={{ width: `${Math.max(2, Math.min(100, score))}%` }}
                          />
                        </div>
                        {Array.isArray(pr.highlights) && pr.highlights.length > 0 && (
                          <div className="text-[11px] text-emerald-300">亮点：{pr.highlights.join('；')}</div>
                        )}
                        {Array.isArray(pr.mistakes) && pr.mistakes.length > 0 && (
                          <div className="text-[11px] text-rose-300">败着/失误：{pr.mistakes.join('；')}</div>
                        )}
                        {Array.isArray(pr.betterLines) && pr.betterLines.length > 0 && (
                          <div className="text-[11px] text-amber-200">更优路线：{pr.betterLines.join('；')}</div>
                        )}
                      </div>
                    );
                  })}
                </div>
              )}

              {/* 关键胜负手转折点 */}
              {Array.isArray(rev.turningPoints) && rev.turningPoints.length > 0 && (
                <div className="space-y-1.5">
                  <div className="font-semibold text-slate-300">关键胜负手转折点</div>
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-2">
                    {rev.turningPoints.map((tp: any, idx: number) => (
                      <div key={idx} className="bg-slate-900/80 border border-slate-800 rounded p-2.5 flex gap-2">
                        <span className="px-1.5 py-0.5 h-fit rounded bg-purple-500/20 text-purple-300 font-mono text-[10px] shrink-0">
                          T{tp.turn ?? '?'} {tp.phase || ''}
                        </span>
                        <div className="space-y-0.5">
                          <div className="font-medium text-slate-200">
                            {tp.actor || '局势节点'}{' '}
                            {tp.impact && <span className="text-[10px] text-amber-300">[{tp.impact}]</span>}
                          </div>
                          <div className="text-[11px] text-slate-400">{tp.analysis}</div>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* 观测到的实战套路 */}
              {Array.isArray(rev.observedPatterns) && rev.observedPatterns.length > 0 && (
                <div className="flex flex-wrap gap-2 pt-1">
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
        <div className="bg-slate-950 border border-slate-800 rounded-lg p-4 text-center text-slate-400 space-y-2">
          <div>该场录像尚未提交 AI 教练复盘打分报告。</div>
          <div className="text-[11px] text-slate-500">
            可点击右上角「复制 AI 教练复盘指令」发送给已接入 MCP 的外部 AI（任意角色均可调用{' '}
            <code className="text-emerald-300">duel_get_replay_timeline</code> 与{' '}
            <code className="text-emerald-300">duel_submit_review</code>）。
          </div>
        </div>
      )}

      {/* 归档公开事件与 AI 思考时间轴 */}
      <div className="space-y-1.5">
        <div className="font-semibold text-slate-300">本局归档公开时间轴 ({timeline.length} 条记录)</div>
        <div className="max-h-48 overflow-y-auto bg-slate-950 border border-slate-800 rounded-lg p-2.5 space-y-1 font-mono text-[11px]">
          {timeline.length > 0 ? (
            timeline.map((item: any, idx: number) => (
              <div key={idx} className="text-slate-300">
                <span className="text-slate-500">[T{item.turn ?? 0}]</span>{' '}
                {item.actor && <span className="text-amber-300">{item.actor}: </span>}
                <span>{item.text}</span>
              </div>
            ))
          ) : (
            <div className="text-slate-500">暂无时间轴记录</div>
          )}
        </div>
      </div>
    </div>
  );
}
