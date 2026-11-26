// 游戏王 OCG / YGOPro 卡片分类、位掩码、字段打包与智能脚本骨架工具

export interface EditableDiyCard {
  id: number;
  ot: number;
  alias: number;
  setcodeHex: string;
  type: number;
  level: number;
  lScale: number;
  rScale: number;
  race: number;
  attribute: number;
  attack: number;
  defense: number;
  linkMarker: number;
  name: string;
  desc: string;
  strings?: string[];
}

export const CARD_BITS = {
  MONSTER: 0x1,
  SPELL: 0x2,
  TRAP: 0x4,
  NORMAL: 0x10,
  EFFECT: 0x20,
  FUSION: 0x40,
  RITUAL: 0x80,
  SPIRIT: 0x200,
  UNION: 0x400,
  GEMINI: 0x800,
  TUNER: 0x1000,
  SYNCHRO: 0x2000,
  TOKEN: 0x4000,
  QUICKPLAY: 0x10000,
  CONTINUOUS: 0x20000,
  EQUIP: 0x40000,
  FIELD: 0x80000,
  COUNTER: 0x100000,
  FLIP: 0x200000,
  TOON: 0x400000,
  XYZ: 0x800000,
  PENDULUM: 0x1000000,
  SPSUMMON: 0x2000000,
  LINK: 0x4000000
} as const;

export const YGO_ATTRIBUTES: {
  value: number;
  label: string;
  kanji: string;
  en: string;
  pillCls: string;
  orbCls: string;
}[] = [
  {
    value: 0x01,
    label: '地属性',
    kanji: '地',
    en: 'EARTH',
    pillCls: 'bg-amber-900/50 border-amber-500/60 text-amber-200',
    orbCls: 'from-amber-500 via-yellow-700 to-stone-900 text-amber-50 border-amber-300/70'
  },
  {
    value: 0x02,
    label: '水属性',
    kanji: '水',
    en: 'WATER',
    pillCls: 'bg-sky-900/50 border-sky-400/60 text-sky-200',
    orbCls: 'from-sky-400 via-blue-600 to-sky-950 text-sky-50 border-sky-200/70'
  },
  {
    value: 0x04,
    label: '炎属性',
    kanji: '炎',
    en: 'FIRE',
    pillCls: 'bg-red-900/50 border-red-500/60 text-red-200',
    orbCls: 'from-orange-400 via-red-600 to-red-950 text-orange-50 border-orange-200/70'
  },
  {
    value: 0x08,
    label: '风属性',
    kanji: '风',
    en: 'WIND',
    pillCls: 'bg-emerald-900/50 border-emerald-400/60 text-emerald-200',
    orbCls: 'from-lime-400 via-emerald-600 to-teal-950 text-emerald-50 border-lime-200/70'
  },
  {
    value: 0x10,
    label: '光属性',
    kanji: '光',
    en: 'LIGHT',
    pillCls: 'bg-yellow-800/50 border-yellow-300/70 text-yellow-100',
    orbCls: 'from-yellow-200 via-amber-400 to-yellow-700 text-slate-950 border-yellow-100'
  },
  {
    value: 0x20,
    label: '暗属性',
    kanji: '暗',
    en: 'DARK',
    pillCls: 'bg-purple-950/70 border-purple-400/60 text-purple-200',
    orbCls: 'from-purple-500 via-indigo-900 to-slate-950 text-purple-100 border-purple-300/70'
  },
  {
    value: 0x40,
    label: '神属性',
    kanji: '神',
    en: 'DIVINE',
    pillCls: 'bg-amber-700/50 border-amber-300/80 text-amber-100',
    orbCls: 'from-amber-200 via-amber-500 to-red-900 text-slate-950 border-amber-100'
  }
];

export const YGO_RACES: { value: number; label: string }[] = [
  { value: 0, label: '无种族 (魔陷)' },
  { value: 0x1, label: '战士族' },
  { value: 0x2, label: '魔法师族' },
  { value: 0x4, label: '天使族' },
  { value: 0x8, label: '恶魔族' },
  { value: 0x10, label: '不死族' },
  { value: 0x20, label: '机械族' },
  { value: 0x40, label: '水族' },
  { value: 0x80, label: '炎族' },
  { value: 0x100, label: '岩石族' },
  { value: 0x200, label: '鸟兽族' },
  { value: 0x400, label: '植物族' },
  { value: 0x800, label: '昆虫族' },
  { value: 0x1000, label: '雷族' },
  { value: 0x2000, label: '龙族' },
  { value: 0x4000, label: '兽族' },
  { value: 0x8000, label: '兽战士族' },
  { value: 0x10000, label: '恐龙族' },
  { value: 0x20000, label: '鱼族' },
  { value: 0x40000, label: '海龙族' },
  { value: 0x80000, label: '爬虫类族' },
  { value: 0x100000, label: '念动力族' },
  { value: 0x200000, label: '幻神兽族' },
  { value: 0x400000, label: '创造神族' },
  { value: 0x800000, label: '幻龙族' },
  { value: 0x1000000, label: '电子界族' },
  { value: 0x2000000, label: '幻想魔族' }
];

