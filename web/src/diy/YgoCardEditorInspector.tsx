import { useState } from 'react';
import { Check, ChevronDown, ChevronUp, Copy, Wand2 } from 'lucide-react';
import {
  CARD_BITS,
  combinePendulumAndMonsterDesc,
  countLinkArrows,
  EditableDiyCard,
  packSetcodes,
  parseStringsConfSetnames,
  splitPendulumAndMonsterDesc,
  unpackSetcodes,
  YGO_ATTRIBUTES,
  YGO_CARD_PRESETS,
  YGO_LINK_ARROWS,
  YGO_OT_OPTIONS,
  YGO_RACES,
  YGO_TYPE_GROUPS
} from './ygoCardTaxonomy';

const PSCT_QUICK_SNIPPETS: { label: string; text: string }[] = [
  { label: '卡名①回合1次', text: '这个卡名的①②的效果1回合各能使用1次。\n' },
  { label: '① 召唤·特召诱发', text: '①：这张卡召唤·特殊召唤的场合才能发动。从卡组抽1张。\n' },
  { label: '② 二速康卡', text: '②：对方把魔法·陷阱·怪兽的效果发动时才能发动。那个发动无效并破坏。\n' },
  { label: '③ 遗言调度', text: '③：这张卡被送去墓地的场合才能发动。从卡组把1只同种族怪兽加入手卡。\n' },
  { label: '同调素材头', text: '调整＋调整以外的怪兽1只以上\n' },
  { label: '连接素材头', text: '效果怪兽2只以上\n' },
  { label: '超量素材头', text: '4星怪兽×2\n' }
];

