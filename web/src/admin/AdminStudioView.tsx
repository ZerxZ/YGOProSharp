import React, { useEffect, useState, useCallback, useRef, useMemo } from 'react';
import {
  AlertTriangle,
  CheckCircle2,
  Code2,
  CopyPlus,
  Download,
  Eye,
  FileCheck2,
  FolderPlus,
  FolderUp,
  Layers,
  Palette,
  Play,
  Plus,
  RefreshCw,
  Save,
  Search,
  Sparkles,
  Trash2,
  Upload,
  Wand2,
  XCircle
} from 'lucide-react';
import { apiFetch, apiUploadWithProgress, AuthUser, hasPerm, UploadProgressInfo } from '../api';
import { DeckSixDimRadarSection } from '../portal/DeckSixDimRadarSection';
import { CodeMirrorEditor } from '../components/CodeMirrorEditor';
import {
  CARD_BITS,
  countLinkArrows,
  EditableDiyCard,
  formatStatDisplay,
  generateSmartLuaTemplate,
  resolveCardFrameTheme
} from '../diy/ygoCardTaxonomy';
import { YgoCardVisualPreview } from '../diy/YgoCardVisualPreview';
import { CardBaseStatsInspector, CardTextAndStringsInspector } from '../diy/YgoCardEditorInspector';
import {
  createInstalledPackageResolver,
  createPreviewTokenResolver,
  createProposalYpkResolver,
  formatBytes,
  YpkEntryResolver,
  YpkExplorerModal,
  YpkInspectionReport,
  YpkPackageExplorer
} from '../diy/YpkPackageExplorer';

type StudioWorkspace = 'workbench' | 'proposals' | 'pool_catalog';
type InspectorTab = 'stats' | 'text' | 'script';
type ExplorerFilter = 'all' | 'monster' | 'spell' | 'trap' | 'no_script' | 'no_image';

function createInitialDraft(id = 99000001): EditableDiyCard {
  return {
    id,
    ot: 4,
    alias: 0,
    setcodeHex: '0xa100',
    type: CARD_BITS.MONSTER | CARD_BITS.SYNCHRO | CARD_BITS.EFFECT,
    level: 8,
    lScale: 0,
    rScale: 0,
    race: 0x2000, // 龙族
    attribute: 0x10, // 光
    attack: 3000,
    defense: 2500,
    linkMarker: 0,
    name: '星辉圣珖龙',
    desc: '调整＋调整以外的怪兽1只以上\n这个卡名的①②的效果1回合各能使用1次。\n①：这张卡同调召唤的场合才能发动。自己从卡组抽1张。\n②：对方把魔法·陷阱·怪兽的效果发动时才能发动。那个发动无效并破坏。',
    strings: ['抽1张卡', '发动无效并破坏']
  };
}

function parseProposalDiff(diffJson: string): any | null {
  try {
    return JSON.parse(diffJson);
  } catch {
    return null;
  }
}

