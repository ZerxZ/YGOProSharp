import React, { useEffect, useMemo, useState } from 'react';
import {
  AlertTriangle,
  CheckCircle2,
  Code2,
  Database,
  FileCode2,
  FileText,
  FolderOpen,
  Image as ImageIcon,
  Layers,
  Music,
  Search,
  Sparkles,
  X
} from 'lucide-react';
import { apiFetch } from '../api';
import { CodeMirrorEditor, CodeMirrorLang } from '../components/CodeMirrorEditor';
import {
  CARD_BITS,
  countLinkArrows,
  EditableDiyCard,
  formatStatDisplay,
  resolveCardFrameTheme
} from './ygoCardTaxonomy';
import { YgoCardVisualPreview } from './YgoCardVisualPreview';

export interface YpkInspectionReport {
  archiveName: string;
  isSafe: boolean;
  compressedBytes: number;
  uncompressedBytes: number;
  totalFiles: number;
  cardCount: number;
  scriptCount: number;
  imageCount: number;
  deckCount: number;
  confCount: number;
  categories: { category: string; fileCount: number; totalBytes: number }[];
  files: {
    path: string;
    name: string;
    folder: string;
    category: string;
    compressedBytes: number;
    uncompressedBytes: number;
  }[];
  cards: {
    card: any;
    hasScript: boolean;
    hasImage: boolean;
  }[];
  decks: {
    name: string;
    path: string;
    mainCount: number;
    extraCount: number;
    sideCount: number;
  }[];
  stringsConf: {
    rawContent: string;
    setnames: { kind: string; codeHex: string; name: string }[];
    counters: { kind: string; codeHex: string; name: string }[];
  };
  errors: string[];
}

export type YpkEntryResolver = {
  buildCardImageUrl: (cardId: number) => string;
  buildFileImageUrl: (filePath: string) => string;
  fetchTextFile: (filePath: string) => Promise<string>;
};

export function createPreviewTokenResolver(
  apiPrefix: '/api/portal' | '/api/v1',
  previewToken: string
): YpkEntryResolver {
  const base = `${apiPrefix}/diy/ypk-preview/${encodeURIComponent(previewToken)}/entry`;
  return {
    buildCardImageUrl: (cardId) => `${base}?cardId=${cardId}`,
    buildFileImageUrl: (filePath) => `${base}?path=${encodeURIComponent(filePath)}`,
    fetchTextFile: async (filePath) => {
      const res = await apiFetch<{ content: string }>(`${base}?path=${encodeURIComponent(filePath)}`);
      return res.content || '';
    }
  };
}

export function createInstalledPackageResolver(
  apiPrefix: '/api/portal' | '/api/v1',
  packageId: string
): YpkEntryResolver {
  const pkgEnc = encodeURIComponent(packageId);
  return {
    buildCardImageUrl: (cardId) => `${apiPrefix}/diy/packages/${pkgEnc}/cards/${cardId}/image`,
    buildFileImageUrl: (filePath) => `${apiPrefix}/diy/packages/${pkgEnc}/file?path=${encodeURIComponent(filePath)}`,
    fetchTextFile: async (filePath) => {
      const res = await apiFetch<{ content: string }>(
        `${apiPrefix}/diy/packages/${pkgEnc}/file?path=${encodeURIComponent(filePath)}`
      );
      return res.content || '';
    }
  };
}

export function createProposalYpkResolver(proposalId: string): YpkEntryResolver {
  const base = `/api/v1/diy/proposals/${encodeURIComponent(proposalId)}/ypk-entry`;
  return {
    buildCardImageUrl: (cardId) => `${base}?cardId=${cardId}`,
    buildFileImageUrl: (filePath) => `${base}?path=${encodeURIComponent(filePath)}`,
    fetchTextFile: async (filePath) => {
      const res = await apiFetch<{ content: string }>(`${base}?path=${encodeURIComponent(filePath)}`);
      return res.content || '';
    }
  };
}

export function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return '0 B';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
}

