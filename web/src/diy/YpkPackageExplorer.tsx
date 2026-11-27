import React, { useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Button,
  Chip,
  Modal,
  SearchField,
  Spinner,
  Surface,
  Tabs
} from '@heroui/react';
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
  Sparkles
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
    badgeCls: 'bg-sky-500/12 border-sky-500/30 text-sky-400',
    icon: <ImageIcon className="w-3.5 h-3.5 text-sky-400" />
  },
  lua: {
    label: 'Lua 效果脚本 (script)',
    barCls: 'bg-emerald-500',
    badgeCls: 'bg-emerald-500/12 border-emerald-500/30 text-emerald-400',
    icon: <FileCode2 className="w-3.5 h-3.5 text-emerald-400" />
  },
  cdb: {
    label: 'SQLite 卡表 (.cdb)',
    barCls: 'bg-amber-500',
    badgeCls: 'bg-amber-500/12 border-amber-500/30 text-amber-400',
    icon: <Database className="w-3.5 h-3.5 text-amber-400" />
  },
  ydk: {
    label: '预设卡组 (.ydk)',
    barCls: 'bg-purple-500',
    badgeCls: 'bg-purple-500/12 border-purple-500/30 text-purple-400',
    icon: <Layers className="w-3.5 h-3.5 text-purple-400" />
  },
  conf: {
    label: '字段与禁限配置 (.conf)',
    barCls: 'bg-indigo-500',
    badgeCls: 'bg-indigo-500/12 border-indigo-500/30 text-indigo-400',
    icon: <FileText className="w-3.5 h-3.5 text-indigo-400" />
  },
  audio: {
    label: '音效资源',
    barCls: 'bg-pink-500',
    badgeCls: 'bg-pink-500/12 border-pink-500/30 text-pink-400',
    icon: <Music className="w-3.5 h-3.5 text-pink-400" />
  },
  doc: {
    label: '说明文档',
    barCls: 'bg-zinc-400',
    badgeCls: 'bg-surface border-border text-muted',
    icon: <FileText className="w-3.5 h-3.5 text-muted" />
  },
  other: {
    label: '其他资源',
    barCls: 'bg-zinc-500',
    badgeCls: 'bg-surface border-border text-muted',
    icon: <FolderOpen className="w-3.5 h-3.5 text-muted" />
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

  const [cardSearch, setCardSearch] = useState('');
  const [cardTypeFilter, setCardTypeFilter] = useState<'all' | 'monster' | 'spell' | 'trap'>('all');
  const [selectedCardIdx, setSelectedCardIdx] = useState(0);
  const [cardScriptText, setCardScriptText] = useState<string | null>(null);
  const [loadingCardScript, setLoadingCardScript] = useState(false);

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
  const previewEditorLang: CodeMirrorLang =
    selectedFile?.category === 'ydk' ? 'ydk' : selectedFile?.category === 'conf' ? 'conf' : 'lua';

  return (
    <div className="space-y-4 text-xs animate-fade-in">
      {/* 1. 顶部包体摘要与体积占比条 */}
      <Surface variant="secondary" className="rounded-xl p-4 space-y-3 border border-border">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div className="flex items-center gap-2 min-w-0">
            <span className="font-mono font-bold text-sm text-foreground truncate">{report.archiveName}</span>
            {report.isSafe ? (
              <Chip size="sm" variant="soft" color="success" className="h-5 px-2 text-[10px]">
                <CheckCircle2 className="w-3 h-3" />
                <Chip.Label>安全与结构预检通过</Chip.Label>
              </Chip>
            ) : (
              <Chip size="sm" variant="soft" color="danger" className="h-5 px-2 text-[10px]">
                <AlertTriangle className="w-3 h-3" />
                <Chip.Label>预检发现异常 ({report.errors.length})</Chip.Label>
              </Chip>
            )}
          </div>

          <div className="flex flex-wrap items-center gap-2.5 text-[11px] text-muted font-mono">
            <span>共 {report.totalFiles} 个文件</span>
            <span>·</span>
            <span>压缩包 {formatBytes(report.compressedBytes)}</span>
            <span>·</span>
            <span>解压后 {formatBytes(report.uncompressedBytes)}</span>
          </div>
        </div>

        {report.categories.length > 0 && (
          <div className="space-y-2">
            <div className="h-2 w-full rounded-full bg-background overflow-hidden flex gap-0.5 p-0.5 border border-border">
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
          <Alert status="danger">
            <Alert.Indicator />
            <Alert.Content>
              <Alert.Description className="space-y-1 font-mono text-[11px] max-h-32 overflow-y-auto">
                {report.errors.map((err, i) => (
                  <div key={i}>• {err}</div>
                ))}
              </Alert.Description>
            </Alert.Content>
          </Alert>
        )}

        {actionFooter && <div className="pt-2.5 border-t border-separator">{actionFooter}</div>}
      </Surface>

      {/* 2. 三大可视化视图 Tabs */}
      <Tabs
        selectedKey={activeTab}
        onSelectionChange={(k) => setActiveTab(String(k) as 'cards' | 'files' | 'decks')}
        className="w-full"
      >
        <Tabs.ListContainer>
          <Tabs.List aria-label="扩展包内部探索视图" className="w-full">
            <Tabs.Tab id="cards" className="flex-1 text-xs gap-1.5">
              <Sparkles className="w-3.5 h-3.5" />
              <span>卡片可视化画廊 ({report.cardCount})</span>
              <Tabs.Indicator />
            </Tabs.Tab>
            <Tabs.Tab id="files" className="flex-1 text-xs gap-1.5">
              <FolderOpen className="w-3.5 h-3.5" />
              <span>包内全部文件树与源码 ({report.totalFiles})</span>
              <Tabs.Indicator />
            </Tabs.Tab>
            <Tabs.Tab id="decks" className="flex-1 text-xs gap-1.5">
              <Layers className="w-3.5 h-3.5" />
              <span>主题卡组 & 字段声明 ({report.deckCount + (report.stringsConf?.setnames?.length || 0)})</span>
              <Tabs.Indicator />
            </Tabs.Tab>
          </Tabs.List>
        </Tabs.ListContainer>
      </Tabs>

      {/* 视图 A：包内卡片可视化画廊 */}
      {activeTab === 'cards' && (
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-4 items-start">
          <Surface variant="secondary" className="lg:col-span-7 rounded-xl p-3.5 space-y-3 border border-border">
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
              <Tabs
                selectedKey={cardTypeFilter}
                onSelectionChange={(k) => {
                  setCardTypeFilter(String(k) as 'all' | 'monster' | 'spell' | 'trap');
                  setSelectedCardIdx(0);
                }}
              >
                <Tabs.ListContainer>
                  <Tabs.List aria-label="卡片种类筛选">
                    <Tabs.Tab id="all" className="text-[11px] px-2.5">
                      全部 ({report.cards.length})
                      <Tabs.Indicator />
                    </Tabs.Tab>
                    <Tabs.Tab id="monster" className="text-[11px] px-2.5">
                      怪兽
                      <Tabs.Indicator />
                    </Tabs.Tab>
                    <Tabs.Tab id="spell" className="text-[11px] px-2.5">
                      魔法
                      <Tabs.Indicator />
                    </Tabs.Tab>
                    <Tabs.Tab id="trap" className="text-[11px] px-2.5">
                      陷阱
                      <Tabs.Indicator />
                    </Tabs.Tab>
                  </Tabs.List>
                </Tabs.ListContainer>
              </Tabs>

              <SearchField
                aria-label="搜包内卡片"
                variant="secondary"
                value={cardSearch}
                onChange={(val) => {
                  setCardSearch(val);
                  setSelectedCardIdx(0);
                }}
                className="sm:w-52"
              >
                <SearchField.Group>
                  <SearchField.SearchIcon />
                  <SearchField.Input placeholder="搜卡名、卡密或效果..." className="text-xs" />
                  <SearchField.ClearButton />
                </SearchField.Group>
              </SearchField>
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
                    className={`p-2.5 rounded-lg border flex items-center gap-2.5 cursor-pointer transition ${
                      selected
                        ? 'bg-surface border-accent ring-1 ring-accent/30'
                        : 'bg-background/60 border-border hover:border-foreground/25'
                    }`}
                  >
                    {item.hasImage ? (
                      <img
                        src={resolver.buildCardImageUrl(c.id)}
                        alt={c.name}
                        loading="lazy"
                        className="w-11 h-11 rounded-md object-cover border border-border shrink-0 bg-surface"
                      />
                    ) : (
                      <div className="w-11 h-11 rounded-md border border-dashed border-border flex items-center justify-center text-[10px] text-muted shrink-0">
                        无图
                      </div>
                    )}

                    <div className="min-w-0 flex-1">
                      <div className="flex items-center justify-between gap-1">
                        <span className="font-bold text-foreground flex items-center gap-1.5 truncate">
                          <span className={`w-2 h-2 rounded-xs shrink-0 ${ft.dotColor}`} />
                          {c.name || `<未命名 #${c.id}>`}
                        </span>
                      </div>
                      <div className="text-[10px] text-muted font-mono truncate mt-0.5">
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
                <div className="sm:col-span-2 py-12 text-center text-muted">
                  {report.cardCount === 0
                    ? '该压缩包不含 .cdb 卡片定义（请切换至「包内全部文件树」查看脚本或图片资源）'
                    : '未找到匹配的卡片'}
                </div>
              )}
            </div>
          </Surface>

          {/* 右侧：实体卡面渲染 + 对应 Lua 源码阅读 */}
          <Surface variant="secondary" className="lg:col-span-5 rounded-xl p-4 space-y-3 border border-border">
            {activeEditableCard ? (
              <>
                <div className="flex items-center justify-between">
                  <span className="font-semibold text-foreground">实体卡面实时还原</span>
                  {activeCardSummary?.hasScript && (
                    <Button
                      size="sm"
                      variant="secondary"
                      isPending={loadingCardScript}
                      onPress={loadActiveCardLua}
                      className="h-7 px-2.5 text-[11px]"
                    >
                      <Code2 className="w-3.5 h-3.5" />
                      <span>{loadingCardScript ? '读取中...' : `查看 c${activeEditableCard.id}.lua`}</span>
                    </Button>
                  )}
                </div>

                <YgoCardVisualPreview
                  card={activeEditableCard}
                  imageUrl={activeCardSummary?.hasImage ? resolver.buildCardImageUrl(activeEditableCard.id) : null}
                  canUploadImage={false}
                />

                {cardScriptText !== null && (
                  <div className="pt-2 space-y-1.5 animate-fade-in">
                    <div className="flex items-center justify-between text-[11px] text-muted">
                      <span className="font-mono">script/c{activeEditableCard.id}.lua</span>
                      <Button size="sm" variant="ghost" className="h-6 px-2 text-[11px]" onPress={() => setCardScriptText(null)}>
                        收起脚本
                      </Button>
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
              <div className="py-16 text-center text-muted">点击左侧任意卡片查看完整卡面渲染与脚本</div>
            )}
          </Surface>
        </div>
      )}

      {/* 视图 B：完整文件树浏览器 & 在线内容查看器 */}
      {activeTab === 'files' && (
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-4 items-start">
          <Surface variant="secondary" className="lg:col-span-5 rounded-xl p-3.5 space-y-2.5 border border-border">
            <div className="flex items-center gap-2">
              <SearchField
                aria-label="筛选包内文件路径"
                variant="secondary"
                value={fileSearch}
                onChange={setFileSearch}
                className="flex-1"
              >
                <SearchField.Group>
                  <SearchField.SearchIcon />
                  <SearchField.Input placeholder="按文件名或目录筛选 (如 script/ 或 .lua)..." className="text-xs" />
                  <SearchField.ClearButton />
                </SearchField.Group>
              </SearchField>

              {fileCatFilter !== 'all' && (
                <Button size="sm" variant="secondary" className="shrink-0 text-xs" onPress={() => setFileCatFilter('all')}>
                  全部
                </Button>
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
                    className={`px-2.5 py-2 rounded-lg border flex items-center justify-between gap-2 cursor-pointer transition ${
                      active
                        ? 'bg-surface border-accent text-foreground font-semibold'
                        : 'bg-background/60 border-border text-foreground/85 hover:border-foreground/25'
                    }`}
                  >
                    <div className="flex items-center gap-2 min-w-0">
                      <span className="shrink-0">{meta.icon}</span>
                      <span className="truncate" title={f.path}>
                        {f.folder && <span className="text-muted">{f.folder}/</span>}
                        <span>{f.name}</span>
                      </span>
                    </div>
                    <span className="text-[10px] text-muted shrink-0">{formatBytes(f.uncompressedBytes)}</span>
                  </div>
                );
              })}
              {filteredFiles.length === 0 && (
                <div className="py-10 text-center text-muted font-sans">没有符合筛选条件的包内文件</div>
              )}
            </div>
          </Surface>

          <Surface variant="secondary" className="lg:col-span-7 rounded-xl p-4 space-y-3 border border-border">
            {selectedFile ? (
              <>
                <div className="flex items-center justify-between gap-2 border-b border-separator pb-2.5">
                  <div className="font-mono font-semibold text-foreground truncate">{selectedFile.path}</div>
                  <span className="text-[11px] font-mono text-muted shrink-0">
                    解压大小 {formatBytes(selectedFile.uncompressedBytes)}
                  </span>
                </div>

                {selectedFile.category === 'image' ? (
                  <div className="flex flex-col items-center justify-center py-4 space-y-2">
                    <img
                      src={resolver.buildFileImageUrl(selectedFile.path)}
                      alt={selectedFile.name}
                      className="max-h-96 rounded-lg border border-border shadow-xl object-contain"
                    />
                    <span className="text-[11px] text-muted font-mono">{selectedFile.name}</span>
                  </div>
                ) : selectedFile.category === 'cdb' ? (
                  <div className="py-10 text-center space-y-3">
                    <Database className="w-9 h-9 text-amber-400 mx-auto" />
                    <div className="text-sm font-bold text-foreground">
                      SQLite 卡片数据库 ({selectedFile.name}) · 已解析出 {report.cardCount} 张卡片
                    </div>
                    <Button variant="primary" onPress={() => setActiveTab('cards')}>
                      切换至「卡片可视化画廊」逐张浏览卡面 →
                    </Button>
                  </div>
                ) : selectedFile.category === 'audio' ? (
                  <div className="py-12 text-center text-muted">音频二进制资源文件（{formatBytes(selectedFile.uncompressedBytes)}）</div>
                ) : loadingFilePreview ? (
                  <div className="py-12 flex items-center justify-center gap-2 text-muted">
                    <Spinner size="sm" />
                    <span>正在读取文件内容...</span>
                  </div>
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
              <div className="py-16 text-center text-muted">点击左侧任意文件即可在线查看源码、配置或卡图</div>
            )}
          </Surface>
        </div>
      )}

      {/* 视图 C：配套主题卡组 (.ydk) 与 strings.conf 字段表 */}
      {activeTab === 'decks' && (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
          <Surface variant="secondary" className="rounded-xl p-4 space-y-2.5 border border-border">
            <div className="font-bold text-foreground">包内附带主题卡组 ({report.decks.length} 套)</div>
            {report.decks.length > 0 ? (
              <div className="space-y-2">
                {report.decks.map((d) => (
                  <div
                    key={d.path}
                    className="p-3 rounded-lg bg-background/60 border border-border flex items-center justify-between gap-2"
                  >
                    <div>
                      <div className="font-semibold text-foreground">{d.name}</div>
                      <div className="text-[11px] font-mono text-muted">
                        主卡组 {d.mainCount} · 额外 {d.extraCount} · 副卡组 {d.sideCount}
                      </div>
                    </div>
                    <Button
                      size="sm"
                      variant="secondary"
                      onPress={() => {
                        const fileEntry = report.files.find((f) => f.path === d.path);
                        if (fileEntry) {
                          setSelectedFile(fileEntry);
                          setActiveTab('files');
                        }
                      }}
                    >
                      查看 .ydk 卡密
                    </Button>
                  </div>
                ))}
              </div>
            ) : (
              <div className="py-8 text-center text-muted">该扩展包未附带预设 .ydk 卡组文件</div>
            )}
          </Surface>

          <Surface variant="secondary" className="rounded-xl p-4 space-y-2.5 border border-border">
            <div className="font-bold text-foreground">
              自定义字段与指示物声明 ({(report.stringsConf?.setnames?.length || 0) + (report.stringsConf?.counters?.length || 0)})
            </div>
            {(report.stringsConf?.setnames?.length || 0) > 0 || (report.stringsConf?.counters?.length || 0) > 0 ? (
              <div className="flex flex-wrap gap-2">
                {(report.stringsConf?.setnames || []).map((sn, i) => (
                  <Chip key={`sn-${i}`} size="sm" variant="soft" color="accent" className="px-2.5">
                    字段 <code className="font-mono font-bold ml-1">{sn.codeHex}</code> · {sn.name}
                  </Chip>
                ))}
                {(report.stringsConf?.counters || []).map((ct, i) => (
                  <Chip key={`ct-${i}`} size="sm" variant="soft" color="warning" className="px-2.5">
                    指示物 <code className="font-mono font-bold ml-1">{ct.codeHex}</code> · {ct.name}
                  </Chip>
                ))}
              </div>
            ) : (
              <div className="py-8 text-center text-muted">未声明自定义 strings.conf 字段或指示物</div>
            )}
          </Surface>
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
    <Modal.Backdrop
      isOpen={open}
      onOpenChange={(nextOpen) => {
        if (!nextOpen) onClose();
      }}
      className={accent === 'emerald' ? 'portal-theme' : 'admin-theme'}
    >
      <Modal.Container size="cover" scroll="inside">
        <Modal.Dialog className="max-w-6xl mx-auto border border-border">
          <Modal.CloseTrigger aria-label="关闭扩展包探索器" />
          <Modal.Header className="border-b border-separator pb-3">
            <Modal.Heading className="text-sm sm:text-base font-bold truncate pr-8">{title}</Modal.Heading>
          </Modal.Header>
          <Modal.Body className="pt-4">
            <YpkPackageExplorer report={report} resolver={resolver} accent={accent} />
          </Modal.Body>
        </Modal.Dialog>
      </Modal.Container>
    </Modal.Backdrop>
  );
}