export const YGO_OT_OPTIONS: { value: number; label: string }[] = [
  { value: 4, label: 'DIY 专属 (0x4)' },
  { value: 7, label: '全环境通用 OCG/TCG/DIY (0x7)' },
  { value: 3, label: 'OCG + TCG (0x3)' },
  { value: 1, label: '仅 OCG (0x1)' },
  { value: 2, label: '仅 TCG (0x2)' }
];

export const YGO_LINK_ARROWS: { bit: number; symbol: string; row: number; col: number; name: string }[] = [
  { bit: 0x40, symbol: '↖', row: 0, col: 0, name: '左上' },
  { bit: 0x80, symbol: '↑', row: 0, col: 1, name: '正上' },
  { bit: 0x100, symbol: '↗', row: 0, col: 2, name: '右上' },
  { bit: 0x08, symbol: '←', row: 1, col: 0, name: '正左' },
  { bit: 0x20, symbol: '→', row: 1, col: 2, name: '正右' },
  { bit: 0x01, symbol: '↙', row: 2, col: 0, name: '左下' },
  { bit: 0x02, symbol: '↓', row: 2, col: 1, name: '正下' },
  { bit: 0x04, symbol: '↘', row: 2, col: 2, name: '右下' }
];

export interface CardArchetypePreset {
  key: string;
  label: string;
  type: number;
  badgeCls: string;
  isMonster: boolean;
}

export const YGO_CARD_PRESETS: CardArchetypePreset[] = [
  { key: 'effect', label: '效果怪兽', type: CARD_BITS.MONSTER | CARD_BITS.EFFECT, badgeCls: 'bg-orange-500/20 border-orange-400/50 text-orange-200', isMonster: true },
  { key: 'normal', label: '通常怪兽', type: CARD_BITS.MONSTER | CARD_BITS.NORMAL, badgeCls: 'bg-amber-400/20 border-amber-300/50 text-amber-100', isMonster: true },
  { key: 'tuner', label: '调整效果', type: CARD_BITS.MONSTER | CARD_BITS.EFFECT | CARD_BITS.TUNER, badgeCls: 'bg-orange-500/20 border-amber-400/50 text-amber-200', isMonster: true },
  { key: 'ritual', label: '仪式怪兽', type: CARD_BITS.MONSTER | CARD_BITS.RITUAL | CARD_BITS.EFFECT, badgeCls: 'bg-sky-500/20 border-sky-400/50 text-sky-200', isMonster: true },
  { key: 'fusion', label: '融合怪兽', type: CARD_BITS.MONSTER | CARD_BITS.FUSION | CARD_BITS.EFFECT, badgeCls: 'bg-purple-500/25 border-purple-400/50 text-purple-200', isMonster: true },
  { key: 'synchro', label: '同调怪兽', type: CARD_BITS.MONSTER | CARD_BITS.SYNCHRO | CARD_BITS.EFFECT, badgeCls: 'bg-slate-200/20 border-slate-200/60 text-white', isMonster: true },
  { key: 'xyz', label: '超量怪兽', type: CARD_BITS.MONSTER | CARD_BITS.XYZ | CARD_BITS.EFFECT, badgeCls: 'bg-zinc-900 border-amber-400/50 text-amber-200', isMonster: true },
  { key: 'pendulum', label: '灵摆怪兽', type: CARD_BITS.MONSTER | CARD_BITS.PENDULUM | CARD_BITS.EFFECT, badgeCls: 'bg-gradient-to-r from-orange-500/25 to-emerald-500/25 border-emerald-400/50 text-emerald-100', isMonster: true },
  { key: 'link', label: '连接 Link', type: CARD_BITS.MONSTER | CARD_BITS.LINK | CARD_BITS.EFFECT, badgeCls: 'bg-blue-600/25 border-blue-400/60 text-blue-200', isMonster: true },
  { key: 'spell', label: '通常魔法', type: CARD_BITS.SPELL, badgeCls: 'bg-teal-500/20 border-teal-400/50 text-teal-200', isMonster: false },
  { key: 'quickplay', label: '速攻魔法', type: CARD_BITS.SPELL | CARD_BITS.QUICKPLAY, badgeCls: 'bg-teal-500/20 border-cyan-400/50 text-cyan-200', isMonster: false },
  { key: 'cont_spell', label: '永续魔法', type: CARD_BITS.SPELL | CARD_BITS.CONTINUOUS, badgeCls: 'bg-teal-500/20 border-emerald-400/50 text-emerald-200', isMonster: false },
  { key: 'field', label: '场地魔法', type: CARD_BITS.SPELL | CARD_BITS.FIELD, badgeCls: 'bg-teal-500/20 border-teal-300/50 text-teal-100', isMonster: false },
  { key: 'equip', label: '装备魔法', type: CARD_BITS.SPELL | CARD_BITS.EQUIP, badgeCls: 'bg-teal-500/20 border-emerald-300/50 text-emerald-100', isMonster: false },
  { key: 'trap', label: '通常陷阱', type: CARD_BITS.TRAP, badgeCls: 'bg-pink-500/20 border-pink-400/50 text-pink-200', isMonster: false },
  { key: 'cont_trap', label: '永续陷阱', type: CARD_BITS.TRAP | CARD_BITS.CONTINUOUS, badgeCls: 'bg-pink-500/20 border-rose-400/50 text-rose-200', isMonster: false },
  { key: 'counter', label: '反击陷阱', type: CARD_BITS.TRAP | CARD_BITS.COUNTER, badgeCls: 'bg-pink-600/25 border-fuchsia-400/50 text-fuchsia-200', isMonster: false }
];