const CATEGORY_META: Record<string, { label: string; barCls: string; badgeCls: string; icon: React.ReactNode }> = {
  image: {
    label: '卡图与美术 (pics)',
    barCls: 'bg-sky-500',
    badgeCls: 'bg-sky-500/15 border-sky-500/30 text-sky-300',
    icon: <ImageIcon className="w-3.5 h-3.5 text-sky-400" />
  },
  lua: {
    label: 'Lua 效果脚本 (script)',
    barCls: 'bg-emerald-500',
    badgeCls: 'bg-emerald-500/15 border-emerald-500/30 text-emerald-300',
    icon: <FileCode2 className="w-3.5 h-3.5 text-emerald-400" />
  },
  cdb: {
    label: 'SQLite 卡表 (.cdb)',
    barCls: 'bg-amber-500',
    badgeCls: 'bg-amber-500/15 border-amber-500/30 text-amber-300',
    icon: <Database className="w-3.5 h-3.5 text-amber-400" />
  },
  ydk: {
    label: '预设卡组 (.ydk)',
    barCls: 'bg-purple-500',
    badgeCls: 'bg-purple-500/15 border-purple-500/30 text-purple-300',
    icon: <Layers className="w-3.5 h-3.5 text-purple-400" />
  },
  conf: {
    label: '字段与禁限配置 (.conf)',
    barCls: 'bg-indigo-500',
    badgeCls: 'bg-indigo-500/15 border-indigo-500/30 text-indigo-300',
    icon: <FileText className="w-3.5 h-3.5 text-indigo-400" />
  },
  audio: {
    label: '音效资源',
    barCls: 'bg-pink-500',
    badgeCls: 'bg-pink-500/15 border-pink-500/30 text-pink-300',
    icon: <Music className="w-3.5 h-3.5 text-pink-400" />
  },
  doc: {
    label: '说明文档',
    barCls: 'bg-slate-400',
    badgeCls: 'bg-slate-800 border-slate-700 text-slate-300',
    icon: <FileText className="w-3.5 h-3.5 text-slate-400" />
  },
  other: {
    label: '其他资源',
    barCls: 'bg-slate-600',
    badgeCls: 'bg-slate-800 border-slate-700 text-slate-400',
    icon: <FolderOpen className="w-3.5 h-3.5 text-slate-400" />
  }
};

function mapRawCardToEditable(raw: any): EditableDiyCard {
  return {
    id: Number(raw.id ?? raw.Id ?? 0),
    ot: Number(raw.ot ?? raw.Ot ?? 4),
    alias: Number(raw.alias ?? raw.Alias ?? 0),
    setcodeHex: '0x' + BigInt(raw.setcode ?? raw.Setcode ?? 0).toString(16),
    type: Number(raw.type ?? raw.Type ?? 0x21),
    level: Number(raw.level ?? raw.Level ?? 4),
    lScale: Number(raw.lScale ?? raw.LScale ?? 0),
    rScale: Number(raw.rScale ?? raw.RScale ?? 0),
    race: Number(raw.race ?? raw.Race ?? 1),
    attribute: Number(raw.attribute ?? raw.Attribute ?? 0x20),
    attack: Number(raw.attack ?? raw.Attack ?? 0),
    defense: Number(raw.defense ?? raw.Defense ?? 0),
    linkMarker: Number(raw.linkMarker ?? raw.LinkMarker ?? 0),
    name: String(raw.name ?? raw.Name ?? ''),
    desc: String(raw.desc ?? raw.Desc ?? ''),
    strings: Array.isArray(raw.strings ?? raw.Strings) ? (raw.strings ?? raw.Strings) : []
  };
}