export function StudioView({ user, notify }: { user: AuthUser; notify: (t: string, k?: 'ok' | 'err') => void }) {
  const [workspace, setWorkspace] = useState<StudioWorkspace>('workbench');
  const [inspectorTab, setInspectorTab] = useState<InspectorTab>('stats');

  const [packages, setPackages] = useState<any[]>([]);
  const [trash, setTrash] = useState<string[]>([]);
  const [conflicts, setConflicts] = useState<any>(null);
  const [selectedPkgId, setSelectedPkgId] = useState('');
  const [pkgCards, setPkgCards] = useState<any[]>([]);
  const [cardFilter, setCardFilter] = useState('');
  const [explorerFilter, setExplorerFilter] = useState<ExplorerFilter>('all');
  const [stringsConfText, setStringsConfText] = useState('');
  const [showStringsPanel, setShowStringsPanel] = useState(false);

  const [editingCard, setEditingCard] = useState<EditableDiyCard>(() => createInitialDraft(99000001));
  const [luaScript, setLuaScript] = useState<string>(() => generateSmartLuaTemplate(createInitialDraft(99000001)));
  const [hasCardImage, setHasCardImage] = useState(false);
  const [imgCacheBust, setImgCacheBust] = useState(0);
  const [sandboxResult, setSandboxResult] = useState<any>(null);
  const [sandboxRunning, setSandboxRunning] = useState(false);
  const [savingCard, setSavingCard] = useState(false);

  const [proposals, setProposals] = useState<any[]>([]);
  const [reviewComment, setReviewComment] = useState('');

  const [poolDecks, setPoolDecks] = useState<any[]>([]);
  const [chaosPreview, setChaosPreview] = useState<any>(null);
  const [cardQuery, setCardQuery] = useState('');
  const [cards, setCards] = useState<any[]>([]);

  const [showNewPkgForm, setShowNewPkgForm] = useState(false);
  const [newPkgId, setNewPkgId] = useState('');
  const [newPkgName, setNewPkgName] = useState('');
  const [showYpkImporter, setShowYpkImporter] = useState(false);
  const [ypkTargetId, setYpkTargetId] = useState('');
  const [ypkTargetName, setYpkTargetName] = useState('');
  const [ypkMergeMode, setYpkMergeMode] = useState(true);
  const [ypkAutoReload, setYpkAutoReload] = useState(true);
  const [ypkImporting, setYpkImporting] = useState(false);
  const [ypkUploadProg, setYpkUploadProg] = useState<UploadProgressInfo | null>(null);
  const [ypkPreviewToken, setYpkPreviewToken] = useState<string | null>(null);
  const [ypkPreviewReport, setYpkPreviewReport] = useState<YpkInspectionReport | null>(null);

  const [explorerModalTitle, setExplorerModalTitle] = useState('');
  const [explorerModalReport, setExplorerModalReport] = useState<YpkInspectionReport | null>(null);
  const [explorerModalResolver, setExplorerModalResolver] = useState<YpkEntryResolver | null>(null);

  const stageRef = useRef<HTMLDivElement | null>(null);

  const loadPackageCards = useCallback(
    async (pkgId: string) => {
      if (!pkgId || !hasPerm(user, 'diy:view')) return;
      try {
        const res = await apiFetch(`/api/v1/diy/packages/${encodeURIComponent(pkgId)}/cards`);
        setPkgCards(res.cards || []);
        setStringsConfText(res.stringsConf?.rawContent || '');
      } catch (err) {
        notify(err instanceof Error ? err.message : '读取包内卡片失败', 'err');
      }
    },
    [user, notify]
  );

  const loadAll = useCallback(async () => {
    try {
      if (hasPerm(user, 'diy:view')) {
        const [d, cf, pr] = await Promise.all([
          apiFetch('/api/v1/diy/packages'),
          apiFetch('/api/v1/diy/conflicts'),
          apiFetch('/api/v1/diy/proposals?limit=50')
        ]);
        setPackages(d.packages || []);
        setTrash(d.trash || []);
        setConflicts(cf);
        setProposals(pr.proposals || []);
        const activePkg = selectedPkgId || (d.packages?.length ? d.packages[0].manifest.id : '');
        if (activePkg) {
          if (activePkg !== selectedPkgId) setSelectedPkgId(activePkg);
          await loadPackageCards(activePkg);
        }
      }
      if (hasPerm(user, 'deck_pool:view')) {
        const p = await apiFetch('/api/v1/decks/pool');
        setPoolDecks(p.decks || []);
      }
    } catch (err) {
      notify(err instanceof Error ? err.message : '加载工作台资源失败', 'err');
    }
  }, [user, notify, selectedPkgId, loadPackageCards]);

  useEffect(() => {
    loadAll();
  }, [loadAll]);

  const scrollStageOnMobile = () => {
    if (typeof window !== 'undefined' && window.innerWidth < 1024) {
      setTimeout(() => stageRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' }), 60);
    }
  };

  const selectCardForEdit = async (pkgId: string, cardId: number) => {
    try {
      const res = await apiFetch(`/api/v1/diy/packages/${encodeURIComponent(pkgId)}/cards/${cardId}`);
      const c = res.detail.card;
      const loaded: EditableDiyCard = {
        id: Number(c.id),
        ot: Number(c.ot ?? 4),
        alias: Number(c.alias ?? 0),
        setcodeHex: '0x' + BigInt(c.setcode || 0).toString(16),
        type: Number(c.type ?? 0x21),
        level: Number(c.level ?? 4),
        lScale: Number(c.lScale ?? 0),
        rScale: Number(c.rScale ?? 0),
        race: Number(c.race ?? 1),
        attribute: Number(c.attribute ?? 0x20),
        attack: Number(c.attack ?? 0),
        defense: Number(c.defense ?? 0),
        linkMarker: Number(c.linkMarker ?? 0),
        name: String(c.name || ''),
        desc: String(c.desc || ''),
        strings: Array.isArray(c.strings) ? c.strings : []
      };
      setEditingCard(loaded);
      setLuaScript(res.detail.scriptContent || generateSmartLuaTemplate(loaded));
      setHasCardImage(Boolean(res.detail.hasImage));
      setSandboxResult(null);
      scrollStageOnMobile();
    } catch (err) {
      notify(err instanceof Error ? err.message : '加载卡片详情失败', 'err');
    }
  };

  const startNewCardDraft = () => {
    const nextId = pkgCards.length > 0 ? Math.max(...pkgCards.map((x: any) => Number(x.card.id))) + 1 : 99000001;
    const draft = createInitialDraft(nextId);
    draft.name = `新 DIY 卡 #${nextId}`;
    setEditingCard(draft);
    setLuaScript(generateSmartLuaTemplate(draft));
    setHasCardImage(false);
    setSandboxResult(null);
    setInspectorTab('stats');
    scrollStageOnMobile();
  };

  const cloneCurrentCard = () => {
    const nextId = pkgCards.length > 0 ? Math.max(...pkgCards.map((x: any) => Number(x.card.id)), Number(editingCard.id)) + 1 : Number(editingCard.id) + 1;
    const cloned: EditableDiyCard = {
      ...editingCard,
      id: nextId,
      name: `${editingCard.name} (副本)`
    };
    setEditingCard(cloned);
    setHasCardImage(false);
    setSandboxResult(null);
    notify(`已克隆为新卡密 #${nextId}，修改后点击保存即可入库`);
  };

  const saveCardDirect = useCallback(
    async (autoReload: boolean) => {
      if (!selectedPkgId) {
        notify('请先在左上角选择或新建一个受管 DIY 包', 'err');
        setShowNewPkgForm(true);
        return;
      }
      setSavingCard(true);
      try {
        const res = await apiFetch(
          `/api/v1/diy/packages/${encodeURIComponent(selectedPkgId)}/cards/${Number(editingCard.id)}`,
          {
            method: 'PUT',
            body: JSON.stringify({
              card: {
                ...editingCard,
                id: Number(editingCard.id)
              },
              luaScript,
              autoReload
            })
          }
        );
        notify(
          res.reload
            ? `已保存 ${editingCard.name} (#${editingCard.id}) 并热重载至 Catalog v${res.reload.catalogVersion}`
            : `已通过 Working Copy 原子保存 ${editingCard.name} (#${editingCard.id})`
        );
        await loadAll();
      } catch (err) {
        notify(err instanceof Error ? err.message : '保存失败', 'err');
      } finally {
        setSavingCard(false);
      }
    },
    [selectedPkgId, editingCard, luaScript, notify, loadAll]
  );

  // 支持 Ctrl+S / Cmd+S 快捷原子保存
  useEffect(() => {
    if (workspace !== 'workbench' || !hasPerm(user, 'diy:edit')) return;
    const onKeyDown = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') {
        e.preventDefault();
        saveCardDirect(true);
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [workspace, user, saveCardDirect]);

  const submitAsProposal = async () => {
    if (!selectedPkgId) {
      notify('请先选择目标 DIY 包', 'err');
      return;
    }
    try {
      await apiFetch('/api/v1/diy/proposals', {
        method: 'POST',
        body: JSON.stringify({
          packageId: selectedPkgId,
          title: `新增/修改卡片 ${editingCard.name} (#${editingCard.id})`,
          payload: {
            cards: [{ card: { ...editingCard, id: Number(editingCard.id) }, luaScript }]
          }
        })
      });
      notify('已提交至提案审批中心');
      setWorkspace('proposals');
      await loadAll();
    } catch (err) {
      notify(err instanceof Error ? err.message : '提交提案失败', 'err');
    }
  };

  const deleteCurrentCard = async () => {
    if (!selectedPkgId || !editingCard.id) return;
    try {
      await apiFetch(`/api/v1/diy/packages/${encodeURIComponent(selectedPkgId)}/cards/${Number(editingCard.id)}`, {
        method: 'DELETE'
      });
      notify(`已从包 ${selectedPkgId} 移除卡片 #${editingCard.id}`);
      await loadAll();
    } catch (err) {
      notify(err instanceof Error ? err.message : '删除失败', 'err');
    }
  };

  const runSandbox = async () => {
    setSandboxRunning(true);
    try {
      const res = await apiFetch('/api/v1/diy/sandbox-test', {
        method: 'POST',
        body: JSON.stringify({
          card: { ...editingCard, id: Number(editingCard.id) },
          luaScript,
          testExecuteFirstActivation: true
        })
      });
      setSandboxResult(res.result);
      if (res.result.success) {
        notify(`ocgcore 沙盒预检通过 (${res.result.elapsedMs} ms)`);
      } else {
        notify('沙盒检测到脚本错误或安全违规，请查看下方诊断台', 'err');
      }
    } catch (err) {
      notify(err instanceof Error ? err.message : '沙盒运行失败', 'err');
    } finally {
      setSandboxRunning(false);
    }
  };

  const uploadCardArtwork = async (files: FileList | null) => {
    if (!files?.length) return;
    if (!selectedPkgId) {
      notify('请先选择或创建一个 DIY 包后再上传卡图', 'err');
      return;
    }
    const fd = new FormData();
    fd.append('image', files[0]);
    try {
      await apiFetch(
        `/api/v1/diy/packages/${encodeURIComponent(selectedPkgId)}/cards/${Number(editingCard.id)}/image`,
        {
          method: 'POST',
          body: fd
        }
      );
      setHasCardImage(true);
      setImgCacheBust((n) => n + 1);
      notify(`卡图已保存至 pics/${editingCard.id}`);
      await loadPackageCards(selectedPkgId);
    } catch (err) {
      notify(err instanceof Error ? err.message : '卡图上传失败', 'err');
    }
  };

  const saveStringsConf = async () => {
    if (!selectedPkgId) return;
    try {
      await apiFetch(`/api/v1/diy/packages/${encodeURIComponent(selectedPkgId)}/strings`, {
        method: 'PUT',
        body: JSON.stringify({ content: stringsConfText })
      });
      notify('strings.conf 字段配置已原子保存');
    } catch (err) {
      notify(err instanceof Error ? err.message : '保存 strings.conf 失败', 'err');
    }
  };

  const reviewProposal = async (proposalId: string, approve: boolean) => {
    try {
      const res = await apiFetch(`/api/v1/diy/proposals/${proposalId}/review`, {
        method: 'POST',
        body: JSON.stringify({ approve, comment: reviewComment })
      });
      setReviewComment('');
      notify(res.message || (approve ? '提案已批准并热重载生效' : '提案已驳回'));
      await loadAll();
    } catch (err) {
      notify(err instanceof Error ? err.message : '审批失败', 'err');
    }
  };

  const createPackage = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      await apiFetch('/api/v1/diy/packages', {
        method: 'POST',
        body: JSON.stringify({ id: newPkgId.trim(), name: newPkgName.trim() || newPkgId.trim() })
      });
      setSelectedPkgId(newPkgId.trim());
      setNewPkgId('');
      setNewPkgName('');
      setShowNewPkgForm(false);
      notify('受管 DIY 包创建成功');
      loadAll();
    } catch (err) {
      notify(err instanceof Error ? err.message : '创建包失败', 'err');
    }
  };

  const uploadFiles = async (files: FileList | null) => {
    if (!files || files.length === 0 || !selectedPkgId) return;
    const fd = new FormData();
    Array.from(files).forEach((f) => fd.append('files', f));
    try {
      const res = await apiFetch(`/api/v1/diy/packages/${encodeURIComponent(selectedPkgId)}/upload`, {
        method: 'POST',
        body: fd
      });
      if (res.errors?.length) {
        notify(`部分文件被安全拦截: ${res.errors.join('; ')}`, 'err');
      } else {
        notify(`成功导入 ${res.savedFiles.length} 项资源至 ${selectedPkgId}`);
      }
      loadAll();
    } catch (err) {
      notify(err instanceof Error ? err.message : '上传失败', 'err');
    }
  };

  const previewYpkBeforeImport = async (files: FileList | null) => {
    if (!files || files.length === 0) return;
    const file = files[0];
    setYpkUploadProg({ loaded: 0, total: file.size, percent: 0 });
    try {
      const fd = new FormData();
      fd.append('file', file);
      const res = await apiUploadWithProgress<{
        previewToken: string;
        suggestedPackageId: string;
        suggestedName: string;
        report: YpkInspectionReport;
      }>('/api/v1/diy/inspect-ypk', fd, (p) => setYpkUploadProg(p));

      setYpkPreviewToken(res.previewToken);
      setYpkPreviewReport(res.report);
      if (!ypkTargetId.trim()) setYpkTargetId(res.suggestedPackageId);
      if (!ypkTargetName.trim()) setYpkTargetName(res.suggestedName);
      notify(`已透视 ${file.name}：共 ${res.report.totalFiles} 个文件 · ${res.report.cardCount} 张卡片，核对无误点击确认即可秒级导入！`);
    } catch (err) {
      notify(err instanceof Error ? err.message : '透视解析 .ypk 失败', 'err');
    } finally {
      setYpkUploadProg(null);
    }
  };

  const confirmImportPreviewedYpk = async () => {
    if (!ypkPreviewToken) return;
    setYpkImporting(true);
    try {
      const res = await apiFetch('/api/v1/diy/packages/import-ypk', {
        method: 'POST',
        body: JSON.stringify({
          previewToken: ypkPreviewToken,
          packageId: ypkTargetId.trim() || undefined,
          name: ypkTargetName.trim() || undefined,
          merge: ypkMergeMode,
          autoReload: ypkAutoReload
        })
      });
      const r = res.importResult;
      setSelectedPkgId(res.packageId);
      setYpkTargetId('');
      setYpkTargetName('');
      setYpkPreviewToken(null);
      setYpkPreviewReport(null);
      setShowYpkImporter(false);
      notify(
        `YPK 导入成功 [${res.packageId}]：卡片 ${r.totalCards} 张 · 脚本 ${r.importedScripts} 个 · 卡图 ${r.importedImages} 张 · 卡组 ${r.importedDecks} 套${
          res.reload ? `（已热重载至 Catalog v${res.reload.catalogVersion}）` : ''
        }`
      );
      await loadAll();
      await loadPackageCards(res.packageId);
    } catch (err) {
      notify(err instanceof Error ? err.message : '导入 .ypk 失败', 'err');
    } finally {
      setYpkImporting(false);
    }
  };

  const openInstalledPkgInspector = async (pkgId: string) => {
    try {
      const res = await apiFetch<{ report: YpkInspectionReport }>(
        `/api/v1/diy/packages/${encodeURIComponent(pkgId)}/inspect`
      );
      setExplorerModalTitle(`受管 DIY 包文件树与可视化透视 · ${pkgId}`);
      setExplorerModalReport(res.report);
      setExplorerModalResolver(createInstalledPackageResolver('/api/v1', pkgId));
    } catch (err) {
      notify(err instanceof Error ? err.message : '读取包内部文件清单失败', 'err');
    }
  };

  const openProposalYpkInspector = async (proposalId: string, title: string) => {
    try {
      const res = await apiFetch<{ report: YpkInspectionReport }>(
        `/api/v1/diy/proposals/${encodeURIComponent(proposalId)}/inspect-ypk`
      );
      setExplorerModalTitle(`待审 .ypk 提案全包透视 · ${title}`);
      setExplorerModalReport(res.report);
      setExplorerModalResolver(createProposalYpkResolver(proposalId));
    } catch (err) {
      notify(err instanceof Error ? err.message : '读取待审 .ypk 详情失败', 'err');
    }
  };

  const runChaosPreview = async () => {
    try {
      const res = await apiFetch('/api/v1/decks/chaos-preview', {
        method: 'POST',
        body: JSON.stringify({ mainSize: 40, pureChaos: false })
      });
      setChaosPreview(res);
    } catch (err) {
      notify(err instanceof Error ? err.message : '试抽失败', 'err');
    }
  };

  const searchCards = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const res = await apiFetch(`/api/v1/cards/search?q=${encodeURIComponent(cardQuery)}&limit=30`);
      setCards(res.cards || []);
    } catch (err) {
      notify(err instanceof Error ? err.message : '查询失败', 'err');
    }
  };

  const filteredPkgCards = useMemo(() => {
    return pkgCards.filter((item: any) => {
      const c = item.card;
      const t = Number(c.type || 0);
      if (explorerFilter === 'monster' && (t & CARD_BITS.MONSTER) === 0) return false;
      if (explorerFilter === 'spell' && ((t & CARD_BITS.SPELL) === 0 || (t & CARD_BITS.MONSTER) !== 0)) return false;
      if (explorerFilter === 'trap' && ((t & CARD_BITS.TRAP) === 0 || (t & CARD_BITS.MONSTER) !== 0)) return false;
      if (explorerFilter === 'no_script' && item.hasScript) return false;
      if (explorerFilter === 'no_image' && item.hasImage) return false;

      if (cardFilter.trim()) {
        const kw = cardFilter.trim().toLowerCase();
        return (
          String(c.id).includes(kw) ||
          String(c.name || '').toLowerCase().includes(kw) ||
          String(c.desc || '').toLowerCase().includes(kw)
        );
      }
      return true;
    });
  }, [pkgCards, explorerFilter, cardFilter]);

  const pendingProposalCount = proposals.filter((p) => p.status === 'pending').length;
  const currentArtworkUrl =
    selectedPkgId && hasCardImage
      ? `/api/v1/diy/packages/${encodeURIComponent(selectedPkgId)}/cards/${Number(editingCard.id)}/image?v=${imgCacheBust}`
      : null;

  return (
    <div className="space-y-4 animate-fade-in">
      <YpkExplorerModal
        open={Boolean(explorerModalReport && explorerModalResolver)}
        title={explorerModalTitle}
        report={explorerModalReport}
        resolver={explorerModalResolver}
        accent="indigo"
        onClose={() => {
          setExplorerModalReport(null);
          setExplorerModalResolver(null);
        }}
      />
      {/* 顶层三工作面导航条 + 全局热重载 */}
      <div className="glass-card rounded-2xl p-3 sm:px-4 flex flex-col md:flex-row md:items-center justify-between gap-3">
        <div className="flex items-center gap-1.5 overflow-x-auto no-scrollbar">
          <button
            type="button"
            onClick={() => setWorkspace('workbench')}
            className={`px-3.5 py-2 rounded-xl text-xs font-bold flex items-center gap-1.5 shrink-0 transition cursor-pointer ${
              workspace === 'workbench'
                ? 'bg-gradient-to-r from-indigo-600 to-purple-600 text-white shadow-lg shadow-indigo-950/60'
                : 'glass-subcard text-slate-300 hover:text-white'
            }`}
          >
            <Palette className="w-4 h-4" />
            <span>DIY 制卡 IDE 工作台</span>
          </button>

          <button
            type="button"
            onClick={() => setWorkspace('proposals')}
            className={`px-3.5 py-2 rounded-xl text-xs font-bold flex items-center gap-1.5 shrink-0 transition cursor-pointer ${
              workspace === 'proposals'
                ? 'bg-gradient-to-r from-indigo-600 to-purple-600 text-white shadow-lg shadow-indigo-950/60'
                : 'glass-subcard text-slate-300 hover:text-white'
            }`}
          >
            <FileCheck2 className="w-4 h-4" />
            <span>提案审批中心</span>
            {pendingProposalCount > 0 && (
              <span className="px-1.5 py-0.2 rounded-full bg-amber-500 text-slate-950 text-[10px] font-black">
                {pendingProposalCount}
              </span>
            )}
          </button>

          <button
            type="button"
            onClick={() => setWorkspace('pool_catalog')}
            className={`px-3.5 py-2 rounded-xl text-xs font-bold flex items-center gap-1.5 shrink-0 transition cursor-pointer ${
              workspace === 'pool_catalog'
                ? 'bg-gradient-to-r from-indigo-600 to-purple-600 text-white shadow-lg shadow-indigo-950/60'
                : 'glass-subcard text-slate-300 hover:text-white'
            }`}
          >
            <Sparkles className="w-4 h-4" />
            <span>随机盲盒池 & 六维卡查</span>
          </button>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          {packages.some((p) => p.manifest?.enabled) && (
            <a
              href="/api/v1/diy/export-all.ypk"
              download
              className="min-h-[34px] inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl bg-emerald-600/20 border border-emerald-500/40 text-emerald-300 text-xs font-medium hover:bg-emerald-600/30"
              title="将所有已启用 DIY 包按优先级合并打包为单个客户端 .ypk"
            >
              <Download className="w-3.5 h-3.5" /> 导出全服合集 .ypk
            </a>
          )}

          {hasPerm(user, 'diy:reload') && (
            <button
              type="button"
              onClick={async () => {
                const r = await apiFetch('/api/v1/diy/reload', { method: 'POST', body: '{}' });
                notify(`已热重载至 Catalog v${r.reload.catalogVersion}（进行中对局零中断）`);
                loadAll();
              }}
              className="min-h-[34px] px-3.5 py-1.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold flex items-center gap-1.5 cursor-pointer"
            >
              <RefreshCw className="w-3.5 h-3.5" /> 免重启热重载
            </button>
          )}
        </div>
      </div>

      {/* ==================== 工作面 1：DataEditorY + YGODIY 风格三栏制卡 IDE ==================== */}
      {workspace === 'workbench' && (
        <>
          {/* 受管 DIY 包工具带 */}
          <div className="glass-card rounded-2xl p-3.5 space-y-3">
            <div className="flex flex-wrap items-center justify-between gap-2.5">
              <div className="flex flex-wrap items-center gap-2">
                <span className="text-xs font-bold text-slate-300 flex items-center gap-1.5">
                  <Layers className="w-4 h-4 text-indigo-400" /> 工作包:
                </span>
                <select
                  value={selectedPkgId}
                  onChange={(e) => {
                    setSelectedPkgId(e.target.value);
                    loadPackageCards(e.target.value);
                  }}
                  className="w-full sm:w-auto px-3 py-1.5 text-xs font-mono input-modern input-modern-indigo"
                >
                  {packages.length === 0 && <option value="">-- 请先新建或导入 .ypk 扩展包 --</option>}
                  {packages.map((p) => (
                    <option key={p.manifest.id} value={p.manifest.id}>
                      {p.manifest.name} ({p.manifest.id}) · {p.validation.cardCount} 卡 · P{p.manifest.priority}
                    </option>
                  ))}
                </select>

                {hasPerm(user, 'diy:edit') && (
                  <button
                    type="button"
                    onClick={() => {
                      setShowNewPkgForm((v) => !v);
                      setShowYpkImporter(false);
                    }}
                    className="min-h-[32px] px-2.5 py-1 rounded-xl glass-subcard hover:border-indigo-400/50 text-xs text-slate-200 flex items-center gap-1 cursor-pointer"
                  >
                    <FolderPlus className="w-3.5 h-3.5 text-indigo-400" /> 新建包
                  </button>
                )}

                {(hasPerm(user, 'diy:upload') || hasPerm(user, 'diy:edit')) && (
                  <button
                    type="button"
                    onClick={() => {
                      setShowYpkImporter((v) => !v);
                      setShowNewPkgForm(false);
                    }}
                    className={`min-h-[32px] inline-flex items-center gap-1.5 px-3 py-1 rounded-xl border text-xs font-semibold cursor-pointer transition ${
                      showYpkImporter
                        ? 'bg-purple-600 border-purple-400 text-white'
                        : 'bg-purple-600/20 border-purple-500/40 text-purple-200 hover:bg-purple-600/30'
                    }`}
                  >
                    <FolderUp className="w-3.5 h-3.5" /> 导入 .ypk 整包
                  </button>
                )}
              </div>

              <div className="flex flex-wrap items-center gap-2">
                {selectedPkgId && (
                  <>
                    <button
                      type="button"
                      onClick={() => openInstalledPkgInspector(selectedPkgId)}
                      className="min-h-[32px] inline-flex items-center gap-1 px-3 py-1 rounded-xl bg-indigo-500/15 border border-indigo-500/35 text-indigo-200 text-xs font-medium hover:bg-indigo-500/25 cursor-pointer"
                    >
                      <Eye className="w-3.5 h-3.5" /> 包内文件与可视化
                    </button>
                    <a
                      href={`/api/v1/diy/packages/${encodeURIComponent(selectedPkgId)}/export.ypk`}
                      download
                      className="min-h-[32px] inline-flex items-center gap-1 px-3 py-1 rounded-xl bg-sky-600/20 border border-sky-500/40 text-sky-300 text-xs font-medium hover:bg-sky-600/30"
                    >
                      <Download className="w-3.5 h-3.5" /> 导出本包 .ypk
                    </a>
                  </>
                )}

                {selectedPkgId && hasPerm(user, 'diy:upload') && (
                  <label className="min-h-[32px] inline-flex items-center gap-1 px-3 py-1 rounded-xl glass-subcard hover:border-indigo-400/50 text-slate-300 text-xs cursor-pointer">
                    <Upload className="w-3.5 h-3.5 text-indigo-400" /> 批量导入文件
                    <input
                      type="file"
                      multiple
                      accept=".cdb,.lua,.ydk,.ypk,.zip,.conf"
                      className="hidden"
                      onChange={(e) => {
                        uploadFiles(e.target.files);
                        e.target.value = '';
                      }}
                    />
                  </label>
                )}

                {selectedPkgId && hasPerm(user, 'diy:edit') && (
                  <button
                    type="button"
                    onClick={async () => {
                      await apiFetch(`/api/v1/diy/packages/${encodeURIComponent(selectedPkgId)}`, { method: 'DELETE' });
                      setSelectedPkgId('');
                      setPkgCards([]);
                      notify('包已安全移入 .trash 回收站，随时可恢复');
                      loadAll();
                    }}
                    className="min-h-[32px] px-2.5 py-1 rounded-xl bg-rose-950/60 border border-rose-500/30 text-rose-300 text-xs cursor-pointer"
                  >
                    卸载入回收站
                  </button>
                )}
              </div>
            </div>

            {showNewPkgForm && (
              <form
                onSubmit={createPackage}
                className="glass-subcard rounded-xl p-3 border border-indigo-500/35 flex flex-wrap items-center gap-2 text-xs animate-fade-in"
              >
                <span className="font-semibold text-indigo-300">新建受管 DIY 包：</span>
                <input
                  value={newPkgId}
                  onChange={(e) => setNewPkgId(e.target.value)}
                  placeholder="包英文标识 (如 starlight-pack)"
                  className="flex-1 min-w-44 px-3 py-1.5 font-mono input-modern input-modern-indigo"
                  required
                />
                <input
                  value={newPkgName}
                  onChange={(e) => setNewPkgName(e.target.value)}
                  placeholder="中文显示名称 (如 星辉试作卡包)"
                  className="flex-1 min-w-44 px-3 py-1.5 input-modern input-modern-indigo"
                />
                <button className="px-3.5 py-1.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white font-semibold cursor-pointer">
                  立即创建
                </button>
              </form>
            )}

            {showYpkImporter && (
              <div className="glass-subcard rounded-2xl p-4 border border-purple-500/35 space-y-3.5 text-xs animate-fade-in">
                <div className="font-semibold text-purple-200 flex items-center justify-between">
                  <span>透视 & 导入标准 .ypk / .zip 扩展包（支持 256MB 大包，先查看包内文件树与卡片再一键入库）</span>
                  <button
                    type="button"
                    onClick={() => setShowYpkImporter(false)}
                    className="text-slate-400 hover:text-white cursor-pointer"
                  >
                    收起
                  </button>
                </div>

                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-2.5 items-center">
                  <input
                    value={ypkTargetId}
                    onChange={(e) => setYpkTargetId(e.target.value)}
                    placeholder="目标包 ID（留空自动按文件名生成）"
                    className="px-3 py-2 input-modern input-modern-indigo font-mono"
                  />
                  <input
                    value={ypkTargetName}
                    onChange={(e) => setYpkTargetName(e.target.value)}
                    placeholder="包显示名（留空自动读取原包名）"
                    className="px-3 py-2 input-modern input-modern-indigo"
                  />
                  <div className="flex flex-wrap items-center gap-3 px-1">
                    <label className="inline-flex items-center gap-1.5 text-slate-300 cursor-pointer">
                      <input
                        type="checkbox"
                        checked={ypkMergeMode}
                        onChange={(e) => setYpkMergeMode(e.target.checked)}
                        className="w-4 h-4 rounded accent-indigo-500"
                      />
                      同名包增量合并
                    </label>
                    <label className="inline-flex items-center gap-1.5 text-slate-300 cursor-pointer">
                      <input
                        type="checkbox"
                        checked={ypkAutoReload}
                        onChange={(e) => setYpkAutoReload(e.target.checked)}
                        className="w-4 h-4 rounded accent-emerald-500"
                      />
                      自动热重载
                    </label>
                  </div>
                  <label
                    className={`min-h-[38px] inline-flex items-center justify-center gap-1.5 px-4 py-2 rounded-xl bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white font-semibold shadow-md cursor-pointer ${
                      ypkUploadProg ? 'opacity-60 pointer-events-none' : ''
                    }`}
                  >
                    <FolderUp className="w-4 h-4" />
                    <span>
                      {ypkUploadProg
                        ? `上传解析中 (${ypkUploadProg.percent}%)...`
                        : ypkPreviewReport
                          ? '重新选择 .ypk / .zip'
                          : '选择 .ypk / .zip 透视预览'}
                    </span>
                    <input
                      type="file"
                      accept=".ypk,.zip"
                      className="hidden"
                      onChange={(e) => {
                        previewYpkBeforeImport(e.target.files);
                        e.target.value = '';
                      }}
                    />
                  </label>
                </div>

                {ypkUploadProg && (
                  <div className="space-y-1">
                    <div className="h-2 rounded-full bg-slate-900 overflow-hidden border border-slate-800">
                      <div
                        className="h-full bg-gradient-to-r from-purple-500 to-indigo-400 transition-all duration-150"
                        style={{ width: `${ypkUploadProg.percent}%` }}
                      />
                    </div>
                    <div className="text-[11px] font-mono text-indigo-200">
                      {ypkUploadProg.percent}% · {formatBytes(ypkUploadProg.loaded)} / {formatBytes(ypkUploadProg.total)}
                    </div>
                  </div>
                )}

                {ypkPreviewReport && ypkPreviewToken && (
                  <YpkPackageExplorer
                    report={ypkPreviewReport}
                    resolver={createPreviewTokenResolver('/api/v1', ypkPreviewToken)}
                    accent="indigo"
                    actionFooter={
                      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                        <span className="text-slate-300">
                          核对包内文件、卡面与脚本无误后，点击右侧按钮立即原子导入至受管包{' '}
                          <code className="text-indigo-300 font-mono">{ypkTargetId || ypkPreviewReport.archiveName}</code>
                        </span>
                        <button
                          type="button"
                          disabled={!ypkPreviewReport.isSafe || ypkImporting}
                          onClick={confirmImportPreviewedYpk}
                          className="min-h-[38px] px-5 py-2 rounded-xl bg-emerald-600 hover:bg-emerald-500 disabled:opacity-45 text-white font-bold shrink-0 cursor-pointer"
                        >
                          {ypkImporting ? '正在原子写入...' : '确认原子导入此 .ypk 扩展包'}
                        </button>
                      </div>
                    }
                  />
                )}
              </div>
            )}

            {conflicts?.conflicts?.length > 0 && (
              <div className="px-3 py-2 rounded-xl bg-amber-950/50 border border-amber-500/30 text-xs text-amber-200 flex items-center gap-2">
                <AlertTriangle className="w-4 h-4 text-amber-400 shrink-0" />
                <span>
                  冲突提示：共 {conflicts.conflicts.length} 张卡覆盖了基础卡库或跨包重复定义（
                  {conflicts.conflicts
                    .slice(0, 5)
                    .map((c: any) => `#${c.cardId} ${c.cardName}`)
                    .join('、')}
                  ）
                </span>
              </div>
            )}

            {trash.length > 0 && (
              <div className="flex flex-wrap items-center gap-2 pt-2 border-t border-slate-800/80 text-xs">
                <span className="text-slate-400">回收站 (.trash)：</span>
                {trash.map((t) => (
                  <span key={t} className="inline-flex items-center gap-1.5 glass-subcard px-2.5 py-1 rounded-lg">
                    <span className="font-mono text-[11px]">{t}</span>
                    <button
                      type="button"
                      onClick={async () => {
                        await apiFetch(`/api/v1/diy/trash/${encodeURIComponent(t)}/restore`, {
                          method: 'POST',
                          body: '{}'
                        });
                        notify('已从回收站恢复扩展包');
                        loadAll();
                      }}
                      className="text-emerald-400 hover:underline cursor-pointer"
                    >
                      恢复
                    </button>
                  </span>
                ))}
              </div>
            )}
          </div>

          {/* 核心三栏 IDE：左卡表导航 (3) + 中实时实体卡面预览台 (4) + 右结构化属性/Lua 沙盒检视器 (5) */}
          <div className="grid grid-cols-1 lg:grid-cols-12 gap-4 items-start">
            {/* 左栏：色彩编码卡片资源管理器 */}
            <div className="lg:col-span-3 glass-card rounded-2xl p-3.5 space-y-3 flex flex-col">
              <div className="flex items-center justify-between gap-2">
                <span className="text-xs font-bold text-slate-200">
                  卡表目录 ({filteredPkgCards.length}/{pkgCards.length})
                </span>
                {hasPerm(user, 'diy:edit') && (
                  <button
                    type="button"
                    onClick={startNewCardDraft}
                    className="px-2.5 py-1 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold flex items-center gap-1 cursor-pointer"
                  >
                    <Plus className="w-3.5 h-3.5" /> 新建单卡
                  </button>
                )}
              </div>

              <div className="relative">
                <Search className="w-3.5 h-3.5 text-slate-500 absolute left-3 top-1/2 -translate-y-1/2" />
                <input
                  value={cardFilter}
                  onChange={(e) => setCardFilter(e.target.value)}
                  placeholder="搜卡名、卡密或效果词..."
                  className="w-full pl-8 pr-3 py-1.5 text-xs input-modern input-modern-indigo"
                />
              </div>

              <div className="flex items-center gap-1 overflow-x-auto no-scrollbar pb-0.5">
                {(
                  [
                    ['all', '全部'],
                    ['monster', '怪兽'],
                    ['spell', '魔法'],
                    ['trap', '陷阱'],
                    ['no_script', '缺脚本'],
                    ['no_image', '缺图']
                  ] as [ExplorerFilter, string][]
                ).map(([k, label]) => (
                  <button
                    key={k}
                    type="button"
                    onClick={() => setExplorerFilter(k)}
                    className={`px-2 py-0.5 rounded-full text-[10px] font-medium shrink-0 cursor-pointer ${
                      explorerFilter === k
                        ? 'bg-indigo-600 text-white'
                        : 'bg-slate-900 text-slate-400 hover:text-slate-200'
                    }`}
                  >
                    {label}
                  </button>
                ))}
              </div>

              <div className="space-y-1.5 max-h-64 lg:max-h-[540px] overflow-y-auto pr-0.5">
                {filteredPkgCards.map((item: any) => {
                  const c = item.card;
                  const fTheme = resolveCardFrameTheme(Number(c.type || 0));
                  const isSelected = Number(editingCard.id) === Number(c.id);
                  const isLink = (c.type & CARD_BITS.LINK) !== 0;

                  return (
                    <div
                      key={c.id}
                      onClick={() => selectCardForEdit(selectedPkgId, c.id)}
                      className={`p-2.5 rounded-xl border text-xs cursor-pointer transition ${
                        isSelected
                          ? 'bg-indigo-950/65 border-indigo-400 shadow-sm'
                          : 'glass-subcard hover:border-slate-700'
                      }`}
                    >
                      <div className="flex items-center justify-between gap-1.5">
                        <div className="flex items-center gap-2 min-w-0">
                          <span className={`w-2.5 h-2.5 rounded-sm shrink-0 ${fTheme.dotColor}`} title={fTheme.kindLabel} />
                          <span className="font-semibold text-slate-100 truncate">{c.name || '<未命名>'}</span>
                        </div>
                        <span className="font-mono text-[10px] text-slate-400 shrink-0">#{c.id}</span>
                      </div>

                      <div className="flex items-center justify-between gap-2 mt-1 text-[10px] text-slate-400">
                        <span className="truncate">
                          {fTheme.kindLabel}
                          {!fTheme.isSpell && !fTheme.isTrap
                            ? isLink
                              ? ` · L-${countLinkArrows(c.linkMarker) || c.level} · ATK ${formatStatDisplay(c.attack)}`
                              : ` · ★${c.level} · ${formatStatDisplay(c.attack)}/${formatStatDisplay(c.defense)}`
                            : ''}
                        </span>
                        <span className="flex items-center gap-1 shrink-0">
                          <span
                            className={`px-1 rounded ${
                              item.hasScript ? 'bg-emerald-500/15 text-emerald-300' : 'bg-amber-500/15 text-amber-300'
                            }`}
                          >
                            Lua
                          </span>
                          <span
                            className={`px-1 rounded ${
                              item.hasImage ? 'bg-sky-500/15 text-sky-300' : 'bg-slate-800 text-slate-500'
                            }`}
                          >
                            图
                          </span>
                        </span>
                      </div>
                    </div>
                  );
                })}

                {filteredPkgCards.length === 0 && (
                  <div className="py-10 text-center text-xs text-slate-500 space-y-1">
                    <div>{pkgCards.length === 0 ? '当前工作包尚无卡片' : '无符合筛选条件的卡片'}</div>
                    <div className="text-[11px]">点击右上角「新建单卡」或上方「导入 .ypk 整包」</div>
                  </div>
                )}
              </div>

              {selectedPkgId && (
                <div className="pt-2.5 border-t border-slate-800/80 space-y-2">
                  <div className="flex items-center justify-between text-xs">
                    <button
                      type="button"
                      onClick={() => setShowStringsPanel((v) => !v)}
                      className="text-slate-300 font-semibold hover:text-white cursor-pointer"
                    >
                      {showStringsPanel ? '▾' : '▸'} 本包自定义字段 (strings.conf)
                    </button>
                    {showStringsPanel && hasPerm(user, 'diy:edit') && (
                      <button
                        type="button"
                        onClick={saveStringsConf}
                        className="text-indigo-400 hover:underline cursor-pointer"
                      >
                        保存配置
                      </button>
                    )}
                  </div>
                  {showStringsPanel && (
                    <CodeMirrorEditor
                      value={stringsConfText}
                      onChange={setStringsConfText}
                      language="conf"
                      accent="indigo"
                      placeholder="!setname 0xa100 星辉"
                      minHeight="84px"
                      maxHeight="150px"
                      compact
                    />
                  )}
                </div>
              )}
            </div>

            {/* 中栏：实时动态游戏王实体卡面预览台 + 快捷保存底座 */}
            <div ref={stageRef} className="lg:col-span-4 lg:sticky lg:top-20 space-y-3">
              <div className="glass-card rounded-2xl p-4 bg-[radial-gradient(circle_at_50%_20%,rgba(99,102,241,0.16),transparent_70%)] space-y-3.5">
                <div className="flex items-center justify-between text-xs">
                  <span className="font-bold text-slate-200 flex items-center gap-1.5">
                    <Sparkles className="w-3.5 h-3.5 text-amber-400" /> 实时卡面渲染预览
                  </span>
                  <span className="text-[10px] text-slate-400">点击或拖拽图片至卡图框即可上传</span>
                </div>

                <YgoCardVisualPreview
                  card={editingCard}
                  imageUrl={currentArtworkUrl}
                  canUploadImage={Boolean(selectedPkgId && hasPerm(user, 'diy:edit'))}
                  onSelectImageFiles={uploadCardArtwork}
                />

                {hasPerm(user, 'diy:edit') && (
                  <div className="space-y-2 pt-1">
                    <button
                      type="button"
                      disabled={savingCard}
                      onClick={() => saveCardDirect(true)}
                      className="w-full min-h-[42px] py-2.5 rounded-xl bg-gradient-to-r from-emerald-600 to-teal-500 hover:from-emerald-500 hover:to-teal-400 disabled:opacity-50 text-white text-xs font-bold flex items-center justify-center gap-1.5 shadow-lg shadow-emerald-950/60 cursor-pointer"
                    >
                      <Save className="w-4 h-4" />
                      <span>{savingCard ? '正在校验保存...' : '原子保存并热重载 (Ctrl+S)'}</span>
                    </button>

                    <div className="grid grid-cols-4 gap-1.5 text-[11px]">
                      <button
                        type="button"
                        onClick={() => saveCardDirect(false)}
                        className="py-1.5 rounded-xl glass-subcard hover:border-slate-600 text-slate-300 font-medium cursor-pointer"
                        title="仅原子写入包目录，暂不刷新对局服"
                      >
                        仅存包内
                      </button>
                      <button
                        type="button"
                        onClick={submitAsProposal}
                        className="py-1.5 rounded-xl bg-amber-500/15 hover:bg-amber-500/25 border border-amber-500/30 text-amber-200 font-medium cursor-pointer"
                      >
                        转交提案
                      </button>
                      <button
                        type="button"
                        onClick={cloneCurrentCard}
                        className="py-1.5 rounded-xl glass-subcard hover:border-indigo-400/50 text-indigo-200 flex items-center justify-center gap-1 cursor-pointer"
                      >
                        <CopyPlus className="w-3 h-3" /> 克隆
                      </button>
                      <button
                        type="button"
                        onClick={deleteCurrentCard}
                        className="py-1.5 rounded-xl bg-rose-950/50 hover:bg-rose-900/60 border border-rose-500/30 text-rose-300 flex items-center justify-center gap-1 cursor-pointer"
                      >
                        <Trash2 className="w-3 h-3" /> 删除
                      </button>
                    </div>
                  </div>
                )}
              </div>
            </div>

            {/* 右栏：结构化属性检视器 & CodeMirror Lua 引擎沙盒 */}
            <div className="lg:col-span-5 glass-card rounded-2xl p-4 space-y-4">
              <div className="flex items-center gap-1.5 p-1 rounded-xl bg-slate-950/90 border border-slate-800 overflow-x-auto no-scrollbar">
                {(
                  [
                    ['stats', '1. 卡面与数值'],
                    ['text', '2. 效果与提示串'],
                    ['script', '3. Lua 脚本 & 沙盒']
                  ] as [InspectorTab, string][]
                ).map(([key, label]) => (
                  <button
                    key={key}
                    type="button"
                    onClick={() => setInspectorTab(key)}
                    className={`flex-1 py-2 px-3 rounded-lg text-xs font-semibold shrink-0 transition cursor-pointer ${
                      inspectorTab === key
                        ? 'bg-indigo-600 text-white shadow'
                        : 'text-slate-400 hover:text-slate-200'
                    }`}
                  >
                    {label}
                  </button>
                ))}
              </div>

              {inspectorTab === 'stats' && (
                <CardBaseStatsInspector
                  card={editingCard}
                  onChange={setEditingCard}
                  stringsConfText={stringsConfText}
                  accent="indigo"
                />
              )}

              {inspectorTab === 'text' && (
                <CardTextAndStringsInspector
                  card={editingCard}
                  onChange={setEditingCard}
                  accent="indigo"
                  showHintStrings={true}
                />
              )}

              {inspectorTab === 'script' && (
                <div className="space-y-3 text-xs">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <button
                      type="button"
                      onClick={() => {
                        setLuaScript(generateSmartLuaTemplate(editingCard));
                        notify('已按当前卡片种类（超量/同调/连接/灵摆/魔陷）生成对应 OCGCore 脚本骨架');
                      }}
                      className="min-h-[34px] px-3 py-1.5 rounded-xl glass-subcard hover:border-indigo-400/60 text-indigo-200 font-medium flex items-center gap-1.5 cursor-pointer"
                    >
                      <Wand2 className="w-3.5 h-3.5 text-amber-400" /> 按当前卡种生成 Lua 骨架
                    </button>

                    {hasPerm(user, 'diy:sandbox_test') && (
                      <button
                        type="button"
                        onClick={runSandbox}
                        disabled={sandboxRunning}
                        className="min-h-[34px] px-3.5 py-1.5 rounded-xl bg-purple-600 hover:bg-purple-500 text-white font-semibold flex items-center gap-1.5 shadow cursor-pointer"
                      >
                        <Play className="w-3.5 h-3.5" />
                        <span>{sandboxRunning ? '沙盒试跑中...' : '运行 ocgcore 情景沙盒'}</span>
                      </button>
                    )}
                  </div>

                  <CodeMirrorEditor
                    value={luaScript}
                    onChange={setLuaScript}
                    language="lua"
                    accent="indigo"
                    title={`script/c${editingCard.id}.lua`}
                    minHeight="360px"
                    maxHeight="540px"
                  />

                  {sandboxResult && (
                    <div
                      className={`p-3.5 rounded-2xl border space-y-1.5 animate-fade-in ${
                        sandboxResult.success
                          ? 'bg-emerald-950/45 border-emerald-500/40 text-emerald-200'
                          : 'bg-rose-950/55 border-rose-500/40 text-rose-200'
                      }`}
                    >
                      <div className="font-bold flex flex-wrap items-center justify-between gap-2">
                        <span className="flex items-center gap-1.5">
                          {sandboxResult.success ? (
                            <CheckCircle2 className="w-4 h-4 text-emerald-400" />
                          ) : (
                            <XCircle className="w-4 h-4 text-rose-400" />
                          )}
                          {sandboxResult.success ? 'ocgcore 引擎沙盒预检通过' : '沙盒拦截：发现错误'} ·{' '}
                          {sandboxResult.elapsedMs} ms
                        </span>
                        <span className="font-mono text-[11px]">
                          可通召:{sandboxResult.summonableCount} · 可发动:{sandboxResult.activatableCount} · P0 LP:{' '}
                          {sandboxResult.postPlayer0Lp}
                        </span>
                      </div>
                      {sandboxResult.staticAuditViolations?.length > 0 && (
                        <div className="font-mono text-[11px] break-all">
                          静态安全拦截：{sandboxResult.staticAuditViolations.join(' | ')}
                        </div>
                      )}
                      {sandboxResult.luaErrors?.length > 0 && (
                        <div className="font-mono text-[11px] break-all">
                          Lua 运行时异常：{sandboxResult.luaErrors.join(' | ')}
                        </div>
                      )}
                      {sandboxResult.observedMessages?.length > 0 && (
                        <div className="text-[11px] text-slate-300">
                          引擎消息流：{sandboxResult.observedMessages.join(' → ')}
                        </div>
                      )}
                    </div>
                  )}
                </div>
              )}
            </div>
          </div>
        </>
      )}

      {/* ==================== 工作面 2：可视化提案审批中心 (Proposals) ==================== */}
      {workspace === 'proposals' && (
        <div className="glass-card rounded-2xl p-4 sm:p-5 space-y-4 text-xs animate-fade-in">
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
            <div>
              <h2 className="text-sm sm:text-base font-bold flex items-center gap-2">
                <FileCheck2 className="w-4 h-4 text-indigo-400" /> DIY 社区投稿与外部 MCP 提案审批门禁
              </h2>
              <p className="text-[11px] text-slate-400 mt-0.5">
                审阅玩家或受限 AI 提交的单卡 / <code className="font-mono text-indigo-300">.ypk</code> 整包变更，点击批准即可自动落盘并热重载全服卡库
              </p>
            </div>
            <input
              value={reviewComment}
              onChange={(e) => setReviewComment(e.target.value)}
              placeholder="统一填写可选审批意见备注..."
              className="w-full sm:w-72 px-3 py-2 input-modern input-modern-indigo"
            />
          </div>

          <div className="space-y-3">
            {proposals.map((p) => {
              const parsed = parseProposalDiff(p.diffJson);
              const cardsInProp: any[] = parsed?.Cards || parsed?.cards || [];
              const imagesInProp: any[] = parsed?.Images || parsed?.images || [];
              const decksInProp: any[] = parsed?.Decks || parsed?.decks || [];
              const stagedYpk: string | undefined = parsed?.StagedYpkFileName || parsed?.stagedYpkFileName;

              return (
                <div key={p.id} className="glass-subcard rounded-2xl p-4 space-y-3">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <span className="font-bold text-sm text-slate-100">{p.title}</span>{' '}
                      <span className="px-2 py-0.5 rounded-md bg-indigo-500/15 border border-indigo-500/30 font-mono text-[11px] text-indigo-300">
                        目标包: {p.packageId}
                      </span>
                    </div>
                    <div className="flex items-center gap-2">
                      <span className="text-[11px] text-slate-400">
                        投稿人: {p.proposerName || '匿名'} · {new Date(p.createdAt).toLocaleString()}
                      </span>
                      <span
                        className={`px-2.5 py-0.5 rounded-full text-[11px] font-semibold ${
                          p.status === 'approved'
                            ? 'bg-emerald-500/20 text-emerald-300'
                            : p.status === 'rejected'
                              ? 'bg-rose-500/20 text-rose-300'
                              : 'bg-amber-500/20 text-amber-300'
                        }`}
                      >
                        {p.status === 'pending' ? '待审阅' : p.status === 'approved' ? '已批准' : '已驳回'}
                      </span>
                    </div>
                  </div>

                  {/* 可视化渲染提案内卡片摘要 */}
                  {cardsInProp.length > 0 && (
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                      {cardsInProp.slice(0, 6).map((ci: any, idx: number) => {
                        const rawCard = ci.Card || ci.card || {};
                        const cid = Number(rawCard.Id ?? rawCard.id ?? 0);
                        const cname = rawCard.Name ?? rawCard.name ?? '未命名';
                        const cdesc = rawCard.Desc ?? rawCard.desc ?? '';
                        const ctype = Number(rawCard.Type ?? rawCard.type ?? 0x21);
                        const catk = Number(rawCard.Attack ?? rawCard.attack ?? 0);
                        const cdef = Number(rawCard.Defense ?? rawCard.defense ?? 0);
                        const lua = ci.LuaScript ?? ci.luaScript ?? '';
                        const matchedImg = imagesInProp.find(
                          (im: any) => Number(im.CardId ?? im.cardId) === cid
                        );
                        const imgB64 = matchedImg?.Base64Data ?? matchedImg?.base64Data;
                        const ft = resolveCardFrameTheme(ctype);

                        return (
                          <div key={`${cid}-${idx}`} className="rounded-xl bg-slate-950/85 border border-slate-800 p-3 space-y-2">
                            <div className="flex items-start gap-3">
                              {imgB64 && (
                                <img
                                  src={`data:image/png;base64,${imgB64}`}
                                  alt={cname}
                                  className="w-14 h-14 rounded-lg object-cover border border-slate-700 shrink-0"
                                />
                              )}
                              <div className="min-w-0 flex-1 space-y-1">
                                <div className="flex items-center justify-between gap-2">
                                  <span className="font-bold text-slate-100 flex items-center gap-1.5 truncate">
                                    <span className={`w-2.5 h-2.5 rounded-sm ${ft.dotColor}`} />
                                    {cname}
                                  </span>
                                  <span className="font-mono text-[11px] text-slate-400">#{cid}</span>
                                </div>
                                <div className="text-[11px] text-indigo-300 font-mono">
                                  {ft.kindLabel}
                                  {!ft.isSpell && !ft.isTrap ? ` · ATK ${formatStatDisplay(catk)} / DEF ${formatStatDisplay(cdef)}` : ''}
                                </div>
                                <div className="text-[11px] text-slate-300 line-clamp-3 whitespace-pre-wrap">{cdesc}</div>
                              </div>
                            </div>
                            {lua && (
                              <details className="text-[11px]">
                                <summary className="text-emerald-300 cursor-pointer flex items-center gap-1">
                                  <Code2 className="w-3.5 h-3.5" /> 展开审阅配套 Lua 脚本 (c{cid}.lua)
                                </summary>
                                <pre className="mt-1.5 p-2.5 rounded-lg bg-slate-900 text-emerald-200 font-mono text-[10.5px] max-h-44 overflow-auto">
                                  {lua}
                                </pre>
                              </details>
                            )}
                          </div>
                        );
                      })}
                    </div>
                  )}

                  {(cardsInProp.length > 6 || decksInProp.length > 0) && (
                    <div className="text-[11px] text-slate-400">
                      共包含卡片 {cardsInProp.length} 张 · 卡图 {imagesInProp.length} 张 · 主题卡组 {decksInProp.length} 套
                    </div>
                  )}

                  <div className="flex flex-wrap items-center justify-between gap-2 pt-1">
                    <div>
                      {stagedYpk && (
                        <button
                          type="button"
                          onClick={() => openProposalYpkInspector(p.id, p.title)}
                          className="min-h-[34px] inline-flex items-center gap-1.5 px-3.5 py-1.5 rounded-xl bg-indigo-500/15 hover:bg-indigo-500/25 border border-indigo-500/35 text-indigo-200 font-semibold cursor-pointer"
                        >
                          <Eye className="w-3.5 h-3.5" /> 透视查看待审 .ypk 全部文件树与卡片画廊
                        </button>
                      )}
                    </div>

                    {p.status === 'pending' && hasPerm(user, 'diy:review_proposal') && (
                      <div className="flex justify-end gap-2">
                        <button
                          type="button"
                          onClick={() => reviewProposal(p.id, false)}
                          className="min-h-[36px] px-3.5 py-1.5 rounded-xl bg-rose-600/80 hover:bg-rose-600 text-white font-medium cursor-pointer"
                        >
                          驳回提案
                        </button>
                        <button
                          type="button"
                          onClick={() => reviewProposal(p.id, true)}
                          className="min-h-[36px] px-4 py-1.5 rounded-xl bg-emerald-600 hover:bg-emerald-500 text-white font-bold cursor-pointer"
                        >
                          批准写入并热重载生效
                        </button>
                      </div>
                    )}
                  </div>
                </div>
              );
            })}

            {proposals.length === 0 && (
              <div className="py-12 text-center text-slate-500">当前暂无待审阅或历史 DIY 提案</div>
            )}
          </div>
        </div>
      )}

      {/* ==================== 工作面 3：随机盲盒池 & 六维卡查 ==================== */}
      {workspace === 'pool_catalog' && (
        <div className="space-y-5 animate-fade-in">
          <DeckSixDimRadarSection apiPrefix="/api/v1" chaosYdk={chaosPreview?.ydk} notify={notify} />

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
            <div className="glass-card rounded-2xl p-4 sm:p-5 space-y-4">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <h2 className="text-sm font-bold flex items-center gap-2">
                  <Sparkles className="w-4 h-4 text-purple-400" /> 随机盲盒池 ({poolDecks.length} 套) & 混沌组卡试抽
                </h2>
                <button
                  type="button"
                  onClick={runChaosPreview}
                  className="min-h-[34px] px-3.5 py-1.5 rounded-xl bg-purple-600 hover:bg-purple-500 text-white text-xs font-semibold cursor-pointer"
                >
                  试抽一套 40+15 混沌卡组
                </button>
              </div>

              {chaosPreview && (
                <div className="glass-subcard p-3.5 rounded-xl border border-purple-500/30 text-xs space-y-1.5">
                  <div className="font-semibold text-purple-300">
                    平衡配比结果：下级怪 {chaosPreview.breakdown.lowLevelMonsters} · 上级怪{' '}
                    {chaosPreview.breakdown.highLevelMonsters} · 魔法 {chaosPreview.breakdown.spells} · 陷阱{' '}
                    {chaosPreview.breakdown.traps} · 额外 {chaosPreview.extraCount}
                  </div>
                  <div className="text-[11px] text-slate-400 line-clamp-2">
                    主卡组抽样：{chaosPreview.sampleMainCards.map((c: any) => c.name).join('、')}
                  </div>
                </div>
              )}

              <div className="max-h-56 overflow-y-auto space-y-1.5 text-xs">
                {poolDecks.map((d) => (
                  <div key={d.id} className="flex items-center justify-between glass-subcard px-3 py-2 rounded-xl">
                    <span className="truncate">
                      {d.name} {d.hasDiyCards && <span className="text-purple-400">[DIY]</span>}
                    </span>
                    <span className="text-slate-400 font-mono shrink-0">
                      M:{d.mainCount} E:{d.extraCount} S:{d.sideCount}
                    </span>
                  </div>
                ))}
              </div>
            </div>

            <div className="glass-card rounded-2xl p-4 sm:p-5 space-y-3">
              <form onSubmit={searchCards} className="flex gap-2">
                <input
                  value={cardQuery}
                  onChange={(e) => setCardQuery(e.target.value)}
                  placeholder="输入卡密或卡名/效果关键字检索卡库..."
                  className="flex-1 min-w-0 px-3 py-2 text-xs input-modern input-modern-indigo"
                />
                <button className="min-h-[38px] px-4 py-1.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold shrink-0 cursor-pointer">
                  检索卡库
                </button>
              </form>
              {cards.length > 0 && (
                <div className="grid grid-cols-1 gap-2 max-h-60 overflow-y-auto">
                  {cards.map((c) => (
                    <div key={c.id} className="glass-subcard p-3 rounded-xl text-xs space-y-1">
                      <div className="flex items-center justify-between gap-2 font-semibold">
                        <span className="truncate">
                          {c.name} <span className="text-slate-400 font-mono">#{c.id}</span>
                        </span>
                        {c.isDiy && <span className="text-purple-400 text-[11px] shrink-0">DIY (ot=4)</span>}
                      </div>
                      <div className="text-[11px] text-slate-400 line-clamp-2">{c.desc}</div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