export const YGO_TYPE_GROUPS: { title: string; items: { label: string; bit: number }[] }[] = [
  {
    title: '主类别',
    items: [
      { label: '怪兽', bit: CARD_BITS.MONSTER },
      { label: '魔法', bit: CARD_BITS.SPELL },
      { label: '陷阱', bit: CARD_BITS.TRAP }
    ]
  },
  {
    title: '召唤 / 额外 / 能力子类',
    items: [
      { label: '通常', bit: CARD_BITS.NORMAL },
      { label: '效果', bit: CARD_BITS.EFFECT },
      { label: '调整', bit: CARD_BITS.TUNER },
      { label: '仪式', bit: CARD_BITS.RITUAL },
      { label: '融合', bit: CARD_BITS.FUSION },
      { label: '同调', bit: CARD_BITS.SYNCHRO },
      { label: '超量', bit: CARD_BITS.XYZ },
      { label: '灵摆', bit: CARD_BITS.PENDULUM },
      { label: '连接', bit: CARD_BITS.LINK },
      { label: '特殊召唤', bit: CARD_BITS.SPSUMMON },
      { label: '反转', bit: CARD_BITS.FLIP },
      { label: '灵魂', bit: CARD_BITS.SPIRIT },
      { label: '同盟', bit: CARD_BITS.UNION },
      { label: '二重', bit: CARD_BITS.GEMINI },
      { label: '卡通', bit: CARD_BITS.TOON },
      { label: '衍生物', bit: CARD_BITS.TOKEN }
    ]
  },
  {
    title: '魔陷专用图标子类',
    items: [
      { label: '速攻 ⚡', bit: CARD_BITS.QUICKPLAY },
      { label: '永续 ∞', bit: CARD_BITS.CONTINUOUS },
      { label: '装备 ✚', bit: CARD_BITS.EQUIP },
      { label: '场地 ✦', bit: CARD_BITS.FIELD },
      { label: '反击 ↩', bit: CARD_BITS.COUNTER }
    ]
  }
];

export function countLinkArrows(mask: number): number {
  let count = 0;
  for (const a of YGO_LINK_ARROWS) {
    if ((mask & a.bit) !== 0) count++;
  }
  return count;
}

export function unpackSetcodes(hexStr: string | undefined | null): [string, string, string, string] {
  const cleaned = String(hexStr || '0')
    .trim()
    .replace(/^0x/i, '');
  let val = 0n;
  try {
    if (cleaned && /^[0-9a-fA-F]+$/.test(cleaned)) {
      val = BigInt('0x' + cleaned);
    }
  } catch {
    val = 0n;
  }
  const slots: [string, string, string, string] = ['', '', '', ''];
  for (let i = 0; i < 4; i++) {
    const part = Number((val >> BigInt(i * 16)) & 0xffffn);
    slots[i] = part > 0 ? '0x' + part.toString(16) : '';
  }
  return slots;
}

