import { useState } from 'react';
import { Swords } from 'lucide-react';
import { apiFetch } from '../api';
import type { PortalUser } from './portalTypes';

const TAG_FORCE_STANCES: { code: string; label: string; desc: string }[] = [
  { code: 'all_in_otk', label: '总攻斩杀', desc: '本回合全力交资源打空对方 LP' },
  { code: 'aggressive', label: '激进强攻', desc: '主动压场拆后场逼出阻抗' },
  { code: 'setup_partner', label: '铺垫留场', desc: '做稳终端与续航留给搭档接棒' },
  { code: 'control', label: '稳健留康', desc: '保留灰流丽/泡影/红坑精准断初动' },
  { code: 'defensive', label: '防守保命', desc: '优先守备壁垒护住队伍共享 LP' },
  { code: 'balanced', label: '自由发挥', desc: '由 AI 搭档自主权衡攻守节奏' }
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
    <div className="bg-amber-950/20 border border-amber-500/30 rounded-lg p-2.5 space-y-2">
      <div className="flex items-center justify-between gap-2">
        <span className="font-semibold text-amber-300 flex items-center gap-1">
          <Swords className="w-3.5 h-3.5" /> 2v2《卡片力量》搭档战术指令盘
        </span>
        <span className="text-[10px] text-amber-200/80">仅同队私密可见 · 不向敌方剧透</span>
      </div>

      <div className="bg-slate-950/90 border border-slate-800 rounded px-2 py-1.5 text-[11px] text-slate-300">
        <span className="text-slate-400">四席行动轮转：</span>
        <span className="text-emerald-300 font-mono">
          #0 {seatName(0)}(A) → #2 {seatName(2)}(B) → #1 {seatName(1)}(A) → #3 {seatName(3)}(B)
        </span>
      </div>

      <div className="flex items-center gap-1.5">
        <button
          type="button"
          onClick={() => setTeamId(0)}
          className={`flex-1 py-1 rounded text-[11px] font-semibold border cursor-pointer ${
            teamId === 0
              ? 'bg-emerald-600/30 border-emerald-400 text-emerald-200'
              : 'bg-slate-950 border-slate-800 text-slate-400'
          }`}
        >
          指挥 A 队 (#0/#1)
        </button>
        <button
          type="button"
          onClick={() => setTeamId(1)}
          className={`flex-1 py-1 rounded text-[11px] font-semibold border cursor-pointer ${
            teamId === 1
              ? 'bg-sky-600/30 border-sky-400 text-sky-200'
              : 'bg-slate-950 border-slate-800 text-slate-400'
          }`}
        >
          指挥 B 队 (#2/#3)
        </button>
      </div>

      <div className="grid grid-cols-2 gap-1.5">
        {TAG_FORCE_STANCES.map((s) => (
          <button
            key={s.code}
            type="button"
            disabled={sending}
            onClick={() => dispatchDirective(s.code)}
            title={s.desc}
            className={`px-2 py-1.5 rounded text-left border transition cursor-pointer ${
              stance === s.code
                ? 'bg-amber-500/20 border-amber-400 text-amber-200'
                : 'bg-slate-950 border-slate-800 hover:border-amber-500/40 text-slate-200'
            }`}
          >
            <div className="font-semibold text-[11px]">{s.label}</div>
            <div className="text-[10px] text-slate-400 truncate">{s.desc}</div>
          </button>
        ))}
      </div>

      <div className="flex items-center gap-1.5">
        <input
          value={note}
          onChange={(e) => setNote(e.target.value)}
          maxLength={120}
          placeholder="补充交接叮嘱（如：后场盖了泡影，别炸我墓地炎龙）"
          className="flex-1 px-2.5 py-1 rounded bg-slate-950 border border-slate-700 text-[11px]"
        />
        <button
          type="button"
          disabled={sending}
          onClick={() => dispatchDirective(stance)}
          className="px-2.5 py-1 rounded bg-amber-500 hover:bg-amber-400 text-slate-950 font-semibold text-[11px] shrink-0 cursor-pointer"
        >
          下达
        </button>
      </div>
    </div>
  );
}
