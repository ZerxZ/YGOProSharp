import { useEffect, useMemo, useRef, useState } from 'react';
import { basicSetup } from 'codemirror';
import { Compartment, EditorState, Extension } from '@codemirror/state';
import { EditorView, keymap, placeholder as cmPlaceholder } from '@codemirror/view';
import { indentWithTab } from '@codemirror/commands';
import { StreamLanguage } from '@codemirror/language';
import { lua } from '@codemirror/legacy-modes/mode/lua';
import { Completion, CompletionContext } from '@codemirror/autocomplete';
import { Diagnostic, linter } from '@codemirror/lint';
import { oneDark } from '@codemirror/theme-one-dark';
import { Check, Copy, WrapText } from 'lucide-react';

export type CodeMirrorLang = 'lua' | 'ydk' | 'conf';

const OCGCORE_LUA_COMPLETIONS: Completion[] = [
  // 常用框架与函数
  { label: 'GetID', type: 'function', detail: 'local s,id=GetID()', apply: 'GetID()' },
  { label: 's.initial_effect', type: 'function', detail: '卡片主入口函数', apply: 'function s.initial_effect(c)\n\t\nend' },
  { label: 'aux.Stringid', type: 'function', detail: '(id, offset) 提示文本编号', apply: 'aux.Stringid(id, 0)' },
  { label: 'aux.AddSynchroProcedure', type: 'function', detail: '同调召唤手续' },
  { label: 'aux.AddXyzProcedure', type: 'function', detail: '超量召唤手续' },
  { label: 'aux.AddLinkProcedure', type: 'function', detail: '连接召唤手续' },
  // Duel.*
  { label: 'Duel.Hint', type: 'function', detail: '(hint_type, player, desc)' },
  { label: 'Duel.IsExistingTarget', type: 'function', detail: '(filter, tp, s, o, count, ex, ...)' },
  { label: 'Duel.SelectTarget', type: 'function', detail: '(sel_p, filter, tp, s, o, min, max, ex, ...)' },
  { label: 'Duel.IsExistingMatchingCard', type: 'function', detail: '(filter, tp, s, o, count, ex, ...)' },
  { label: 'Duel.SelectMatchingCard', type: 'function', detail: '(sel_p, filter, tp, s, o, min, max, ex, ...)' },
  { label: 'Duel.GetMatchingGroup', type: 'function', detail: '(filter, tp, s, o, ex, ...)' },
  { label: 'Duel.GetFirstTarget', type: 'function', detail: '获取单个对象卡' },
  { label: 'Duel.GetLocationCount', type: 'function', detail: '(tp, location) 可用格子数' },
  { label: 'Duel.SetOperationInfo', type: 'function', detail: '(ch, cat, targets, count, p, val)' },
  { label: 'Duel.SpecialSummon', type: 'function', detail: '(targets, sumtype, sumplayer, target_p, nocheck, nolimit, pos)' },
  { label: 'Duel.SendtoHand', type: 'function', detail: '(targets, player, reason)' },
  { label: 'Duel.SendtoGrave', type: 'function', detail: '(targets, reason)' },
  { label: 'Duel.SendtoDeck', type: 'function', detail: '(targets, player, seq, reason)' },
  { label: 'Duel.Remove', type: 'function', detail: '(targets, pos, reason)' },
  { label: 'Duel.Destroy', type: 'function', detail: '(targets, reason)' },
  { label: 'Duel.Draw', type: 'function', detail: '(player, count, reason)' },
  { label: 'Duel.Recover', type: 'function', detail: '(player, value, reason)' },
  { label: 'Duel.Damage', type: 'function', detail: '(player, value, reason)' },
  { label: 'Duel.NegateActivation', type: 'function', detail: '(chainc) 发动无效' },
  { label: 'Duel.NegateEffect', type: 'function', detail: '(chainc) 效果无效' },
  { label: 'Duel.CheckLPCost', type: 'function', detail: '(player, cost)' },
  { label: 'Duel.PayLPCost', type: 'function', detail: '(player, cost)' },
  { label: 'Duel.ConfirmCards', type: 'function', detail: '(player, targets)' },
  // Effect.*
  { label: 'Effect.CreateEffect', type: 'function', detail: '(c) 创建卡片效果', apply: 'Effect.CreateEffect(c)' },
  { label: 'SetDescription', type: 'method', detail: 'e:SetDescription(aux.Stringid(id,0))' },
  { label: 'SetCategory', type: 'method', detail: 'e:SetCategory(CATEGORY_...)' },
  { label: 'SetType', type: 'method', detail: 'e:SetType(EFFECT_TYPE_...)' },
  { label: 'SetProperty', type: 'method', detail: 'e:SetProperty(EFFECT_FLAG_...)' },
  { label: 'SetCode', type: 'method', detail: 'e:SetCode(EVENT_...)' },
  { label: 'SetRange', type: 'method', detail: 'e:SetRange(LOCATION_...)' },
  { label: 'SetCountLimit', type: 'method', detail: 'e:SetCountLimit(1, id)' },
  { label: 'SetCondition', type: 'method', detail: 'e:SetCondition(s.con)' },
  { label: 'SetCost', type: 'method', detail: 'e:SetCost(s.cost)' },
  { label: 'SetTarget', type: 'method', detail: 'e:SetTarget(s.tg)' },
  { label: 'SetOperation', type: 'method', detail: 'e:SetOperation(s.op)' },
  { label: 'SetValue', type: 'method', detail: 'e:SetValue(val)' },
  { label: 'SetReset', type: 'method', detail: 'e:SetReset(RESET_EVENT+RESETS_STANDARD)' },
  { label: 'RegisterEffect', type: 'method', detail: 'c:RegisterEffect(e1)' },
  // 常用 OCG 常量
  { label: 'EFFECT_TYPE_ACTIVATE', type: 'constant', detail: '魔陷发动' },
  { label: 'EFFECT_TYPE_IGNITION', type: 'constant', detail: '起动效果' },
  { label: 'EFFECT_TYPE_QUICK_O', type: 'constant', detail: '诱发即时（选发）' },
  { label: 'EFFECT_TYPE_TRIGGER_O', type: 'constant', detail: '诱发效果（选发）' },
  { label: 'EFFECT_TYPE_TRIGGER_F', type: 'constant', detail: '诱发效果（必发）' },
  { label: 'EFFECT_TYPE_SINGLE', type: 'constant', detail: '单体永续/自身效果' },
  { label: 'EFFECT_TYPE_FIELD', type: 'constant', detail: '场地型效果' },
  { label: 'EFFECT_FLAG_DELAY', type: 'constant', detail: '场合型延迟诱发' },
  { label: 'EFFECT_FLAG_CARD_TARGET', type: 'constant', detail: '取对象效果' },
  { label: 'EFFECT_FLAG_DAMAGE_STEP', type: 'constant', detail: '伤害步骤可发动' },
  { label: 'EVENT_FREE_CHAIN', type: 'constant', detail: '自由时点' },
  { label: 'EVENT_SUMMON_SUCCESS', type: 'constant', detail: '通常召唤成功时' },
  { label: 'EVENT_SPSUMMON_SUCCESS', type: 'constant', detail: '特殊召唤成功时' },
  { label: 'EVENT_TO_GRAVE', type: 'constant', detail: '送去墓地时' },
  { label: 'EVENT_CHAINING', type: 'constant', detail: '效果发动连锁时' },
  { label: 'LOCATION_HAND', type: 'constant', detail: '手牌' },
  { label: 'LOCATION_MZONE', type: 'constant', detail: '怪兽区域' },
  { label: 'LOCATION_SZONE', type: 'constant', detail: '魔法与陷阱区域' },
  { label: 'LOCATION_GRAVE', type: 'constant', detail: '墓地' },
  { label: 'LOCATION_DECK', type: 'constant', detail: '主卡组' },
  { label: 'LOCATION_EXTRA', type: 'constant', detail: '额外卡组' },
  { label: 'LOCATION_REMOVED', type: 'constant', detail: '除外状态' },
  { label: 'LOCATION_ONFIELD', type: 'constant', detail: '场上' },
  { label: 'CATEGORY_SEARCH', type: 'constant', detail: '卡组检索类别' },
  { label: 'CATEGORY_TOHAND', type: 'constant', detail: '加入/返回手牌类别' },
  { label: 'CATEGORY_SPECIAL_SUMMON', type: 'constant', detail: '特殊召唤类别' },
  { label: 'CATEGORY_DESTROY', type: 'constant', detail: '破坏类别' },
  { label: 'CATEGORY_NEGATE', type: 'constant', detail: '发动无效类别' },
  { label: 'CATEGORY_DISABLE', type: 'constant', detail: '效果无效类别' },
  { label: 'CATEGORY_DRAW', type: 'constant', detail: '抽卡类别' },
  { label: 'CATEGORY_RECOVER', type: 'constant', detail: '回复基本分类别' },
  { label: 'CATEGORY_DAMAGE', type: 'constant', detail: '效果伤害类别' },
  { label: 'CATEGORY_TOGRAVE', type: 'constant', detail: '送去墓地类别' },
  { label: 'CATEGORY_REMOVE', type: 'constant', detail: '除外类别' },
  { label: 'REASON_EFFECT', type: 'constant', detail: '因效果处理' },
  { label: 'REASON_COST', type: 'constant', detail: '作为代价支付' },
  { label: 'POS_FACEUP', type: 'constant', detail: '表侧表示' },
  { label: 'POS_FACEUP_ATTACK', type: 'constant', detail: '表侧攻击表示' },
  { label: 'POS_FACEUP_DEFENSE', type: 'constant', detail: '表侧守备表示' },
  { label: 'RESET_EVENT', type: 'constant', detail: '事件重置标志' },
  { label: 'RESETS_STANDARD', type: 'constant', detail: '标准离场重置组合' },
  { label: 'RESET_PHASE', type: 'constant', detail: '阶段重置标志' },
  { label: 'PHASE_END', type: 'constant', detail: '结束阶段' }
];