export function packSetcodes(slots: readonly string[]): string {
  let acc = 0n;
  let shift = 0n;
  for (let i = 0; i < 4; i++) {
    const raw = String(slots[i] || '')
      .trim()
      .replace(/^0x/i, '');
    if (!raw || !/^[0-9a-fA-F]{1,4}$/.test(raw)) continue;
    const part = BigInt('0x' + raw) & 0xffffn;
    if (part > 0n) {
      acc |= part << shift;
      shift += 16n;
    }
  }
  return '0x' + acc.toString(16);
}

export function parseStringsConfSetnames(rawConf: string): { code: string; name: string }[] {
  const result: { code: string; name: string }[] = [];
  const seen = new Set<string>();
  for (const rawLine of String(rawConf || '').split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line.startsWith('#')) continue;
    if (line.toLowerCase().startsWith('!setname ')) {
      const rest = line.slice(9).trim();
      const m = rest.match(/^(0x[0-9a-fA-F]+|\d+)\s+(.+)$/);
      if (m) {
        const hex = m[1].startsWith('0x') || m[1].startsWith('0X')
          ? '0x' + m[1].slice(2).toLowerCase()
          : '0x' + Number(m[1]).toString(16);
        if (!seen.has(hex)) {
          seen.add(hex);
          result.push({ code: hex, name: m[2].trim() });
        }
      }
    }
  }
  return result;
}

export interface CardFrameVisualTheme {
  kindLabel: string;
  dotColor: string;
  cardOuterBg: string;
  cardInnerBorder: string;
  titleBarCls: string;
  textBoxBg: string;
  isDarkTitle: boolean;
  isXyz: boolean;
  isLink: boolean;
  isPendulum: boolean;
  isSpell: boolean;
  isTrap: boolean;
}