export function YpkPackageExplorer({
  report,
  resolver,
  accent = 'emerald',
  actionFooter
}: {
  report: YpkInspectionReport;
  resolver: YpkEntryResolver;
  accent?: 'emerald' | 'indigo';
  actionFooter?: React.ReactNode;
}) {
  const [activeTab, setActiveTab] = useState<'cards' | 'files' | 'decks'>(() =>
    report.cardCount > 0 ? 'cards' : 'files'
  );

  // 卡片画廊状态
  const [cardSearch, setCardSearch] = useState('');
  const [cardTypeFilter, setCardTypeFilter] = useState<'all' | 'monster' | 'spell' | 'trap'>('all');
  const [selectedCardIdx, setSelectedCardIdx] = useState(0);
  const [cardScriptText, setCardScriptText] = useState<string | null>(null);
  const [loadingCardScript, setLoadingCardScript] = useState(false);

  // 文件资源管理器状态
  const [fileSearch, setFileSearch] = useState('');
  const [fileCatFilter, setFileCatFilter] = useState<string>('all');
  const [selectedFile, setSelectedFile] = useState<YpkInspectionReport['files'][number] | null>(null);
  const [filePreviewText, setFilePreviewText] = useState<string>('');
  const [loadingFilePreview, setLoadingFilePreview] = useState(false);

  useEffect(() => {
    setActiveTab(report.cardCount > 0 ? 'cards' : 'files');
    setSelectedCardIdx(0);
    setCardScriptText(null);
    const firstPreviewable =
      report.files.find((f) => f.category === 'lua' || f.category === 'conf' || f.category === 'ydk') ||
      report.files[0] ||
      null;
    setSelectedFile(firstPreviewable);
  }, [report]);

  const filteredCards = useMemo(() => {
    return (report.cards || []).filter((item) => {
      const c = item.card;
      const t = Number(c.type ?? c.Type ?? 0);
      if (cardTypeFilter === 'monster' && (t & CARD_BITS.MONSTER) === 0) return false;
      if (cardTypeFilter === 'spell' && ((t & CARD_BITS.SPELL) === 0 || (t & CARD_BITS.MONSTER) !== 0)) return false;
      if (cardTypeFilter === 'trap' && ((t & CARD_BITS.TRAP) === 0 || (t & CARD_BITS.MONSTER) !== 0)) return false;
      if (cardSearch.trim()) {
        const kw = cardSearch.trim().toLowerCase();
        const nm = String(c.name ?? c.Name ?? '').toLowerCase();
        const ds = String(c.desc ?? c.Desc ?? '').toLowerCase();
        const idStr = String(c.id ?? c.Id ?? '');
        return nm.includes(kw) || ds.includes(kw) || idStr.includes(kw);
      }
      return true;
    });
  }, [report.cards, cardTypeFilter, cardSearch]);

  const activeCardSummary = filteredCards[selectedCardIdx] || filteredCards[0] || null;
  const activeEditableCard = useMemo(
    () => (activeCardSummary ? mapRawCardToEditable(activeCardSummary.card) : null),
    [activeCardSummary]
  );

  useEffect(() => {
    setCardScriptText(null);
  }, [activeEditableCard?.id]);

  const loadActiveCardLua = async () => {
    if (!activeEditableCard) return;
    const targetLua = report.files.find(
      (f) => f.category === 'lua' && f.name.toLowerCase() === `c${activeEditableCard.id}.lua`
    );
    if (!targetLua) {
      setCardScriptText(`-- 包内未找到独立脚本 c${activeEditableCard.id}.lua`);
      return;
    }
    setLoadingCardScript(true);
    try {
      const txt = await resolver.fetchTextFile(targetLua.path);
      setCardScriptText(txt);
    } catch (err) {
      setCardScriptText(`-- 读取脚本失败: ${err instanceof Error ? err.message : 'unknown'}`);
    } finally {
      setLoadingCardScript(false);
    }
  };

  const filteredFiles = useMemo(() => {
    return (report.files || []).filter((f) => {
      if (fileCatFilter !== 'all' && f.category !== fileCatFilter) return false;
      if (fileSearch.trim()) {
        return f.path.toLowerCase().includes(fileSearch.trim().toLowerCase());
      }
      return true;
    });
  }, [report.files, fileCatFilter, fileSearch]);

  useEffect(() => {
    if (!selectedFile) return;
    if (selectedFile.category === 'image' || selectedFile.category === 'cdb' || selectedFile.category === 'audio') {
      setFilePreviewText('');
      return;
    }
    let cancelled = false;
    setLoadingFilePreview(true);
    resolver
      .fetchTextFile(selectedFile.path)
      .then((txt) => {
        if (!cancelled) setFilePreviewText(txt);
      })
      .catch((err) => {
        if (!cancelled) setFilePreviewText(`# 无法作为文本预览该文件 (${err instanceof Error ? err.message : 'binary'})`);
      })
      .finally(() => {
        if (!cancelled) setLoadingFilePreview(false);
      });
    return () => {
      cancelled = true;
    };
  }, [selectedFile, resolver]);

  const totalUncomp = Math.max(1, report.uncompressedBytes || 1);
  const activeBtnCls =
    accent === 'emerald' ? 'bg-emerald-600 text-white shadow' : 'bg-indigo-600 text-white shadow';

  const previewEditorLang: CodeMirrorLang =
    selectedFile?.category === 'ydk' ? 'ydk' : selectedFile?.category === 'conf' ? 'conf' : 'lua';

  return (
    <div className="space-y-4 text-xs animate-fade-in">
      {/* 1. 顶部包体摘要与可视化体积占比条 */}
      <div className="glass-subcard rounded-2xl p-4 space-y-3 border border-white/10">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div className="flex items-center gap-2 min-w-0">
            <span className="font-mono font-bold text-sm text-slate-100 truncate">{report.archiveName}</span>
            {report.isSafe ? (
              <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full bg-emerald-500/15 border border-emerald-500/35 text-emerald-300 text-[11px] font-semibold shrink-0">
                <CheckCircle2 className="w-3.5 h-3.5" /> 安全与结构预检通过
              </span>
            ) : (
              <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full bg-rose-500/20 border border-rose-500/40 text-rose-300 text-[11px] font-semibold shrink-0">
                <AlertTriangle className="w-3.5 h-3.5" /> 预检发现异常 ({report.errors.length})
              </span>
            )}
          </div>

          <div className="flex flex-wrap items-center gap-3 text-[11px] text-slate-300 font-mono">
            <span>共 {report.totalFiles} 个文件</span>
            <span>·</span>
            <span>压缩包 {formatBytes(report.compressedBytes)}</span>
            <span>·</span>
            <span>解压后 {formatBytes(report.uncompressedBytes)}</span>
          </div>
        </div>

        {/* 可视化资源构成条 */}
        {report.categories.length > 0 && (
          <div className="space-y-2">
            <div className="h-2.5 w-full rounded-full bg-slate-900 overflow-hidden flex gap-0.5 p-0.5 border border-slate-800">
              {report.categories.map((cat) => {
                const meta = CATEGORY_META[cat.category] || CATEGORY_META.other;
                const pct = Math.max(2, Math.round((cat.totalBytes / totalUncomp) * 100));
                return (
                  <div
                    key={cat.category}
                    className={`h-full rounded-full ${meta.barCls}`}
                    style={{ width: `${pct}%` }}
                    title={`${meta.label}: ${cat.fileCount} 个文件 (${formatBytes(cat.totalBytes)})`}
                  />
                );
              })}
            </div>
            <div className="flex flex-wrap gap-1.5">
              {report.categories.map((cat) => {
                const meta = CATEGORY_META[cat.category] || CATEGORY_META.other;
                return (
                  <button
                    key={cat.category}
                    type="button"
                    onClick={() => {
                      setActiveTab('files');
                      setFileCatFilter(cat.category);
                    }}
                    className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg border text-[11px] cursor-pointer transition hover:brightness-110 ${meta.badgeCls}`}
                  >
                    {meta.icon}
                    <span>{meta.label}</span>
                    <span className="font-mono font-bold">{cat.fileCount}</span>
                    <span className="opacity-70 font-mono text-[10px]">({formatBytes(cat.totalBytes)})</span>
                  </button>
                );
              })}
            </div>
          </div>
        )}

        {report.errors.length > 0 && (
          <div className="p-3 rounded-xl bg-rose-950/60 border border-rose-500/40 text-rose-200 space-y-1 font-mono text-[11px] max-h-32 overflow-y-auto">
            {report.errors.map((err, i) => (
              <div key={i}>• {err}</div>
            ))}
          </div>
        )}

        {actionFooter && <div className="pt-2 border-t border-slate-800/80">{actionFooter}</div>}
      </div>

      {/* 2. 三视图切换胶囊 */}
      <div className="flex items-center gap-1.5 p-1 rounded-xl bg-slate-950 border border-slate-800 overflow-x-auto no-scrollbar">
        <button
          type="button"
          onClick={() => setActiveTab('cards')}
          className={`flex-1 py-2 px-3 rounded-lg font-semibold flex items-center justify-center gap-1.5 shrink-0 transition cursor-pointer ${
            activeTab === 'cards' ? activeBtnCls : 'text-slate-400 hover:text-slate-200'
          }`}
        >
          <Sparkles className="w-3.5 h-3.5" />
          <span>卡片可视化画廊 ({report.cardCount})</span>
        </button>
        <button
          type="button"
          onClick={() => setActiveTab('files')}
          className={`flex-1 py-2 px-3 rounded-lg font-semibold flex items-center justify-center gap-1.5 shrink-0 transition cursor-pointer ${
            activeTab === 'files' ? activeBtnCls : 'text-slate-400 hover:text-slate-200'
          }`}
        >
          <FolderOpen className="w-3.5 h-3.5" />
          <span>包内全部文件树与源码 ({report.totalFiles})</span>
        </button>
        <button
          type="button"
          onClick={() => setActiveTab('decks')}
          className={`flex-1 py-2 px-3 rounded-lg font-semibold flex items-center justify-center gap-1.5 shrink-0 transition cursor-pointer ${
            activeTab === 'decks' ? activeBtnCls : 'text-slate-400 hover:text-slate-200'
          }`}
        >
          <Layers className="w-3.5 h-3.5" />
          <span>
            主题卡组 & 字段声明 ({report.deckCount + (report.stringsConf?.setnames?.length || 0)})
          </span>
        </button>
      </div>

      {/* 视图 A：包内卡片可视化画廊 */}
      {activeTab === 'cards' && (
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-4 items-start">
          <div className="lg:col-span-7 glass-subcard rounded-2xl p-3.5 space-y-3">
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
              <div className="flex items-center gap-1">
                {(
                  [
                    ['all', `全部 (${report.cards.length})`],
                    ['monster', '怪兽'],
                    ['spell', '魔法'],
                    ['trap', '陷阱']
                  ] as const
                ).map(([k, label]) => (
                  <button
                    key={k}
                    type="button"
                    onClick={() => {
                      setCardTypeFilter(k);
                      setSelectedCardIdx(0);
                    }}
                    className={`px-2.5 py-1 rounded-full text-[11px] font-medium cursor-pointer ${
                      cardTypeFilter === k ? activeBtnCls : 'bg-slate-900 text-slate-400 hover:text-slate-200'
                    }`}
                  >
                    {label}
                  </button>
                ))}
              </div>

              <div className="relative sm:w-52">
                <Search className="w-3.5 h-3.5 text-slate-500 absolute left-3 top-1/2 -translate-y-1/2" />
                <input
                  value={cardSearch}
                  onChange={(e) => {
                    setCardSearch(e.target.value);
                    setSelectedCardIdx(0);
                  }}
                  placeholder="搜包内卡名、卡密或效果..."
                  className="w-full pl-8 pr-3 py-1.5 text-xs input-modern"
                />
              </div>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 max-h-[520px] overflow-y-auto pr-0.5">
              {filteredCards.map((item, idx) => {
                const c = mapRawCardToEditable(item.card);
                const ft = resolveCardFrameTheme(c.type);
                const selected = activeEditableCard?.id === c.id;
                const isLink = (c.type & CARD_BITS.LINK) !== 0;

                return (
                  <div
                    key={c.id}
                    onClick={() => setSelectedCardIdx(idx)}
                    className={`p-2.5 rounded-xl border flex items-center gap-2.5 cursor-pointer transition ${
                      selected
                        ? 'bg-slate-900 border-emerald-400/80 ring-1 ring-emerald-400/30'
                        : 'bg-slate-950/70 border-slate-800/90 hover:border-slate-700'
                    }`}
                  >
                    {item.hasImage ? (
                      <img
                        src={resolver.buildCardImageUrl(c.id)}
                        alt={c.name}
                        loading="lazy"
                        className="w-11 h-11 rounded-lg object-cover border border-slate-700 shrink-0 bg-slate-900"
                      />
                    ) : (
                      <div className="w-11 h-11 rounded-lg border border-dashed border-slate-700 flex items-center justify-center text-[10px] text-slate-500 shrink-0">
                        无图
                      </div>
                    )}

                    <div className="min-w-0 flex-1">
                      <div className="flex items-center justify-between gap-1">
                        <span className="font-bold text-slate-100 flex items-center gap-1.5 truncate">
                          <span className={`w-2 h-2 rounded-xs shrink-0 ${ft.dotColor}`} />
                          {c.name || `<未命名 #${c.id}>`}
                        </span>
                      </div>
                      <div className="text-[10px] text-slate-400 font-mono truncate mt-0.5">
                        #{c.id} · {ft.kindLabel}
                        {!ft.isSpell && !ft.isTrap
                          ? isLink
                            ? ` · L-${countLinkArrows(c.linkMarker) || c.level} · ATK ${formatStatDisplay(c.attack)}`
                            : ` · ★${c.level} · ${formatStatDisplay(c.attack)}/${formatStatDisplay(c.defense)}`
                          : ''}
                      </div>
                    </div>
                  </div>
                );
              })}

              {filteredCards.length === 0 && (
                <div className="sm:col-span-2 py-12 text-center text-slate-500">
                  {report.cardCount === 0
                    ? '该压缩包不含 .cdb 卡片定义（请切换至「包内全部文件树」查看脚本或图片资源）'
                    : '未找到匹配的卡片'}
                </div>
              )}
            </div>
          </div>

          {/* 右侧：选中卡片的实体卡面渲染 + 对应 Lua 脚本查看 */}
          <div className="lg:col-span-5 glass-subcard rounded-2xl p-4 space-y-3">
            {activeEditableCard ? (
              <>
                <div className="flex items-center justify-between">
                  <span className="font-bold text-slate-200">实体卡面实时还原</span>
                  {activeCardSummary?.hasScript && (
                    <button
                      type="button"
                      onClick={loadActiveCardLua}
                      className="px-2.5 py-1 rounded-lg bg-emerald-500/15 hover:bg-emerald-500/25 border border-emerald-500/30 text-emerald-300 text-[11px] font-semibold flex items-center gap-1 cursor-pointer"
                    >
                      <Code2 className="w-3.5 h-3.5" />
                      <span>{loadingCardScript ? '读取中...' : `查看 c${activeEditableCard.id}.lua`}</span>
                    </button>
                  )}
                </div>

                <YgoCardVisualPreview
                  card={activeEditableCard}
                  imageUrl={activeCardSummary?.hasImage ? resolver.buildCardImageUrl(activeEditableCard.id) : null}
                  canUploadImage={false}
                />

                {cardScriptText !== null && (
                  <div className="pt-2 space-y-1.5 animate-fade-in">
                    <div className="flex items-center justify-between text-[11px] text-slate-400">
                      <span className="font-mono">script/c{activeEditableCard.id}.lua</span>
                      <button
                        type="button"
                        onClick={() => setCardScriptText(null)}
                        className="text-slate-400 hover:text-white cursor-pointer"
                      >
                        收起脚本
                      </button>
                    </div>
                    <CodeMirrorEditor
                      value={cardScriptText}
                      onChange={() => {}}
                      language="lua"
                      accent={accent}
                      readOnly={true}
                      minHeight="180px"
                      maxHeight="300px"
                      compact
                    />
                  </div>
                )}
              </>
            ) : (
              <div className="py-16 text-center text-slate-500">点击左侧任意卡片查看完整卡面渲染与脚本</div>
            )}
          </div>
        </div>
      )}

      {/* 视图 B：完整文件树浏览器 & 在线内容查看器 */}
      {activeTab === 'files' && (
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-4 items-start">
          <div className="lg:col-span-5 glass-subcard rounded-2xl p-3.5 space-y-2.5">
            <div className="flex items-center gap-2">
              <div className="relative flex-1">
                <Search className="w-3.5 h-3.5 text-slate-500 absolute left-3 top-1/2 -translate-y-1/2" />
                <input
                  value={fileSearch}
                  onChange={(e) => setFileSearch(e.target.value)}
                  placeholder="按文件名或目录筛选 (如 script/ 或 .lua)..."
                  className="w-full pl-8 pr-3 py-1.5 text-xs input-modern"
                />
              </div>
              {fileCatFilter !== 'all' && (
                <button
                  type="button"
                  onClick={() => setFileCatFilter('all')}
                  className="px-2.5 py-1.5 rounded-lg bg-slate-800 text-[11px] text-slate-300 cursor-pointer shrink-0"
                >
                  显示全部
                </button>
              )}
            </div>

            <div className="space-y-1 max-h-[480px] overflow-y-auto pr-0.5 font-mono text-[11px]">
              {filteredFiles.map((f) => {
                const meta = CATEGORY_META[f.category] || CATEGORY_META.other;
                const active = selectedFile?.path === f.path;
                return (
                  <div
                    key={f.path}
                    onClick={() => setSelectedFile(f)}
                    className={`px-2.5 py-2 rounded-xl border flex items-center justify-between gap-2 cursor-pointer transition ${
                      active
                        ? 'bg-slate-900 border-emerald-400/70 text-white'
                        : 'bg-slate-950/60 border-slate-800/80 text-slate-300 hover:border-slate-700'
                    }`}
                  >
                    <div className="flex items-center gap-2 min-w-0">
                      <span className="shrink-0">{meta.icon}</span>
                      <span className="truncate" title={f.path}>
                        {f.folder && <span className="text-slate-500">{f.folder}/</span>}
                        <span>{f.name}</span>
                      </span>
                    </div>
                    <span className="text-[10px] text-slate-500 shrink-0">{formatBytes(f.uncompressedBytes)}</span>
                  </div>
                );
              })}
              {filteredFiles.length === 0 && (
                <div className="py-10 text-center text-slate-500 font-sans">没有符合筛选条件的包内文件</div>
              )}
            </div>
          </div>

          <div className="lg:col-span-7 glass-subcard rounded-2xl p-4 space-y-3">
            {selectedFile ? (
              <>
                <div className="flex items-center justify-between gap-2 border-b border-slate-800/80 pb-2.5">
                  <div className="font-mono font-semibold text-slate-200 truncate">{selectedFile.path}</div>
                  <span className="text-[11px] font-mono text-slate-400 shrink-0">
                    解压大小 {formatBytes(selectedFile.uncompressedBytes)}
                  </span>
                </div>

                {selectedFile.category === 'image' ? (
                  <div className="flex flex-col items-center justify-center py-4 space-y-2">
                    <img
                      src={resolver.buildFileImageUrl(selectedFile.path)}
                      alt={selectedFile.name}
                      className="max-h-96 rounded-xl border border-slate-700 shadow-xl object-contain"
                    />
                    <span className="text-[11px] text-slate-400 font-mono">{selectedFile.name}</span>
                  </div>
                ) : selectedFile.category === 'cdb' ? (
                  <div className="py-10 text-center space-y-3">
                    <Database className="w-9 h-9 text-amber-400 mx-auto" />
                    <div className="text-sm font-bold text-slate-200">
                      SQLite 卡片数据库 ({selectedFile.name}) · 已解析出 {report.cardCount} 张卡片
                    </div>
                    <button
                      type="button"
                      onClick={() => setActiveTab('cards')}
                      className={`px-4 py-2 rounded-xl font-semibold cursor-pointer ${activeBtnCls}`}
                    >
                      切换至「卡片可视化画廊」逐张浏览卡面 →
                    </button>
                  </div>
                ) : selectedFile.category === 'audio' ? (
                  <div className="py-12 text-center text-slate-400">音频二进制资源文件（{formatBytes(selectedFile.uncompressedBytes)}）</div>
                ) : loadingFilePreview ? (
                  <div className="py-12 text-center text-slate-400">正在读取文件内容...</div>
                ) : (
                  <CodeMirrorEditor
                    value={filePreviewText}
                    onChange={() => {}}
                    language={previewEditorLang}
                    accent={accent}
                    title={selectedFile.path}
                    readOnly={true}
                    minHeight="340px"
                    maxHeight="480px"
                  />
                )}
              </>
            ) : (
              <div className="py-16 text-center text-slate-500">点击左侧任意文件即可在线查看源码、配置或卡图</div>
            )}
          </div>
        </div>
      )}

      {/* 视图 C：配套主题卡组 (.ydk) 与 strings.conf 字段表 */}
      {activeTab === 'decks' && (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
          <div className="glass-subcard rounded-2xl p-4 space-y-2.5">
            <div className="font-bold text-slate-200">包内附带主题卡组 ({report.decks.length} 套)</div>
            {report.decks.length > 0 ? (
              <div className="space-y-2">
                {report.decks.map((d) => (
                  <div
                    key={d.path}
                    className="p-3 rounded-xl bg-slate-950/75 border border-slate-800 flex items-center justify-between gap-2"
                  >
                    <div>
                      <div className="font-semibold text-slate-100">{d.name}</div>
                      <div className="text-[11px] font-mono text-slate-400">
                        主卡组 {d.mainCount} · 额外 {d.extraCount} · 副卡组 {d.sideCount}
                      </div>
                    </div>
                    <button
                      type="button"
                      onClick={() => {
                        const fileEntry = report.files.find((f) => f.path === d.path);
                        if (fileEntry) {
                          setSelectedFile(fileEntry);
                          setActiveTab('files');
                        }
                      }}
                      className="px-3 py-1.5 rounded-lg bg-purple-500/15 border border-purple-500/35 text-purple-200 font-medium cursor-pointer"
                    >
                      查看 .ydk 卡密
                    </button>
                  </div>
                ))}
              </div>
            ) : (
              <div className="py-8 text-center text-slate-500">该扩展包未附带预设 .ydk 卡组文件</div>
            )}
          </div>

          <div className="glass-subcard rounded-2xl p-4 space-y-2.5">
            <div className="font-bold text-slate-200">
              自定义字段与指示物声明 ({(report.stringsConf?.setnames?.length || 0) + (report.stringsConf?.counters?.length || 0)})
            </div>
            {(report.stringsConf?.setnames?.length || 0) > 0 || (report.stringsConf?.counters?.length || 0) > 0 ? (
              <div className="flex flex-wrap gap-2">
                {(report.stringsConf?.setnames || []).map((sn, i) => (
                  <span
                    key={`sn-${i}`}
                    className="px-2.5 py-1 rounded-lg bg-indigo-500/15 border border-indigo-500/30 text-indigo-200"
                  >
                    字段 <code className="font-mono font-bold">{sn.codeHex}</code> · {sn.name}
                  </span>
                ))}
                {(report.stringsConf?.counters || []).map((ct, i) => (
                  <span
                    key={`ct-${i}`}
                    className="px-2.5 py-1 rounded-lg bg-amber-500/15 border border-amber-500/30 text-amber-200"
                  >
                    指示物 <code className="font-mono font-bold">{ct.codeHex}</code> · {ct.name}
                  </span>
                ))}
              </div>
            ) : (
              <div className="py-8 text-center text-slate-500">未声明自定义 strings.conf 字段或指示物</div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

export function YpkExplorerModal({
  open,
  title,
  report,
  resolver,
  accent = 'emerald',
  onClose
}: {
  open: boolean;
  title: string;
  report: YpkInspectionReport | null;
  resolver: YpkEntryResolver | null;
  accent?: 'emerald' | 'indigo';
  onClose: () => void;
}) {
  if (!open || !report || !resolver) return null;

  return (
    <div
      className="fixed inset-0 z-50 bg-black/80 backdrop-blur-sm flex items-center justify-center p-2 sm:p-5"
      onClick={onClose}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        className="w-full max-w-6xl max-h-[92vh] glass-card rounded-3xl border border-slate-700/80 shadow-2xl flex flex-col overflow-hidden animate-slide-up"
      >
        <div className="px-4 sm:px-6 py-3.5 border-b border-slate-800 flex items-center justify-between gap-3 bg-slate-950/70">
          <div className="font-bold text-sm sm:text-base text-slate-100 truncate">{title}</div>
          <button
            type="button"
            onClick={onClose}
            className="p-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-300 cursor-pointer"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        <div className="p-4 sm:p-6 overflow-y-auto flex-1">
          <YpkPackageExplorer report={report} resolver={resolver} accent={accent} />
        </div>
      </div>
    </div>
  );
}