const YDK_COMPLETIONS: Completion[] = [
  { label: '#created by YGOProSharp', type: 'keyword', detail: '卡组文件头注释' },
  { label: '#main', type: 'keyword', detail: '主卡组段 (40~60 张)' },
  { label: '#extra', type: 'keyword', detail: '额外卡组段 (0~15 张)' },
  { label: '!side', type: 'keyword', detail: '副卡组段 (0~15 张)' }
];

const CONF_COMPLETIONS: Completion[] = [
  { label: '!setname', type: 'keyword', detail: '自定义字段声明 (!setname 0xa100 字段名)', apply: '!setname 0x' },
  { label: '!counter', type: 'keyword', detail: '自定义指示物声明 (!counter 0x1900 指示物名)', apply: '!counter 0x' },
  { label: '!system', type: 'keyword', detail: '系统字符串声明', apply: '!system ' },
  { label: '!victory', type: 'keyword', detail: '特殊胜利提示声明', apply: '!victory 0x' }
];

const luaStreamLang = StreamLanguage.define(lua);

const ydkStreamLang = StreamLanguage.define({
  startState: () => ({}),
  token(stream) {
    if (stream.eatSpace()) return null;
    if (stream.sol()) {
      if (stream.match(/^#(main|extra)\b/i) || stream.match(/^!side\b/i)) {
        stream.skipToEnd();
        return 'keyword';
      }
      if (stream.peek() === '#') {
        stream.skipToEnd();
        return 'comment';
      }
    }
    if (stream.match(/^\d+/)) {
      return 'number';
    }
    stream.next();
    return 'invalid';
  }
});

const confStreamLang = StreamLanguage.define({
  startState: () => ({}),
  token(stream) {
    if (stream.eatSpace()) return null;
    if (stream.sol() && stream.peek() === '#') {
      stream.skipToEnd();
      return 'comment';
    }
    if (stream.sol() && stream.match(/^!(setname|counter|system|victory)\b/)) {
      return 'keyword';
    }
    if (stream.match(/^0x[0-9a-fA-F]+/) || stream.match(/^\d+/)) {
      return 'number';
    }
    stream.skipToEnd();
    return 'string';
  }
});

const FORBIDDEN_LUA_PATTERNS: { regex: RegExp; message: string }[] = [
  { regex: /\bos\s*\./g, message: '沙箱安全策略禁止调用 os.* 系统接口' },
  { regex: /\bio\s*\./g, message: '沙箱安全策略禁止调用 io.* 文件接口' },
  { regex: /\bpackage\s*\./g, message: '沙箱安全策略禁止访问 package.* 模块加载器' },
  { regex: /\bdebug\s*\./g, message: '沙箱安全策略禁止调用 debug.* 调试反射接口' },
  { regex: /\b(dofile|loadfile|require|luanet)\b/g, message: '沙箱安全策略禁止使用外部脚本加载或宿主桥接函数' }
];

const luaSecurityLinter = linter((view) => {
  const diagnostics: Diagnostic[] = [];
  const docText = view.state.doc.toString();
  for (const rule of FORBIDDEN_LUA_PATTERNS) {
    rule.regex.lastIndex = 0;
    let m: RegExpExecArray | null;
    while ((m = rule.regex.exec(docText)) !== null) {
      diagnostics.push({
        from: m.index,
        to: m.index + m[0].length,
        severity: 'error',
        message: rule.message
      });
    }
  }
  return diagnostics;
});

function buildCompletionSource(lang: CodeMirrorLang) {
  return (context: CompletionContext) => {
    if (lang === 'lua') {
      const word = context.matchBefore(/[A-Za-z_][A-Za-z0-9_.]*/);
      if (!word || (word.from === word.to && !context.explicit)) return null;
      return {
        from: word.from,
        options: OCGCORE_LUA_COMPLETIONS,
        validFor: /^[A-Za-z0-9_.]*$/
      };
    }
    const lineWord = context.matchBefore(/^[#!A-Za-z0-9_]*/);
    if (!lineWord || (lineWord.from === lineWord.to && !context.explicit)) return null;
    return {
      from: lineWord.from,
      options: lang === 'ydk' ? YDK_COMPLETIONS : CONF_COMPLETIONS,
      validFor: /^[#!A-Za-z0-9_]*$/
    };
  };
}

function summarizeYdk(text: string): { main: number; extra: number; side: number } {
  let section: 'main' | 'extra' | 'side' = 'main';
  let main = 0;
  let extra = 0;
  let side = 0;
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim();
    if (!line) continue;
    const lower = line.toLowerCase();
    if (lower === '#main') {
      section = 'main';
    } else if (lower === '#extra') {
      section = 'extra';
    } else if (lower === '!side') {
      section = 'side';
    } else if (/^\d+$/.test(line)) {
      if (section === 'main') main++;
      else if (section === 'extra') extra++;
      else side++;
    }
  }
  return { main, extra, side };
}

function createGlassEditorTheme(minHeight: string, maxHeight: string, accent: 'emerald' | 'indigo') {
  const focusBorder = accent === 'emerald' ? 'rgba(52, 211, 153, 0.75)' : 'rgba(129, 140, 248, 0.8)';
  const focusRing = accent === 'emerald' ? 'rgba(16, 185, 129, 0.16)' : 'rgba(99, 102, 241, 0.18)';

  return EditorView.theme(
    {
      '&': {
        backgroundColor: 'rgba(2, 6, 23, 0.86)',
        fontSize: '12px',
        borderRadius: '0 0 0.75rem 0.75rem'
      },
      '&.cm-focused': {
        outline: 'none',
        boxShadow: `inset 0 0 0 1px ${focusBorder}, 0 0 0 3px ${focusRing}`
      },
      '.cm-scroller': {
        minHeight,
        maxHeight,
        overflow: 'auto',
        fontFamily:
          'ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace',
        lineHeight: '1.6'
      },
      '.cm-content': {
        padding: '10px 0'
      },
      '.cm-gutters': {
        backgroundColor: 'rgba(15, 23, 42, 0.75)',
        borderRight: '1px solid rgba(51, 65, 85, 0.55)',
        color: '#64748b'
      },
      '.cm-activeLineGutter': {
        backgroundColor: 'rgba(30, 41, 59, 0.7)',
        color: '#e2e8f0'
      },
      '.cm-activeLine': {
        backgroundColor: 'rgba(30, 41, 59, 0.35)'
      },
      '.cm-tooltip': {
        backgroundColor: '#0f172a',
        border: '1px solid rgba(100, 116, 139, 0.45)',
        borderRadius: '0.5rem'
      }
    },
    { dark: true }
  );
}

export interface CodeMirrorEditorProps {
  value: string;
  onChange: (nextValue: string) => void;
  language: CodeMirrorLang;
  accent?: 'emerald' | 'indigo';
  title?: string;
  placeholder?: string;
  minHeight?: string;
  maxHeight?: string;
  readOnly?: boolean;
  compact?: boolean;
}

export function CodeMirrorEditor({
  value,
  onChange,
  language,
  accent = 'emerald',
  title,
  placeholder,
  minHeight = '160px',
  maxHeight = '420px',
  readOnly = false,
  compact = false
}: CodeMirrorEditorProps) {
  const hostRef = useRef<HTMLDivElement | null>(null);
  const viewRef = useRef<EditorView | null>(null);
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;

  const wrapCompartmentRef = useRef(new Compartment());
  const readOnlyCompartmentRef = useRef(new Compartment());

  const [lineWrap, setLineWrap] = useState(true);
  const [copied, setCopied] = useState(false);

  const ydkCounts = useMemo(() => (language === 'ydk' ? summarizeYdk(value) : null), [language, value]);
  const lineCount = useMemo(() => (value ? value.split(/\r?\n/).length : 1), [value]);

  useEffect(() => {
    if (!hostRef.current) return;

    const langExt: Extension =
      language === 'lua'
        ? [luaStreamLang, luaSecurityLinter]
        : language === 'ydk'
          ? ydkStreamLang
          : confStreamLang;

    const extensions: Extension[] = [
      basicSetup,
      keymap.of([indentWithTab]),
      oneDark,
      createGlassEditorTheme(minHeight, maxHeight, accent),
      langExt,
      EditorState.languageData.of(() => [{ autocomplete: buildCompletionSource(language) }]),
      wrapCompartmentRef.current.of(lineWrap ? EditorView.lineWrapping : []),
      readOnlyCompartmentRef.current.of(EditorState.readOnly.of(readOnly)),
      EditorView.updateListener.of((update) => {
        if (update.docChanged) {
          onChangeRef.current(update.state.doc.toString());
        }
      })
    ];

    if (placeholder) {
      extensions.push(cmPlaceholder(placeholder));
    }

    const state = EditorState.create({
      doc: value,
      extensions
    });

    const view = new EditorView({
      state,
      parent: hostRef.current
    });
    viewRef.current = view;

    return () => {
      view.destroy();
      viewRef.current = null;
    };
    // 仅在语言或尺寸主题变更时重建实例，文本同步通过下方事务完成以保持光标位置稳定
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [language, accent, minHeight, maxHeight, placeholder]);

  useEffect(() => {
    const view = viewRef.current;
    if (!view) return;
    const currentText = view.state.doc.toString();
    if (value !== currentText) {
      view.dispatch({
        changes: { from: 0, to: currentText.length, insert: value }
      });
    }
  }, [value]);

  useEffect(() => {
    const view = viewRef.current;
    if (!view) return;
    view.dispatch({
      effects: wrapCompartmentRef.current.reconfigure(lineWrap ? EditorView.lineWrapping : [])
    });
  }, [lineWrap]);

  useEffect(() => {
    const view = viewRef.current;
    if (!view) return;
    view.dispatch({
      effects: readOnlyCompartmentRef.current.reconfigure(EditorState.readOnly.of(readOnly))
    });
  }, [readOnly]);

  const langLabel = language === 'lua' ? 'Lua 5.4 · OCGCore' : language === 'ydk' ? 'YDK 卡组' : 'strings.conf';

  return (
    <div className="rounded-xl border border-slate-700/75 bg-slate-950/85 overflow-hidden shadow-inner">
      <div
        className={`flex items-center justify-between gap-2 px-3 ${
          compact ? 'py-1.5' : 'py-2'
        } bg-slate-900/90 border-b border-slate-800/90 text-[11px]`}
      >
        <div className="flex items-center gap-2 min-w-0">
          <span
            className={`px-1.5 py-0.5 rounded font-mono text-[10px] font-semibold shrink-0 ${
              accent === 'emerald'
                ? 'bg-emerald-500/15 text-emerald-300 border border-emerald-500/30'
                : 'bg-indigo-500/15 text-indigo-300 border border-indigo-500/30'
            }`}
          >
            {langLabel}
          </span>
          {title && <span className="font-mono text-slate-300 truncate">{title}</span>}
          {ydkCounts && (
            <span className="text-slate-400 font-mono truncate">
              主:{ydkCounts.main} · 额:{ydkCounts.extra} · 副:{ydkCounts.side}
            </span>
          )}
        </div>

        <div className="flex items-center gap-1.5 shrink-0 text-slate-400">
          <span className="hidden sm:inline font-mono text-[10px]">{lineCount} 行</span>
          <button
            type="button"
            onClick={() => setLineWrap((w) => !w)}
            className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-md border text-[10px] transition cursor-pointer ${
              lineWrap
                ? 'bg-slate-800 border-slate-700 text-slate-200'
                : 'bg-slate-950 border-slate-800 text-slate-500 hover:text-slate-300'
            }`}
            title="切换自动折行"
          >
            <WrapText className="w-3 h-3" />
            <span className="hidden sm:inline">折行</span>
          </button>
          <button
            type="button"
            onClick={() => {
              navigator.clipboard?.writeText(value);
              setCopied(true);
              setTimeout(() => setCopied(false), 1400);
            }}
            className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md bg-slate-800/80 hover:bg-slate-700 border border-slate-700/80 text-[10px] text-slate-200 transition cursor-pointer"
            title="复制全部代码"
          >
            {copied ? <Check className="w-3 h-3 text-emerald-400" /> : <Copy className="w-3 h-3" />}
            <span>{copied ? '已复制' : '复制'}</span>
          </button>
        </div>
      </div>

      <div ref={hostRef} />
    </div>
  );
}