export function resolveCardFrameTheme(type: number): CardFrameVisualTheme {
  const isSpell = (type & CARD_BITS.SPELL) !== 0 && (type & CARD_BITS.MONSTER) === 0;
  const isTrap = (type & CARD_BITS.TRAP) !== 0 && (type & CARD_BITS.MONSTER) === 0;
  const isPendulum = (type & CARD_BITS.PENDULUM) !== 0;
  const isLink = (type & CARD_BITS.LINK) !== 0;
  const isXyz = (type & CARD_BITS.XYZ) !== 0;
  const isSynchro = (type & CARD_BITS.SYNCHRO) !== 0;
  const isFusion = (type & CARD_BITS.FUSION) !== 0;
  const isRitual = (type & CARD_BITS.RITUAL) !== 0 && !isSpell;
  const isToken = (type & CARD_BITS.TOKEN) !== 0;
  const isNormal = (type & CARD_BITS.NORMAL) !== 0;

  if (isSpell) {
    const sub =
      (type & CARD_BITS.QUICKPLAY) !== 0
        ? '速攻魔法'
        : (type & CARD_BITS.CONTINUOUS) !== 0
          ? '永续魔法'
          : (type & CARD_BITS.FIELD) !== 0
            ? '场地魔法'
            : (type & CARD_BITS.EQUIP) !== 0
              ? '装备魔法'
              : (type & CARD_BITS.RITUAL) !== 0
                ? '仪式魔法'
                : '通常魔法';
    return {
      kindLabel: sub,
      dotColor: 'bg-teal-400',
      cardOuterBg: 'bg-gradient-to-b from-[#1a8a78] via-[#12685b] to-[#0b463d]',
      cardInnerBorder: 'border-teal-300/45',
      titleBarCls: 'bg-teal-950/35 border-teal-300/40 text-white',
      textBoxBg: 'bg-[#d8efe9]/95 text-slate-900 border-teal-700/60',
      isDarkTitle: false,
      isXyz: false,
      isLink: false,
      isPendulum: false,
      isSpell: true,
      isTrap: false
    };
  }

  if (isTrap) {
    const sub =
      (type & CARD_BITS.COUNTER) !== 0
        ? '反击陷阱'
        : (type & CARD_BITS.CONTINUOUS) !== 0
          ? '永续陷阱'
          : '通常陷阱';
    return {
      kindLabel: sub,
      dotColor: 'bg-pink-500',
      cardOuterBg: 'bg-gradient-to-b from-[#ab296a] via-[#831c50] to-[#521032]',
      cardInnerBorder: 'border-pink-300/45',
      titleBarCls: 'bg-rose-950/35 border-pink-300/40 text-white',
      textBoxBg: 'bg-[#f4dce9]/95 text-slate-900 border-pink-800/60',
      isDarkTitle: false,
      isXyz: false,
      isLink: false,
      isPendulum: false,
      isSpell: false,
      isTrap: true
    };
  }

  if (isLink) {
    return {
      kindLabel: '连接',
      dotColor: 'bg-sky-500',
      cardOuterBg: 'bg-gradient-to-b from-[#17559e] via-[#0f3b74] to-[#092349]',
      cardInnerBorder: 'border-sky-300/50',
      titleBarCls: 'bg-blue-950/45 border-sky-300/40 text-white',
      textBoxBg: 'bg-[#dbe8f6]/95 text-slate-900 border-blue-800/60',
      isDarkTitle: false,
      isXyz: false,
      isLink: true,
      isPendulum: false,
      isSpell: false,
      isTrap: false
    };
  }

  if (isXyz) {
    return {
      kindLabel: isPendulum ? '超量·灵摆' : '超量',
      dotColor: 'bg-zinc-300',
      cardOuterBg: isPendulum
        ? 'bg-gradient-to-b from-[#1f2024] via-[#15171c] to-[#116558]'
        : 'bg-gradient-to-b from-[#27272a] via-[#121215] to-[#050507]',
      cardInnerBorder: 'border-amber-300/45',
      titleBarCls: 'bg-black/60 border-amber-400/40 text-amber-200',
      textBoxBg: 'bg-[#e6e6ea]/95 text-slate-900 border-zinc-600',
      isDarkTitle: false,
      isXyz: true,
      isLink: false,
      isPendulum,
      isSpell: false,
      isTrap: false
    };
  }

  if (isSynchro) {
    return {
      kindLabel: isPendulum ? '同调·灵摆' : '同调',
      dotColor: 'bg-slate-100',
      cardOuterBg: isPendulum
        ? 'bg-gradient-to-b from-[#f1f5f9] via-[#cbd5e1] to-[#167969]'
        : 'bg-gradient-to-b from-[#f8fafc] via-[#e2e8f0] to-[#94a3b8]',
      cardInnerBorder: 'border-slate-400/80',
      titleBarCls: 'bg-white/75 border-slate-400/80 text-slate-900',
      textBoxBg: 'bg-[#f8fafc]/95 text-slate-900 border-slate-500/70',
      isDarkTitle: true,
      isXyz: false,
      isLink: false,
      isPendulum,
      isSpell: false,
      isTrap: false
    };
  }

  if (isFusion) {
    return {
      kindLabel: isPendulum ? '融合·灵摆' : '融合',
      dotColor: 'bg-purple-400',
      cardOuterBg: isPendulum
        ? 'bg-gradient-to-b from-[#7e3ba6] via-[#5b267d] to-[#146e60]'
        : 'bg-gradient-to-b from-[#863fb0] via-[#602784] to-[#3b1554]',
      cardInnerBorder: 'border-purple-300/45',
      titleBarCls: 'bg-purple-950/35 border-purple-300/40 text-white',
      textBoxBg: 'bg-[#eadcf5]/95 text-slate-900 border-purple-800/60',
      isDarkTitle: false,
      isXyz: false,
      isLink: false,
      isPendulum,
      isSpell: false,
      isTrap: false
    };
  }

  if (isRitual) {
    return {
      kindLabel: isPendulum ? '仪式·灵摆' : '仪式',
      dotColor: 'bg-blue-400',
      cardOuterBg: isPendulum
        ? 'bg-gradient-to-b from-[#3d72b8] via-[#2b5491] to-[#146e60]'
        : 'bg-gradient-to-b from-[#457cc4] via-[#2e5996] to-[#1c3966]',
      cardInnerBorder: 'border-sky-200/50',
      titleBarCls: 'bg-blue-950/30 border-sky-200/40 text-white',
      textBoxBg: 'bg-[#dce8f8]/95 text-slate-900 border-blue-700/60',
      isDarkTitle: false,
      isXyz: false,
      isLink: false,
      isPendulum,
      isSpell: false,
      isTrap: false
    };
  }

  if (isToken) {
    return {
      kindLabel: '衍生物',
      dotColor: 'bg-slate-400',
      cardOuterBg: 'bg-gradient-to-b from-[#64748b] via-[#475569] to-[#334155]',
      cardInnerBorder: 'border-slate-300/45',
      titleBarCls: 'bg-slate-900/40 border-slate-300/35 text-slate-100',
      textBoxBg: 'bg-[#e2e8f0]/95 text-slate-900 border-slate-600',
      isDarkTitle: false,
      isXyz: false,
      isLink: false,
      isPendulum: false,
      isSpell: false,
      isTrap: false
    };
  }

  if (isNormal) {
    return {
      kindLabel: isPendulum ? '通常·灵摆' : '通常怪兽',
      dotColor: 'bg-amber-300',
      cardOuterBg: isPendulum
        ? 'bg-gradient-to-b from-[#d9b45b] via-[#b88f38] to-[#146e60]'
        : 'bg-gradient-to-b from-[#dfc06b] via-[#c39c44] to-[#8c6b22]',
      cardInnerBorder: 'border-amber-200/70',
      titleBarCls: 'bg-amber-100/55 border-amber-700/40 text-slate-950',
      textBoxBg: 'bg-[#f8efd4]/95 text-slate-900 border-amber-700/60',
      isDarkTitle: true,
      isXyz: false,
      isLink: false,
      isPendulum,
      isSpell: false,
      isTrap: false
    };
  }

  return {
    kindLabel: isPendulum ? '灵摆·效果' : '效果怪兽',
    dotColor: 'bg-orange-400',
    cardOuterBg: isPendulum
      ? 'bg-gradient-to-b from-[#c46331] via-[#9e491f] to-[#146e60]'
      : 'bg-gradient-to-b from-[#c76634] via-[#a34c21] to-[#6e2f11]',
    cardInnerBorder: 'border-orange-300/45',
    titleBarCls: 'bg-amber-950/35 border-orange-300/40 text-amber-50',
    textBoxBg: 'bg-[#f6e4d4]/95 text-slate-900 border-amber-800/60',
    isDarkTitle: false,
    isXyz: false,
    isLink: false,
    isPendulum,
    isSpell: false,
    isTrap: false
  };
}

