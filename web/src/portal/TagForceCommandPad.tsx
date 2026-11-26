import { useState } from 'react';
import { Send, Swords } from 'lucide-react';
import { apiFetch } from '../api';
import type { PortalUser } from './portalTypes';

const TAG_FORCE_STANCES: { code: string; badge: string; label: string; desc: string }[] = [
  { code: 'all_in_otk', badge: 'OTK', label: '总攻斩杀', desc: '倾尽资源本回合清空敌方 LP' },
  { code: 'aggressive', badge: 'ATK', label: '激进强攻', desc: '主动压场拆后场逼出阻抗' },
  { code: 'setup_partner', badge: 'PASS', label: '铺垫留场', desc: '留格子与墓地续航交接给我' },
  { code: 'control', badge: 'CTRL', label: '稳健留康', desc: '保留灰流丽/泡影精准断初动' },
  { code: 'defensive', badge: 'DEF', label: '防守保命', desc: '优先守备壁垒护住共享 LP' },
  { code: 'balanced', badge: 'AUTO', label: '自由发挥', desc: '由搭档自主权衡攻守节奏' }
];

export function TagForceCommandPad({
  roomName,
  slots,
  user,
  onRequireAuth,
  notify
}: {
  roomName: string;
  slots: any[];
  user: PortalUser | null;
  onRequireAuth: () => void;
  notify: (t: string, k?: 'ok' | 'err') => void;
}) {
  const [teamId, setTeamId] = useState<number>(0);
  const [stance, setStance] = useState<string>('setup_partner');
  const [note, setNote] = useState<string>('');
  const [sending, setSending] = useState<boolean>(false);

  const seatName = (idx: number) => slots.find((s) => s.seatIndex === idx)?.playerName || `席位#${idx}`;

  const dispatchDirective = async (chosenStance: string) => {
    setStance(chosenStance);
    if (!user) {
      onRequireAuth();
      return;
    }
    setSending(true);
    try {
      const res = await apiFetch(`/api/portal/rooms/${encodeURIComponent(roomName)}/partner-tactic`, {
        method: 'POST',
        body: JSON.stringify({
          teamId,
          stance: chosenStance,
          note: note.trim() || undefined
        })
      });
      notify(res.message || '已向同队 AI 搭档传达双打战术！');
    } catch (err) {
      notify(err instanceof Error ? err.message : '发送双打战术指令失败', 'err');
    } finally {
      setSending(false);
    }
  };

  return (
    <div className="bg-gradient-to-br from-amber-950/35 to-slate-950/90 border border-amber-500/35 rounded-2xl p-3 space-y-2.5">
      <div className="flex items-center justify-between gap-2">
        <span className="font-bold text-xs text-amber-300 flex items-center gap-1.5">
          <Swords className="w-3.5 h-3.5" /> 2v2《卡片力量》搭档战术盘
        </span>
        <span className="px-2 py-0.5 rounded-full bg-amber-500/15 text-[10px] text-amber-200 border border-amber-500/25">
          同队加密 · 零剧透
        </span>
      </div>

      <div className="bg-slate-950/90 border border-slate-800/90 rounded-xl px-2.5 py-1.5 text-[11px] overflow-x-auto no-scrollbar whitespace-nowrap">
        <span className="text-slate-400 mr-1">轮转顺位:</span>
        <span className="text-emerald-300 font-mono">
          #0 {seatName(0)}(A) → #2 {seatName(2)}(B) → #1 {seatName(1)}(A) → #3 {seatName(3)}(B)
        </span>
      </div>

      <div className="grid grid-cols-2 gap-1.5 p-1 rounded-xl bg-slate-950 border border-slate-800">
        <button
          type="button"
          onClick={() => setTeamId(0)}
          className={`py-1.5 rounded-lg text-[11px] font-semibold transition cursor-pointer ${
            teamId === 0 ? 'bg-emerald-600/35 text-emerald-200 border border-emerald-500/40' : 'text-slate-400 hover:text-slate-200'
          }`}
        >
          指挥 A 队 (#0/#1)
        </button>
        <button
          type="button"
          onClick={() => setTeamId(1)}
          className={`py-1.5 rounded-lg text-[11px] font-semibold transition cursor-pointer ${
            teamId === 1 ? 'bg-sky-600/35 text-sky-200 border border-sky-500/40' : 'text-slate-400 hover:text-slate-200'
          }`}
        >
          指挥 B 队 (#2/#3)
        </button>
      </div>

      <div className="grid grid-cols-2 gap-1.5">
        {TAG_FORCE_STANCES.map((s) => {
          const active = stance === s.code;
          return (
            <button
              key={s.code}
              type="button"
              disabled={sending}
              onClick={() => dispatchDirective(s.code)}
              title={s.desc}
              className={`p-2 rounded-xl text-left border transition active:scale-[0.98] cursor-pointer ${
                active
                  ? 'bg-amber-500/20 border-amber-400 text-amber-100 shadow-sm'
                  : 'bg-slate-950/85 border-slate-800 hover:border-amber-500/40 text-slate-200'
              }`}
            >
              <div className="flex items-center justify-between gap-1">
                <span className="font-semibold text-[11px]">{s.label}</span>
                <span className="text-[9px] font-mono px-1 rounded bg-slate-900 text-amber-300/90">{s.badge}</span>
              </div>
              <div className="text-[10px] text-slate-400 truncate mt-0.5">{s.desc}</div>
            </button>
          );
        })}
      </div>

      <form
        onSubmit={(e) => {
          e.preventDefault();
          dispatchDirective(stance);
        }}
        className="flex items-center gap-1.5"
      >
        <input
          value={note}
          onChange={(e) => setNote(e.target.value)}
          maxLength={120}
          placeholder="给搭档附一句悄悄话（如：后场盖了泡影）"
          className="flex-1 px-2.5 py-2 input-modern text-[11px]"
        />
        <button
          type="submit"
          disabled={sending}
          className="min-h-[36px] px-3 py-1.5 rounded-xl bg-amber-500 hover:bg-amber-400 text-slate-950 font-bold text-[11px] flex items-center gap-1 shrink-0 cursor-pointer"
        >
          <Send className="w-3 h-3" /> 下达
        </button>
      </form>
    </div>
  );
}