export function CardBaseStatsInspector({
  card,
  onChange,
  stringsConfText = '',
  accent = 'indigo'
}: {
  card: EditableDiyCard;
  onChange: (next: EditableDiyCard) => void;
  stringsConfText?: string;
  accent?: 'indigo' | 'emerald';
}) {
  const [showRawBitmask, setShowRawBitmask] = useState(false);

  const isSpellOrTrap =
    ((card.type & CARD_BITS.SPELL) !== 0 || (card.type & CARD_BITS.TRAP) !== 0) &&
    (card.type & CARD_BITS.MONSTER) === 0;
  const isLink = (card.type & CARD_BITS.LINK) !== 0;
  const isPendulum = (card.type & CARD_BITS.PENDULUM) !== 0;
  const isXyz = (card.type & CARD_BITS.XYZ) !== 0;

  const setcodeSlots = unpackSetcodes(card.setcodeHex);
  const customSetnames = parseStringsConfSetnames(stringsConfText);

  const inputCls =
    accent === 'emerald' ? 'input-modern' : 'input-modern input-modern-indigo';

  const applyPreset = (presetType: number, isMonster: boolean) => {
    const next: EditableDiyCard = {
      ...card,
      type: presetType
    };
    if (!isMonster) {
      next.level = 0;
      next.race = 0;
      next.attribute = 0;
      next.attack = 0;
      next.defense = 0;
      next.lScale = 0;
      next.rScale = 0;
      next.linkMarker = 0;
    } else {
      if (!next.attribute) next.attribute = 0x20; // 默认暗属性
      if (!next.race) next.race = 0x1; // 默认战士族
      if ((presetType & CARD_BITS.LINK) !== 0) {
        const mask = next.linkMarker || (0x01 | 0x04); // 默认左下+右下 Link-2
        next.linkMarker = mask;
        next.level = Math.max(1, countLinkArrows(mask));
        next.defense = 0;
      } else if (!next.level) {
        next.level = 4;
      }
      if ((presetType & CARD_BITS.PENDULUM) !== 0 && next.lScale === 0 && next.rScale === 0) {
        next.lScale = 1;
        next.rScale = 8;
      }
    }
    onChange(next);
  };

  const toggleLinkArrow = (bit: number) => {
    const nextMask = (card.linkMarker ^ bit) >>> 0;
    const arrowCount = countLinkArrows(nextMask);
    onChange({
      ...card,
      linkMarker: nextMask,
      level: arrowCount > 0 ? arrowCount : 1,
      defense: 0
    });
  };

  const updateSetcodeSlot = (slotIdx: number, val: string) => {
    const nextSlots = [...setcodeSlots];
    nextSlots[slotIdx] = val;
    onChange({
      ...card,
      setcodeHex: packSetcodes(nextSlots)
    });
  };

  const toggleTypeBit = (bit: number) => {
    let nextType = (card.type & bit) !== 0 ? card.type & ~bit : card.type | bit;
    // 主类别互斥辅助：勾选魔法或陷阱时自动清掉怪兽位，勾选怪兽时自动清掉魔陷位
    if (bit === CARD_BITS.MONSTER && (nextType & CARD_BITS.MONSTER) !== 0) {
      nextType &= ~(CARD_BITS.SPELL | CARD_BITS.TRAP);
    } else if (bit === CARD_BITS.SPELL && (nextType & CARD_BITS.SPELL) !== 0) {
      nextType &= ~(CARD_BITS.MONSTER | CARD_BITS.TRAP);
    } else if (bit === CARD_BITS.TRAP && (nextType & CARD_BITS.TRAP) !== 0) {
      nextType &= ~(CARD_BITS.MONSTER | CARD_BITS.SPELL);
    }
    onChange({ ...card, type: nextType });
  };

  return (
    <div className="space-y-4 text-xs">
      {/* 1. 一键卡框模板条 */}
      <div className="space-y-1.5">
        <div className="flex items-center justify-between text-[11px] text-slate-400">
          <span className="font-semibold text-slate-300">一键切换卡种与卡框配色</span>
          <span className="font-mono text-[10px]">Type: 0x{Number(card.type || 0).toString(16)}</span>
        </div>
        <div className="flex flex-wrap gap-1.5">
          {YGO_CARD_PRESETS.map((p) => {
            const active = card.type === p.type;
            return (
              <button
                key={p.key}
                type="button"
                onClick={() => applyPreset(p.type, p.isMonster)}
                className={`px-2.5 py-1 rounded-lg border text-[11px] font-medium transition cursor-pointer ${
                  active
                    ? `${p.badgeCls} ring-1 ring-white/40 font-bold shadow-sm`
                    : 'bg-slate-900/80 border-slate-800 text-slate-400 hover:text-slate-200 hover:border-slate-700'
                }`}
              >
                {p.label}
              </button>
            );
          })}
        </div>
      </div>

      {/* 2. 卡密 / 卡名 / 同名异画 / 环境归属 */}
      <div className="grid grid-cols-1 sm:grid-cols-12 gap-2.5">
        <div className="sm:col-span-3">
          <label className="block text-[11px] text-slate-400 mb-1">卡密 (Code)</label>
          <input
            type="number"
            value={card.id}
            onChange={(e) => onChange({ ...card, id: Number(e.target.value) })}
            className={`w-full px-3 py-2 font-mono font-semibold ${inputCls}`}
          />
        </div>
        <div className="sm:col-span-5">
          <label className="block text-[11px] text-slate-400 mb-1">卡片名称</label>
          <input
            value={card.name}
            onChange={(e) => onChange({ ...card, name: e.target.value })}
            placeholder="输入卡片名称..."
            className={`w-full px-3 py-2 font-semibold ${inputCls}`}
          />
        </div>
        <div className="sm:col-span-4">
          <label className="block text-[11px] text-slate-400 mb-1">卡池许可 (OT)</label>
          <select
            value={card.ot}
            onChange={(e) => onChange({ ...card, ot: Number(e.target.value) })}
            className={`w-full px-2.5 py-2 ${inputCls}`}
          >
            {YGO_OT_OPTIONS.map((opt) => (
              <option key={opt.value} value={opt.value}>
                {opt.label}
              </option>
            ))}
          </select>
        </div>
      </div>

      {/* 3. 怪兽专属：可视化七属性珠 + 26 种族 + 星级/阶级 */}
      {!isSpellOrTrap && (
        <div className="glass-subcard rounded-2xl p-3.5 space-y-3">
          <div className="space-y-1.5">
            <label className="block text-[11px] text-slate-400 font-medium">怪兽属性</label>
            <div className="grid grid-cols-7 gap-1.5">
              {YGO_ATTRIBUTES.map((a) => {
                const selected = card.attribute === a.value;
                return (
                  <button
                    key={a.value}
                    type="button"
                    onClick={() => onChange({ ...card, attribute: a.value })}
                    className={`py-1.5 rounded-xl border text-center transition cursor-pointer ${
                      selected
                        ? `${a.pillCls} ring-1 ring-white/40 font-bold shadow`
                        : 'bg-slate-950/70 border-slate-800 text-slate-400 hover:border-slate-700'
                    }`}
                    title={a.label}
                  >
                    <div className="text-xs font-black">{a.kanji}</div>
                    <div className="text-[9px] opacity-75 hidden sm:block">{a.en}</div>
                  </button>
                );
              })}
            </div>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-3 gap-2.5">
            <div>
              <label className="block text-[11px] text-slate-400 mb-1">怪兽种族 (26 全种族)</label>
              <select
                value={card.race}
                onChange={(e) => onChange({ ...card, race: Number(e.target.value) })}
                className={`w-full px-3 py-2 ${inputCls}`}
              >
                {YGO_RACES.map((r) => (
                  <option key={r.value} value={r.value}>
                    {r.label}
                  </option>
                ))}
              </select>
            </div>

            <div>
              <label className="block text-[11px] text-slate-400 mb-1">
                {isLink ? '连接值 (LINK-N)' : isXyz ? '阶级 (Rank ★)' : '星级 (Level ★)'}
              </label>
              <select
                value={Math.max(0, Math.min(13, Number(card.level) || 0))}
                onChange={(e) => onChange({ ...card, level: Number(e.target.value) })}
                disabled={isLink}
                className={`w-full px-3 py-2 font-mono ${inputCls} disabled:opacity-60`}
              >
                <option value={0}>0 (无等级)</option>
                {Array.from({ length: 13 }, (_, i) => i + 1).map((lv) => (
                  <option key={lv} value={lv}>
                    {isLink ? `LINK-${lv}` : isXyz ? `Rank ${lv} 阶` : `Level ${lv} 星`}
                  </option>
                ))}
              </select>
            </div>

            <div>
              <label className="block text-[11px] text-slate-400 mb-1">同名异画卡密 (Alias)</label>
              <input
                type="number"
                value={card.alias}
                onChange={(e) => onChange({ ...card, alias: Number(e.target.value) })}
                placeholder="0 (无同名卡)"
                className={`w-full px-3 py-2 font-mono ${inputCls}`}
              />
            </div>
          </div>

          {/* DataEditorY 风格：ATK + 3x3 Link 罗盘 + DEF + 左右灵摆刻度 */}
          <div className="grid grid-cols-1 sm:grid-cols-12 gap-3 items-center pt-1 border-t border-slate-800/70">
            <div className="sm:col-span-4 space-y-1">
              <div className="flex items-center justify-between text-[11px]">
                <span className="text-slate-400">攻击力 ATK</span>
                <button
                  type="button"
                  onClick={() => onChange({ ...card, attack: card.attack === -2 ? 1800 : -2 })}
                  className={`px-1.5 py-0.2 rounded text-[10px] font-mono border cursor-pointer ${
                    card.attack === -2
                      ? 'bg-amber-500/25 border-amber-400 text-amber-200'
                      : 'bg-slate-900 border-slate-800 text-slate-400 hover:text-white'
                  }`}
                  title="在 YGOPro 中 -2 代表问号攻击力 (?)"
                >
                  设为 ?
                </button>
              </div>
              <input
                type="number"
                step={50}
                value={card.attack}
                onChange={(e) => onChange({ ...card, attack: Number(e.target.value) })}
                className={`w-full px-3 py-2 font-mono font-bold text-amber-200 ${inputCls}`}
              />
            </div>

            {/* 3×3 连接箭头罗盘 */}
            <div className={`sm:col-span-4 flex flex-col items-center ${!isLink ? 'opacity-35' : ''}`}>
              <span className="text-[10px] text-slate-400 mb-1">
                {isLink ? `点击点亮连接箭头 (LINK-${countLinkArrows(card.linkMarker) || 1})` : '连接箭头 (勾选连接后启用)'}
              </span>
              <div className="grid grid-cols-3 gap-1 p-1.5 rounded-xl bg-slate-950 border border-slate-800">
                {[0, 1, 2].map((r) =>
                  [0, 1, 2].map((c) => {
                    if (r === 1 && c === 1) {
                      return (
                        <div
                          key="center"
                          className="w-7 h-7 rounded-md bg-slate-900 flex items-center justify-center text-slate-600 text-xs"
                        >
                          ⬡
                        </div>
                      );
                    }
                    const arrow = YGO_LINK_ARROWS.find((a) => a.row === r && a.col === c)!;
                    const on = isLink && (card.linkMarker & arrow.bit) !== 0;
                    return (
                      <button
                        key={arrow.bit}
                        type="button"
                        disabled={!isLink}
                        onClick={() => toggleLinkArrow(arrow.bit)}
                        className={`w-7 h-7 rounded-md font-black text-xs border transition cursor-pointer disabled:cursor-not-allowed ${
                          on
                            ? 'bg-rose-600 border-amber-300 text-white shadow-[0_0_8px_rgba(244,63,94,0.8)]'
                            : 'bg-slate-900 border-slate-800 text-slate-500 hover:text-slate-200'
                        }`}
                        title={arrow.name}
                      >
                        {arrow.symbol}
                      </button>
                    );
                  })
                )}
              </div>
            </div>

            <div className={`sm:col-span-4 space-y-1 ${isLink ? 'opacity-40' : ''}`}>
              <div className="flex items-center justify-between text-[11px]">
                <span className="text-slate-400">{isLink ? '守备力 (Link无守备)' : '守备力 DEF'}</span>
                {!isLink && (
                  <button
                    type="button"
                    onClick={() => onChange({ ...card, defense: card.defense === -2 ? 1200 : -2 })}
                    className={`px-1.5 py-0.2 rounded text-[10px] font-mono border cursor-pointer ${
                      card.defense === -2
                        ? 'bg-amber-500/25 border-amber-400 text-amber-200'
                        : 'bg-slate-900 border-slate-800 text-slate-400 hover:text-white'
                    }`}
                  >
                    设为 ?
                  </button>
                )}
              </div>
              <input
                type="number"
                step={50}
                disabled={isLink}
                value={isLink ? 0 : card.defense}
                onChange={(e) => onChange({ ...card, defense: Number(e.target.value) })}
                className={`w-full px-3 py-2 font-mono font-bold text-sky-200 ${inputCls}`}
              />
            </div>
          </div>

          {/* 灵摆左右刻度 */}
          {isPendulum && (
            <div className="grid grid-cols-2 gap-3 pt-2 border-t border-slate-800/70">
              <div>
                <label className="block text-[11px] text-sky-300 mb-1">◀◆ 左灵摆蓝刻度 (0~13)</label>
                <input
                  type="number"
                  min={0}
                  max={13}
                  value={card.lScale}
                  onChange={(e) => onChange({ ...card, lScale: Math.max(0, Math.min(13, Number(e.target.value))) })}
                  className={`w-full px-3 py-1.5 font-mono font-bold ${inputCls}`}
                />
              </div>
              <div>
                <label className="block text-[11px] text-rose-300 mb-1">◆▶ 右灵摆红刻度 (0~13)</label>
                <input
                  type="number"
                  min={0}
                  max={13}
                  value={card.rScale}
                  onChange={(e) => onChange({ ...card, rScale: Math.max(0, Math.min(13, Number(e.target.value))) })}
                  className={`w-full px-3 py-1.5 font-mono font-bold ${inputCls}`}
                />
              </div>
            </div>
          )}
        </div>
      )}

      {/* 4. DataEditorY 风格四槽字段编辑器（联动 strings.conf !setname） */}
      <div className="glass-subcard rounded-2xl p-3.5 space-y-2.5">
        <div className="flex items-center justify-between">
          <span className="text-[11px] font-semibold text-slate-300">系列字段归属 (最多同时隶属 4 个字段)</span>
          <span className="font-mono text-[10px] text-slate-400">打包值: {card.setcodeHex || '0x0'}</span>
        </div>

        {customSetnames.length > 0 && (
          <div className="flex flex-wrap items-center gap-1.5">
            <span className="text-[10px] text-slate-400">本包自定义字段快填：</span>
            {customSetnames.map((sn) => (
              <button
                key={sn.code}
                type="button"
                onClick={() => {
                  const emptyIdx = setcodeSlots.findIndex((s) => !s);
                  updateSetcodeSlot(emptyIdx >= 0 ? emptyIdx : 0, sn.code);
                }}
                className="px-2 py-0.5 rounded-md bg-indigo-500/15 hover:bg-indigo-500/25 border border-indigo-500/30 text-[11px] text-indigo-200 cursor-pointer"
              >
                {sn.name} <span className="font-mono opacity-75">({sn.code})</span>
              </button>
            ))}
          </div>
        )}

        <div className="grid grid-cols-2 sm:grid-cols-4 gap-2">
          {([0, 1, 2, 3] as const).map((idx) => (
            <div key={idx}>
              <label className="block text-[10px] text-slate-500 mb-0.5">字段槽 #{idx + 1}</label>
              <input
                value={setcodeSlots[idx]}
                onChange={(e) => updateSetcodeSlot(idx, e.target.value)}
                placeholder="如 0xa100"
                className={`w-full px-2.5 py-1.5 font-mono text-xs ${inputCls}`}
              />
            </div>
          ))}
        </div>
      </div>

      {/* 5. 可展开精细位掩码矩阵 */}
      <div className="glass-subcard rounded-2xl px-3.5 py-2.5">
        <button
          type="button"
          onClick={() => setShowRawBitmask((v) => !v)}
          className="w-full flex items-center justify-between text-[11px] text-slate-300 font-medium cursor-pointer"
        >
          <span>高级：复合卡片种类位掩码精细勾选（如 灵摆+融合 / 调整+同调 / 特殊召唤怪）</span>
          {showRawBitmask ? <ChevronUp className="w-4 h-4 text-slate-400" /> : <ChevronDown className="w-4 h-4 text-slate-400" />}
        </button>

        {showRawBitmask && (
          <div className="space-y-2.5 pt-3 mt-2 border-t border-slate-800/80 animate-fade-in">
            {YGO_TYPE_GROUPS.map((grp) => (
              <div key={grp.title} className="space-y-1">
                <div className="text-[10px] text-slate-500">{grp.title}</div>
                <div className="flex flex-wrap gap-1.5">
                  {grp.items.map((it) => {
                    const checked = (card.type & it.bit) !== 0;
                    return (
                      <button
                        key={it.bit}
                        type="button"
                        onClick={() => toggleTypeBit(it.bit)}
                        className={`px-2.5 py-1 rounded-lg text-[11px] border cursor-pointer transition ${
                          checked
                            ? 'bg-indigo-600/35 border-indigo-400 text-indigo-100 font-semibold'
                            : 'bg-slate-900/80 border-slate-800 text-slate-400 hover:text-slate-200'
                        }`}
                      >
                        {it.label}
                      </button>
                    );
                  })}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

export function CardTextAndStringsInspector({
  card,
  onChange,
  accent = 'indigo',
  showHintStrings = true
}: {
  card: EditableDiyCard;
  onChange: (next: EditableDiyCard) => void;
  accent?: 'indigo' | 'emerald';
  showHintStrings?: boolean;
}) {
  const [copiedIdx, setCopiedIdx] = useState<number | null>(null);
  const isPendulum = (card.type & CARD_BITS.PENDULUM) !== 0;
  const { pendulumEffect, monsterEffect } = isPendulum
    ? splitPendulumAndMonsterDesc(card.desc)
    : { pendulumEffect: '', monsterEffect: card.desc };

  const inputCls =
    accent === 'emerald' ? 'input-modern' : 'input-modern input-modern-indigo';

  const strings = Array.from({ length: 16 }, (_, i) => card.strings?.[i] ?? '');

  const updateHintString = (index: number, val: string) => {
    const next = [...strings];
    next[index] = val;
    // 裁剪尾部连续空串保持干净
    let lastNonEmpty = next.length - 1;
    while (lastNonEmpty >= 0 && !next[lastNonEmpty].trim()) lastNonEmpty--;
    onChange({
      ...card,
      strings: lastNonEmpty >= 0 ? next.slice(0, lastNonEmpty + 1) : []
    });
  };

  const appendPsctSnippet = (snippet: string) => {
    if (isPendulum) {
      onChange({
        ...card,
        desc: combinePendulumAndMonsterDesc(pendulumEffect, (monsterEffect ? monsterEffect.trimEnd() + '\n' : '') + snippet.trim())
      });
    } else {
      const base = (card.desc || '').trimEnd();
      onChange({
        ...card,
        desc: (base ? base + '\n' : '') + snippet.trim()
      });
    }
  };

  return (
    <div className="space-y-4 text-xs">
      {/* OCG 第十二期标准日式效果句式快捷插入 */}
      <div className="space-y-1.5">
        <div className="text-[11px] text-slate-400 flex items-center gap-1">
          <Wand2 className="w-3.5 h-3.5 text-amber-400" />
          <span>快捷插入 OCG 标准效果句式 (PSCT)：</span>
        </div>
        <div className="flex flex-wrap gap-1.5">
          {PSCT_QUICK_SNIPPETS.map((s) => (
            <button
              key={s.label}
              type="button"
              onClick={() => appendPsctSnippet(s.text)}
              className="px-2.5 py-1 rounded-lg glass-subcard hover:border-indigo-400/50 text-[11px] text-slate-200 cursor-pointer"
            >
              + {s.label}
            </button>
          ))}
        </div>
      </div>

      {isPendulum ? (
        <div className="space-y-3">
          <div>
            <label className="block text-[11px] text-teal-300 font-semibold mb-1">
              ◀◆ 灵摆区域效果文本 ◆▶（将自动同步至卡面灵摆框）
            </label>
            <textarea
              rows={3}
              value={pendulumEffect}
              onChange={(e) =>
                onChange({
                  ...card,
                  desc: combinePendulumAndMonsterDesc(e.target.value, monsterEffect)
                })
              }
              placeholder="这个卡名的灵摆效果1回合只能使用1次。①：自己的主要阶段才能发动..."
              className={`w-full p-3 leading-relaxed ${inputCls}`}
            />
          </div>
          <div>
            <label className="block text-[11px] text-amber-200 font-semibold mb-1">怪兽区域效果 / 描述文本</label>
            <textarea
              rows={5}
              value={monsterEffect}
              onChange={(e) =>
                onChange({
                  ...card,
                  desc: combinePendulumAndMonsterDesc(pendulumEffect, e.target.value)
                })
              }
              placeholder="①：这张卡召唤·灵摆召唤的场合才能发动..."
              className={`w-full p-3 leading-relaxed ${inputCls}`}
            />
          </div>
        </div>
      ) : (
        <div>
          <label className="block text-[11px] text-slate-300 font-semibold mb-1">卡片效果文本 / 通常怪兽风味传记</label>
          <textarea
            rows={6}
            value={card.desc}
            onChange={(e) => onChange({ ...card, desc: e.target.value })}
            placeholder="编写卡片召唤条件与 ①②③ 效果文本..."
            className={`w-full p-3 leading-relaxed ${inputCls}`}
          />
        </div>
      )}

      {/* DataEditorY 同款：texts.str1 ~ str16 效果提示字符串与 aux.Stringid 一键复制 */}
      {showHintStrings && (
        <div className="glass-subcard rounded-2xl p-3.5 space-y-2.5">
          <div className="flex items-center justify-between gap-2">
            <div>
              <span className="font-semibold text-slate-200">效果提示字符串 (str1 ~ str16)</span>
              <p className="text-[10px] text-slate-400">
                当单卡有多个发动选项或连锁询问时，客户端菜单将显示此处的中文描述（对应 Lua{' '}
                <code className="text-indigo-300 font-mono">aux.Stringid(id, 0..15)</code>）
              </p>
            </div>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 max-h-60 overflow-y-auto pr-0.5">
            {strings.slice(0, 8).map((val, idx) => (
              <div key={idx} className="flex items-center gap-1.5">
                <button
                  type="button"
                  onClick={() => {
                    navigator.clipboard?.writeText(`aux.Stringid(id,${idx})`);
                    setCopiedIdx(idx);
                    setTimeout(() => setCopiedIdx(null), 1200);
                  }}
                  className="px-2 py-1.5 rounded-lg bg-slate-900 hover:bg-slate-800 border border-slate-800 font-mono text-[10px] text-indigo-300 flex items-center gap-1 shrink-0 cursor-pointer"
                  title={`点击复制 aux.Stringid(id,${idx})`}
                >
                  {copiedIdx === idx ? <Check className="w-3 h-3 text-emerald-400" /> : <Copy className="w-3 h-3" />}
                  <span>#{idx}</span>
                </button>
                <input
                  value={val}
                  onChange={(e) => updateHintString(idx, e.target.value)}
                  placeholder={`str${idx + 1}（如：检索本家 / 特殊召唤）`}
                  className={`flex-1 min-w-0 px-2.5 py-1.5 text-xs ${inputCls}`}
                />
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