export function formatCardBracketLine(card: Pick<EditableDiyCard, 'type' | 'race'>): string {
  const t = card.type || 0;
  if ((t & CARD_BITS.SPELL) !== 0 && (t & CARD_BITS.MONSTER) === 0) {
    if ((t & CARD_BITS.QUICKPLAY) !== 0) return '【魔法卡 ⚡速攻】';
    if ((t & CARD_BITS.CONTINUOUS) !== 0) return '【魔法卡 ∞永续】';
    if ((t & CARD_BITS.EQUIP) !== 0) return '【魔法卡 ✚装备】';
    if ((t & CARD_BITS.FIELD) !== 0) return '【魔法卡 ✦场地】';
    if ((t & CARD_BITS.RITUAL) !== 0) return '【魔法卡 🔥仪式】';
    return '【魔法卡】';
  }
  if ((t & CARD_BITS.TRAP) !== 0 && (t & CARD_BITS.MONSTER) === 0) {
    if ((t & CARD_BITS.COUNTER) !== 0) return '【陷阱卡 ↩反击】';
    if ((t & CARD_BITS.CONTINUOUS) !== 0) return '【陷阱卡 ∞永续】';
    return '【陷阱卡】';
  }

  const raceItem = YGO_RACES.find((r) => r.value === card.race && r.value !== 0);
  const parts: string[] = [raceItem ? raceItem.label : '未定种族'];
  if ((t & CARD_BITS.FUSION) !== 0) parts.push('融合');
  if ((t & CARD_BITS.SYNCHRO) !== 0) parts.push('同调');
  if ((t & CARD_BITS.XYZ) !== 0) parts.push('超量');
  if ((t & CARD_BITS.LINK) !== 0) parts.push('连接');
  if ((t & CARD_BITS.RITUAL) !== 0) parts.push('仪式');
  if ((t & CARD_BITS.SPSUMMON) !== 0) parts.push('特殊召唤');
  if ((t & CARD_BITS.PENDULUM) !== 0) parts.push('灵摆');
  if ((t & CARD_BITS.TOON) !== 0) parts.push('卡通');
  if ((t & CARD_BITS.SPIRIT) !== 0) parts.push('灵魂');
  if ((t & CARD_BITS.UNION) !== 0) parts.push('同盟');
  if ((t & CARD_BITS.GEMINI) !== 0) parts.push('二重');
  if ((t & CARD_BITS.FLIP) !== 0) parts.push('反转');
  if ((t & CARD_BITS.TUNER) !== 0) parts.push('调整');
  if ((t & CARD_BITS.TOKEN) !== 0) parts.push('衍生物');
  if ((t & CARD_BITS.EFFECT) !== 0) parts.push('效果');
  else if ((t & CARD_BITS.NORMAL) !== 0) parts.push('通常');

  return `【${parts.join('／')}】`;
}

