import { useState } from 'react';
import { Button, Chip, Input, Spinner, Surface, Tabs } from '@heroui/react';
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
    <Surface variant="secondary" className="border border-amber-500/35 rounded-xl p-3 space-y-2.5">
      <div className="flex items-center justify-between gap-2">
        <span className="font-bold text-xs text-amber-400 flex items-center gap-1.5">
          <Swords className="w-3.5 h-3.5" /> 2v2《卡片力量》搭档战术盘
        </span>
        <Chip size="sm" variant="soft" color="warning" className="h-5 px-2 text-[10px]">
          同队加密 · 零剧透
        </Chip>
      </div>

      <div className="bg-background border border-border rounded-lg px-2.5 py-1.5 text-[11px] overflow-x-auto no-scrollbar whitespace-nowrap">
        <span className="text-muted mr-1">轮转顺位:</span>
        <span className="text-emerald-400 font-mono">
          #0 {seatName(0)}(A) → #2 {seatName(2)}(B) → #1 {seatName(1)}(A) → #3 {seatName(3)}(B)
        </span>
      </div>

      <Tabs
        selectedKey={String(teamId)}
        onSelectionChange={(k) => setTeamId(Number(k))}
        className="w-full"
      >
        <Tabs.ListContainer>
          <Tabs.List aria-label="双打指挥队伍选择" className="w-full">
            <Tabs.Tab id="0" className="flex-1 text-[11px]">
              指挥 A 队 (#0 / #1)
              <Tabs.Indicator />
            </Tabs.Tab>
            <Tabs.Tab id="1" className="flex-1 text-[11px]">
              指挥 B 队 (#2 / #3)
              <Tabs.Indicator />
            </Tabs.Tab>
          </Tabs.List>
        </Tabs.ListContainer>
      </Tabs>

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
              className={`p-2 rounded-lg text-left border transition active:scale-[0.98] cursor-pointer ${
                active
                  ? 'bg-surface border-amber-400 text-foreground shadow-2xs'
                  : 'bg-background border-border hover:border-amber-400/50 text-foreground/90'
              }`}
            >
              <div className="flex items-center justify-between gap-1">
                <span className="font-semibold text-[11px]">{s.label}</span>
                <span className="text-[9px] font-mono px-1 rounded bg-surface-secondary text-amber-400">{s.badge}</span>
              </div>
              <div className="text-[10px] text-muted truncate mt-0.5">{s.desc}</div>
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
        <Input
          variant="secondary"
          value={note}
          onChange={(e) => setNote(e.target.value)}
          maxLength={120}
          placeholder="给搭档附一句悄悄话（如：后场盖了泡影）"
          className="flex-1 text-xs"
        />
        <Button type="submit" size="sm" variant="primary" isPending={sending} className="shrink-0">
          {sending ? <Spinner color="current" size="sm" /> : <Send className="w-3 h-3" />}
          <span>下达</span>
        </Button>
      </form>
    </Surface>
  );
}