export function splitPendulumAndMonsterDesc(desc: string): { pendulumEffect: string; monsterEffect: string } {
  const raw = String(desc || '');
  const m = raw.match(/【灵摆效果】\s*([\s\S]*?)\s*【怪兽效果】\s*([\s\S]*)/);
  if (m) {
    return { pendulumEffect: m[1].trim(), monsterEffect: m[2].trim() };
  }
  const m2 = raw.match(/\[灵摆效果\]\s*([\s\S]*?)\s*\[怪兽效果\]\s*([\s\S]*)/);
  if (m2) {
    return { pendulumEffect: m2[1].trim(), monsterEffect: m2[2].trim() };
  }
  return { pendulumEffect: '', monsterEffect: raw };
}

export function combinePendulumAndMonsterDesc(pendulumEffect: string, monsterEffect: string): string {
  const p = pendulumEffect.trim();
  const m = monsterEffect.trim();
  if (!p) return m;
  return `【灵摆效果】\n${p}\n【怪兽效果】\n${m}`;
}

export function formatStatDisplay(stat: number): string {
  if (stat === -2) return '?';
  return String(Math.max(0, Number(stat) || 0));
}

export function generateSmartLuaTemplate(card: EditableDiyCard): string {
  const id = Number(card.id) || 99000001;
  const t = Number(card.type) || 0;
  const nameComment = card.name ? `-- ${card.name} (c${id})` : `-- c${id}.lua`;

  if ((t & CARD_BITS.SPELL) !== 0 && (t & CARD_BITS.MONSTER) === 0) {
    const isContOrFieldOrEquip =
      (t & (CARD_BITS.CONTINUOUS | CARD_BITS.FIELD | CARD_BITS.EQUIP)) !== 0;
    return `${nameComment}
local s,id=GetID()
function s.initial_effect(c)
\t-- 魔法卡发动
\tlocal e1=Effect.CreateEffect(c)
\te1:SetType(EFFECT_TYPE_ACTIVATE)
\te1:SetCode(EVENT_FREE_CHAIN)
\te1:SetCountLimit(1,id,EFFECT_COUNT_CODE_OATH)${
      isContOrFieldOrEquip
        ? '\n\tc:RegisterEffect(e1)\n\t-- 场上起动效果\n\tlocal e2=Effect.CreateEffect(c)\n\te2:SetDescription(aux.Stringid(id,0))\n\te2:SetCategory(CATEGORY_TOHAND+CATEGORY_SEARCH)\n\te2:SetType(EFFECT_TYPE_IGNITION)\n\te2:SetRange(LOCATION_SZONE)\n\te2:SetCountLimit(1,{id,1})\n\te2:SetTarget(s.thtg)\n\te2:SetOperation(s.thop)\n\tc:RegisterEffect(e2)'
        : '\n\te1:SetCategory(CATEGORY_DRAW)\n\te1:SetTarget(s.target)\n\te1:SetOperation(s.activate)\n\tc:RegisterEffect(e1)'
    }
end
${
  isContOrFieldOrEquip
    ? `function s.thtg(e,tp,eg,ep,ev,re,r,rp,chk)
\tif chk==0 then return Duel.IsPlayerCanDraw(tp,1) end
\tDuel.SetOperationInfo(0,CATEGORY_DRAW,nil,0,tp,1)
end
function s.thop(e,tp,eg,ep,ev,re,r,rp)
\tif not e:GetHandler():IsRelateToEffect(e) then return end
\tDuel.Draw(tp,1,REASON_EFFECT)
end`
    : `function s.target(e,tp,eg,ep,ev,re,r,rp,chk)
\tif chk==0 then return Duel.IsPlayerCanDraw(tp,1) end
\tDuel.SetOperationInfo(0,CATEGORY_DRAW,nil,0,tp,1)
end
function s.activate(e,tp,eg,ep,ev,re,r,rp)
\tDuel.Draw(tp,1,REASON_EFFECT)
end`
}
`;
  }

  if ((t & CARD_BITS.TRAP) !== 0 && (t & CARD_BITS.MONSTER) === 0) {
    const isCounter = (t & CARD_BITS.COUNTER) !== 0;
    return `${nameComment}
local s,id=GetID()
function s.initial_effect(c)
\tlocal e1=Effect.CreateEffect(c)
\te1:SetCategory(${isCounter ? 'CATEGORY_NEGATE+CATEGORY_DESTROY' : 'CATEGORY_DESTROY'})
\te1:SetType(EFFECT_TYPE_ACTIVATE)
\te1:SetCode(${isCounter ? 'EVENT_CHAINING' : 'EVENT_FREE_CHAIN'})
\te1:SetCountLimit(1,id,EFFECT_COUNT_CODE_OATH)
${isCounter ? '\te1:SetCondition(s.condition)\n' : ''}\te1:SetTarget(s.target)
\te1:SetOperation(s.activate)
\tc:RegisterEffect(e1)
end
${
  isCounter
    ? `function s.condition(e,tp,eg,ep,ev,re,r,rp)
\treturn ep==1-tp and Duel.IsChainNegatable(ev)
end
function s.target(e,tp,eg,ep,ev,re,r,rp,chk)
\tif chk==0 then return true end
\tDuel.SetOperationInfo(0,CATEGORY_NEGATE,eg,1,0,0)
end
function s.activate(e,tp,eg,ep,ev,re,r,rp)
\tif Duel.NegateActivation(ev) and re:GetHandler():IsRelateToEffect(re) then
\t\tDuel.Destroy(eg,REASON_EFFECT)
\tend
end`
    : `function s.target(e,tp,eg,ep,ev,re,r,rp,chk)
\tif chk==0 then return Duel.IsExistingMatchingCard(aux.TRUE,tp,0,LOCATION_ONFIELD,1,nil) end
\tlocal g=Duel.GetMatchingGroup(aux.TRUE,tp,0,LOCATION_ONFIELD,nil)
\tDuel.SetOperationInfo(0,CATEGORY_DESTROY,g,1,0,0)
end
function s.activate(e,tp,eg,ep,ev,re,r,rp)
\tDuel.Hint(HINT_SELECTMSG,tp,HINTMSG_DESTROY)
\tlocal g=Duel.SelectMatchingCard(tp,aux.TRUE,tp,0,LOCATION_ONFIELD,1,1,nil)
\tif #g>0 then
\t\tDuel.Destroy(g,REASON_EFFECT)
\tend
end`
}
`;
  }

  let summonProc = '';
  if ((t & CARD_BITS.LINK) !== 0) {
    const lk = Math.max(1, countLinkArrows(card.linkMarker) || card.level || 2);
    summonProc = `\tc:EnableReviveLimit()\n\tLink.AddProcedure(c,aux.FilterBoolFunctionEx(Card.IsType,TYPE_EFFECT),2,${lk})\n`;
  } else if ((t & CARD_BITS.SYNCHRO) !== 0) {
    summonProc = `\tc:EnableReviveLimit()\n\tSynchro.AddProcedure(c,nil,1,1,Synchro.NonTuner(nil),1,99)\n`;
  } else if ((t & CARD_BITS.XYZ) !== 0) {
    const rk = Math.max(1, card.level || 4);
    summonProc = `\tc:EnableReviveLimit()\n\tXyz.AddProcedure(c,nil,${rk},2)\n`;
  } else if ((t & CARD_BITS.FUSION) !== 0) {
    summonProc = `\tc:EnableReviveLimit()\n\tFusion.AddProcMixN(c,true,true,aux.FilterBoolFunctionEx(Card.IsType,TYPE_EFFECT),2)\n`;
  } else if ((t & CARD_BITS.PENDULUM) !== 0) {
    summonProc = `\tPendulum.AddProcedure(c)\n`;
  }

  return `${nameComment}
local s,id=GetID()
function s.initial_effect(c)
${summonProc}\t-- ①：召唤·特殊召唤成功的场合才能发动
\tlocal e1=Effect.CreateEffect(c)
\te1:SetDescription(aux.Stringid(id,0))
\te1:SetCategory(CATEGORY_DRAW)
\te1:SetType(EFFECT_TYPE_SINGLE+EFFECT_TYPE_TRIGGER_O)
\te1:SetProperty(EFFECT_FLAG_DELAY)
\te1:SetCode(EVENT_SUMMON_SUCCESS)
\te1:SetCountLimit(1,id)
\te1:SetTarget(s.drtg)
\te1:SetOperation(s.drop)
\tc:RegisterEffect(e1)
\tlocal e2=e1:Clone()
\te2:SetCode(EVENT_SPSUMMON_SUCCESS)
\tc:RegisterEffect(e2)
end
function s.drtg(e,tp,eg,ep,ev,re,r,rp,chk)
\tif chk==0 then return Duel.IsPlayerCanDraw(tp,1) end
\tDuel.SetOperationInfo(0,CATEGORY_DRAW,nil,0,tp,1)
end
function s.drop(e,tp,eg,ep,ev,re,r,rp)
\tDuel.Draw(tp,1,REASON_EFFECT)
end
`;
}
