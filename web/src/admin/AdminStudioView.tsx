import React, { useEffect, useState, useCallback, useRef } from 'react';
import { Download, FolderUp, Layers, Plus, RefreshCw, Sparkles, Upload } from 'lucide-react';
import { apiFetch, AuthUser, hasPerm } from '../api';
import { DeckSixDimRadarSection } from '../portal/DeckSixDimRadarSection';
import { CodeMirrorEditor } from '../components/CodeMirrorEditor';

type StudioSubTab = 'card' | 'script' | 'image' | 'proposals';

const CARD_TYPE_FLAGS: { label: string; bit: number }[] = [
  { label: '怪兽', bit: 0x1 },
  { label: '魔法', bit: 0x2 },
  { label: '陷阱', bit: 0x4 },
  { label: '通常', bit: 0x10 },
  { label: '效果', bit: 0x20 },
  { label: '融合', bit: 0x40 },
  { label: '仪式', bit: 0x80 },
  { label: '调整', bit: 0x1000 },
  { label: '同调', bit: 0x2000 },
  { label: '速攻', bit: 0x10000 },
  { label: '永续', bit: 0x20000 },
  { label: '装备', bit: 0x40000 },
  { label: '场地', bit: 0x80000 },
  { label: '反击', bit: 0x100000 },
  { label: '超量', bit: 0x800000 },
  { label: '灵摆', bit: 0x1000000 },
  { label: '连接', bit: 0x4000000 }
];

export function StudioView({ user, notify }: { user: AuthUser; notify: (t: string, k?: 'ok' | 'err') => void }) {
  const [packages, setPackages] = useState<any[]>([]);
  const [trash, setTrash] = useState<string[]>([]);
  const [conflicts, setConflicts] = useState<any>(null);
  const [selectedPkgId, setSelectedPkgId] = useState('');
  const [pkgCards, setPkgCards] = useState<any[]>([]);
  const [cardFilter, setCardFilter] = useState('');
  const [stringsConfText, setStringsConfText] = useState('');
  const editorRef = useRef<HTMLDivElement | null>(null);

  const [subTab, setSubTab] = useState<StudioSubTab>('card');
  const [editingCard, setEditingCard] = useState<any>({
    id: 99000001,
    ot: 4,
    alias: 0,
    setcodeHex: '0x0',
    type: 0x2,
    level: 0,
    lScale: 0,
    rScale: 0,
    race: 0,
    attribute: 0,
    attack: 0,
    defense: 0,
    linkMarker: 0,
    name: '星辉试作魔法',
    desc: '①：自己回复1000基本分。'
  });
  const [luaScript, setLuaScript] = useState(
    `-- DIY 卡脚本示例\nlocal s,id=GetID()\nfunction s.initial_effect(c)\n\tlocal e1=Effect.CreateEffect(c)\n\te1:SetType(EFFECT_TYPE_ACTIVATE)\n\te1:SetCode(EVENT_FREE_CHAIN)\n\te1:SetTarget(s.rectg)\n\te1:SetOperation(s.recop)\n\tc:RegisterEffect(e1)\nend\nfunction s.rectg(e,tp,eg,ep,ev,re,r,rp,chk)\n\tif chk==0 then return true end\n\tDuel.SetOperationInfo(0,CATEGORY_RECOVER,nil,0,tp,1000)\nend\nfunction s.recop(e,tp,eg,ep,ev,re,r,rp)\n\tDuel.Recover(tp,1000,REASON_EFFECT)\nend\n`
  );
  const [hasCardImage, setHasCardImage] = useState(false);
  const [imgCacheBust, setImgCacheBust] = useState(0);
  const [sandboxResult, setSandboxResult] = useState<any>(null);
  const [sandboxRunning, setSandboxRunning] = useState(false);

  const [proposals, setProposals] = useState<any[]>([]);
  const [reviewComment, setReviewComment] = useState('');

  const [poolDecks, setPoolDecks] = useState<any[]>([]);
  const [chaosPreview, setChaosPreview] = useState<any>(null);
  const [cardQuery, setCardQuery] = useState('');
  const [cards, setCards] = useState<any[]>([]);
  const [newPkgId, setNewPkgId] = useState('');
  const [newPkgName, setNewPkgName] = useState('');
  const [showYpkImporter, setShowYpkImporter] = useState(false);
  const [ypkTargetId, setYpkTargetId] = useState('');
  const [ypkTargetName, setYpkTargetName] = useState('');
  const [ypkMergeMode, setYpkMergeMode] = useState(true);
  const [ypkAutoReload, setYpkAutoReload] = useState(true);
  const [ypkImporting, setYpkImporting] = useState(false);

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
      notify(err instanceof Error ? err.message : '加载资源失败', 'err');
    }
  }, [user, notify, selectedPkgId, loadPackageCards]);

  useEffect(() => {
    loadAll();
  }, [loadAll]);

  const scrollEditorOnMobile = () => {
    if (typeof window !== 'undefined' && window.innerWidth < 1024) {
      setTimeout(() => editorRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' }), 60);
    }
  };

  const selectCardForEdit = async (pkgId: string, cardId: number) => {
    try {
      const res = await apiFetch(`/api/v1/diy/packages/${encodeURIComponent(pkgId)}/cards/${cardId}`);
      const c = res.detail.card;
      setEditingCard({
        ...c,
        setcodeHex: '0x' + Number(c.setcode || 0).toString(16)
      });
      setLuaScript(res.detail.scriptContent || `-- c${cardId}.lua\nlocal s,id=GetID()\nfunction s.initial_effect(c)\n\nend\n`);
      setHasCardImage(Boolean(res.detail.hasImage));
      setSandboxResult(null);
      scrollEditorOnMobile();
    } catch (err) {
      notify(err instanceof Error ? err.message : '加载卡片详情失败', 'err');
    }
  };

  const startNewCardDraft = () => {
    const nextId = pkgCards.length > 0 ? Math.max(...pkgCards.map((x: any) => x.card.id)) + 1 : 99000001;
    setEditingCard({
      id: nextId,
      ot: 4,
      alias: 0,
      setcodeHex: '0x0',
      type: 0x21,
      level: 4,
      lScale: 0,
      rScale: 0,
      race: 1,
      attribute: 1,
      attack: 1800,
      defense: 1200,
      linkMarker: 0,
      name: `DIY 新卡 #${nextId}`,
      desc: '①：1回合1次，自己的主要阶段才能发动。自己回复1000基本分。'
    });
    setLuaScript(
      `-- c${nextId}.lua\nlocal s,id=GetID()\nfunction s.initial_effect(c)\n\tlocal e1=Effect.CreateEffect(c)\n\te1:SetType(EFFECT_TYPE_IGNITION)\n\te1:SetRange(LOCATION_MZONE)\n\te1:SetCountLimit(1)\n\te1:SetTarget(s.rectg)\n\te1:SetOperation(s.recop)\n\tc:RegisterEffect(e1)\nend\nfunction s.rectg(e,tp,eg,ep,ev,re,r,rp,chk)\n\tif chk==0 then return true end\n\tDuel.SetOperationInfo(0,CATEGORY_RECOVER,nil,0,tp,1000)\nend\nfunction s.recop(e,tp,eg,ep,ev,re,r,rp)\n\tDuel.Recover(tp,1000,REASON_EFFECT)\nend\n`
    );
    setHasCardImage(false);
    setSandboxResult(null);
    setSubTab('card');
    scrollEditorOnMobile();
  };

  const saveCardDirect = async (autoReload: boolean) => {
    if (!selectedPkgId) {
      notify('请先选择或新建一个受管 DIY 包', 'err');
      return;
    }
    try {
      const res = await apiFetch(`/api/v1/diy/packages/${encodeURIComponent(selectedPkgId)}/cards/${Number(editingCard.id)}`, {
        method: 'PUT',
        body: JSON.stringify({
          card: {
            ...editingCard,
            id: Number(editingCard.id)
          },
          luaScript,
          autoReload
        })
      });
      notify(
        res.reload
          ? `已保存 c${editingCard.id} 并热重载至 Catalog v${res.reload.catalogVersion}`
          : `已通过 Working Copy 原子保存 c${editingCard.id}`
      );
      await loadAll();
    } catch (err) {
      notify(err instanceof Error ? err.message : '保存失败', 'err');
    }
  };

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
      notify('已提交至待审提案队列 (Proposals)');
      setSubTab('proposals');
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
        notify(`沙盒预检通过 (${res.result.elapsedMs} ms)`);
      } else {
        notify('沙盒检测到脚本错误或安全违规，请查看诊断详情', 'err');
      }
    } catch (err) {
      notify(err instanceof Error ? err.message : '沙盒运行失败', 'err');
    } finally {
      setSandboxRunning(false);
    }
  };

  const uploadCardArtwork = async (files: FileList | null) => {
    if (!files?.length || !selectedPkgId || !editingCard.id) return;
    const fd = new FormData();
    fd.append('image', files[0]);
    try {
      await apiFetch(`/api/v1/diy/packages/${encodeURIComponent(selectedPkgId)}/cards/${Number(editingCard.id)}/image`, {
        method: 'POST',
        body: fd
      });
      setHasCardImage(true);
      setImgCacheBust((n) => n + 1);
      notify('卡图上传成功');
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
      notify('strings.conf 已原子保存');
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
      notify(res.message || (approve ? '提案已批准并热重载' : '提案已驳回'));
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
        body: JSON.stringify({ id: newPkgId, name: newPkgName || newPkgId })
      });
      setSelectedPkgId(newPkgId);
      setNewPkgId('');
      setNewPkgName('');
      notify('DIY 包已创建');
      loadAll();
    } catch (err) {
      notify(err instanceof Error ? err.message : '创建失败', 'err');
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
        notify(`成功上传 ${res.savedFiles.length} 个文件至 ${selectedPkgId}`);
      }
      loadAll();
    } catch (err) {
      notify(err instanceof Error ? err.message : '上传失败', 'err');
    }
  };

  const importYpkArchive = async (files: FileList | null) => {
    if (!files || files.length === 0) return;
    setYpkImporting(true);
    try {
      const fd = new FormData();
      fd.append('file', files[0]);
      if (ypkTargetId.trim()) fd.append('packageId', ypkTargetId.trim());
      if (ypkTargetName.trim()) fd.append('name', ypkTargetName.trim());
      fd.append('merge', String(ypkMergeMode));
      fd.append('autoReload', String(ypkAutoReload));

      const res = await apiFetch('/api/v1/diy/packages/import-ypk', {
        method: 'POST',
        body: fd
      });
      const r = res.importResult;
      setSelectedPkgId(res.packageId);
      setYpkTargetId('');
      setYpkTargetName('');
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

  const toggleTypeBit = (bit: number) => {
    setEditingCard((prev: any) => ({
      ...prev,
      type: (prev.type & bit) !== 0 ? prev.type & ~bit : prev.type | bit
    }));
  };

  const filteredPkgCards = pkgCards.filter((x: any) => {
    if (!cardFilter.trim()) return true;
    const kw = cardFilter.trim().toLowerCase();
    return String(x.card.id).includes(kw) || (x.card.name || '').toLowerCase().includes(kw);
  });

  const pendingProposalCount = proposals.filter((p) => p.status === 'pending').length;

  return (
    <div className="space-y-5 animate-fade-in">
      {/* 顶部包选择、批量导入、导出 .ypk 与免重启热重载工具栏 */}
      <div className="glass-card rounded-2xl p-4 space-y-3">
        <div className="flex flex-col lg:flex-row lg:items-center justify-between gap-3">
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-sm font-bold flex items-center gap-1.5">
              <Layers className="w-4 h-4 text-indigo-400" /> 当前工作包:
            </span>
            <select
              value={selectedPkgId}
              onChange={(e) => {
                setSelectedPkgId(e.target.value);
                loadPackageCards(e.target.value);
              }}
              className="w-full sm:w-auto px-3 py-2 text-xs font-mono input-modern input-modern-indigo"
            >
              {packages.length === 0 && <option value="">-- 请先新建受管 DIY 包 --</option>}
              {packages.map((p) => (
                <option key={p.manifest.id} value={p.manifest.id}>
                  {p.manifest.name} ({p.manifest.id}) · {p.validation.cardCount} 张卡
                </option>
              ))}
            </select>

            {selectedPkgId && (
              <a
                href={`/api/v1/diy/packages/${encodeURIComponent(selectedPkgId)}/export.ypk`}
                download
                className="min-h-[34px] inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl bg-sky-600/20 border border-sky-500/40 text-sky-300 text-xs font-medium hover:bg-sky-600/30"
              >
                <Download className="w-3.5 h-3.5" /> 导出当前包 .ypk
              </a>
            )}

            {packages.some((p) => p.manifest?.enabled) && (
              <a
                href="/api/v1/diy/export-all.ypk"
                download
                className="min-h-[34px] inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl bg-emerald-600/20 border border-emerald-500/40 text-emerald-300 text-xs font-medium hover:bg-emerald-600/30"
                title="将所有已启用 DIY 包按优先级合并打包为一个客户端整合 .ypk"
              >
                <Download className="w-3.5 h-3.5" /> 导出全服合集 .ypk
              </a>
            )}

            {(hasPerm(user, 'diy:upload') || hasPerm(user, 'diy:edit')) && (
              <button
                type="button"
                onClick={() => setShowYpkImporter((v) => !v)}
                className={`min-h-[34px] inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl border text-xs font-semibold cursor-pointer transition ${
                  showYpkImporter
                    ? 'bg-indigo-600 border-indigo-500 text-white'
                    : 'bg-purple-600/20 border-purple-500/40 text-purple-200 hover:bg-purple-600/30'
                }`}
              >
                <FolderUp className="w-3.5 h-3.5" /> 导入 .ypk 整包
              </button>
            )}

            {selectedPkgId && hasPerm(user, 'diy:upload') && (
              <label className="min-h-[34px] inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl bg-indigo-600/20 border border-indigo-500/40 text-indigo-300 text-xs font-medium cursor-pointer hover:bg-indigo-600/30">
                <Upload className="w-3.5 h-3.5" /> 传文件入当前包 (.cdb/.lua/.ydk/.ypk)
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
                onClick={async () => {
                  await apiFetch(`/api/v1/diy/packages/${encodeURIComponent(selectedPkgId)}`, { method: 'DELETE' });
                  setSelectedPkgId('');
                  setPkgCards([]);
                  notify('包已移入 .trash 回收站');
                  loadAll();
                }}
                className="min-h-[34px] px-3 py-1.5 rounded-xl bg-rose-950/60 border border-rose-500/30 text-rose-300 text-xs cursor-pointer"
              >
                移入回收站
              </button>
            )}
          </div>

          <div className="flex flex-wrap items-center gap-2">
            {hasPerm(user, 'diy:edit') && (
              <form onSubmit={createPackage} className="flex flex-wrap items-center gap-1.5 w-full sm:w-auto">
                <input
                  value={newPkgId}
                  onChange={(e) => setNewPkgId(e.target.value)}
                  placeholder="新包 ID (如 pack-01)"
                  className="flex-1 sm:w-36 px-3 py-1.5 text-xs input-modern input-modern-indigo"
                  required
                />
                <input
                  value={newPkgName}
                  onChange={(e) => setNewPkgName(e.target.value)}
                  placeholder="显示名称"
                  className="flex-1 sm:w-28 px-3 py-1.5 text-xs input-modern input-modern-indigo"
                />
                <button className="min-h-[34px] px-3 py-1.5 rounded-xl bg-slate-800 hover:bg-slate-700 text-xs font-medium cursor-pointer">
                  + 新建包
                </button>
              </form>
            )}

            {hasPerm(user, 'diy:reload') && (
              <button
                onClick={async () => {
                  const r = await apiFetch('/api/v1/diy/reload', { method: 'POST', body: '{}' });
                  notify(`已热重载至 Catalog v${r.reload.catalogVersion}（进行中对局零中断）`);
                  loadAll();
                }}
                className="w-full sm:w-auto min-h-[36px] px-3.5 py-1.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold flex items-center justify-center gap-1.5 cursor-pointer"
              >
                <RefreshCw className="w-3.5 h-3.5" /> 免重启热重载
              </button>
            )}
          </div>
        </div>

        {showYpkImporter && (
          <div className="glass-subcard rounded-2xl p-3.5 border border-purple-500/35 space-y-3 text-xs animate-fade-in">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="font-semibold text-purple-200 flex items-center gap-1.5">
                <FolderUp className="w-4 h-4 text-purple-400" />
                <span>标准客户端 .ypk / .zip 扩展包一键原子解包导入（自动校验 ZIP Slip、Lua 沙箱安全、SQLite .cdb 与卡图头）</span>
              </div>
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
                placeholder="目标包 ID（留空自动按 .ypk 文件名创建）"
                className="px-3 py-2 input-modern input-modern-indigo font-mono"
              />
              <input
                value={ypkTargetName}
                onChange={(e) => setYpkTargetName(e.target.value)}
                placeholder="包显示名称（留空自动采用原包名）"
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
                  导入后立即热重载
                </label>
              </div>
              <label
                className={`min-h-[38px] inline-flex items-center justify-center gap-1.5 px-4 py-2 rounded-xl bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white font-semibold shadow-md cursor-pointer ${
                  ypkImporting ? 'opacity-60 pointer-events-none' : ''
                }`}
              >
                <FolderUp className="w-4 h-4" />
                <span>{ypkImporting ? '正在解包预检...' : '选择 .ypk / .zip 文件并导入'}</span>
                <input
                  type="file"
                  accept=".ypk,.zip"
                  className="hidden"
                  onChange={(e) => {
                    importYpkArchive(e.target.files);
                    e.target.value = '';
                  }}
                />
              </label>
            </div>
          </div>
        )}

        {conflicts?.conflicts?.length > 0 && (
          <div className="px-3 py-2 rounded-xl bg-amber-950/50 border border-amber-500/30 text-xs text-amber-200">
            多库冲突巡检：检测到 {conflicts.conflicts.length} 个覆盖基础卡库或跨包重复定义的卡密（
            {conflicts.conflicts
              .slice(0, 5)
              .map((c: any) => `#${c.cardId} ${c.cardName}`)
              .join('、')}
            ）
          </div>
        )}

        {trash.length > 0 && (
          <div className="flex flex-wrap items-center gap-2 pt-2 border-t border-slate-800/80 text-xs">
            <span className="text-slate-400">.trash 回收站：</span>
            {trash.map((t) => (
              <span key={t} className="inline-flex items-center gap-1.5 glass-subcard px-2.5 py-1 rounded-lg">
                <span className="font-mono text-[11px]">{t}</span>
                <button
                  onClick={async () => {
                    await apiFetch(`/api/v1/diy/trash/${encodeURIComponent(t)}/restore`, { method: 'POST', body: '{}' });
                    notify('已从回收站恢复');
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

      {/* 主工作区 */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-4">
        <div className="lg:col-span-4 glass-card rounded-2xl p-4 space-y-3 flex flex-col">
          <div className="flex items-center justify-between">
            <span className="text-xs font-bold text-slate-200">包内卡片列表 ({filteredPkgCards.length})</span>
            {hasPerm(user, 'diy:edit') && (
              <button
                onClick={startNewCardDraft}
                className="min-h-[32px] px-3 py-1 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold flex items-center gap-1 cursor-pointer"
              >
                <Plus className="w-3.5 h-3.5" /> 新建单卡
              </button>
            )}
          </div>

          <input
            value={cardFilter}
            onChange={(e) => setCardFilter(e.target.value)}
            placeholder="按卡密或卡名筛选..."
            className="w-full px-3 py-1.5 text-xs input-modern input-modern-indigo"
          />

          <div className="space-y-1.5 max-h-60 lg:max-h-96 overflow-y-auto flex-1 pr-0.5">
            {filteredPkgCards.map((item: any) => (
              <div
                key={item.card.id}
                onClick={() => selectCardForEdit(selectedPkgId, item.card.id)}
                className={`p-2.5 rounded-xl border text-xs cursor-pointer transition ${
                  Number(editingCard.id) === item.card.id
                    ? 'bg-indigo-950/60 border-indigo-500/80'
                    : 'glass-subcard hover:border-slate-700'
                }`}
              >
                <div className="flex items-center justify-between gap-2 font-medium">
                  <span className="truncate">{item.card.name || '<未命名>'}</span>
                  <span className="font-mono text-[11px] text-slate-400 shrink-0">#{item.card.id}</span>
                </div>
                <div className="flex items-center gap-1.5 mt-1 text-[10px]">
                  <span className={item.hasScript ? 'text-emerald-400' : 'text-amber-400'}>
                    {item.hasScript ? 'Lua 已就绪' : '无独立脚本'}
                  </span>
                  <span>·</span>
                  <span className={item.hasImage ? 'text-sky-400' : 'text-slate-500'}>
                    {item.hasImage ? '含卡图' : '无卡图'}
                  </span>
                </div>
              </div>
            ))}
            {filteredPkgCards.length === 0 && (
              <div className="text-xs text-slate-500 py-8 text-center">
                当前包暂无卡片，可点击右上角「新建单卡」或批量上传 .cdb
              </div>
            )}
          </div>

          {selectedPkgId && (
            <div className="pt-3 border-t border-slate-800/80 space-y-2">
              <div className="flex items-center justify-between text-xs">
                <span className="text-slate-400 font-medium">strings.conf (!setname / !counter)</span>
                {hasPerm(user, 'diy:edit') && (
                  <button onClick={saveStringsConf} className="text-indigo-400 hover:underline cursor-pointer">
                    保存字段配置
                  </button>
                )}
              </div>
              <CodeMirrorEditor
                value={stringsConfText}
                onChange={setStringsConfText}
                language="conf"
                accent="indigo"
                placeholder="!setname 0xa100 星辉试作"
                minHeight="84px"
                maxHeight="160px"
                compact
              />
            </div>
          )}
        </div>

        <div ref={editorRef} className="lg:col-span-8 glass-card rounded-2xl p-4 space-y-4">
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2.5 border-b border-slate-800/80 pb-3">
            <div className="flex items-center gap-1.5 overflow-x-auto no-scrollbar pb-0.5">
              {(
                [
                  ['card', '1. Card 属性'],
                  ['script', '2. Lua & 沙盒'],
                  ['image', '3. 卡图预览'],
                  ['proposals', `4. 提案审批 (${pendingProposalCount})`]
                ] as [StudioSubTab, string][]
              ).map(([key, label]) => (
                <button
                  key={key}
                  onClick={() => setSubTab(key)}
                  className={`px-3 py-1.5 rounded-xl text-xs font-semibold shrink-0 cursor-pointer transition ${
                    subTab === key ? 'bg-indigo-600 text-white shadow-sm' : 'glass-subcard text-slate-300 hover:bg-slate-800'
                  }`}
                >
                  {label}
                </button>
              ))}
            </div>

            {subTab !== 'proposals' && hasPerm(user, 'diy:edit') && (
              <div className="flex items-center gap-2 shrink-0">
                <button
                  onClick={submitAsProposal}
                  className="flex-1 sm:flex-none min-h-[34px] px-3 py-1.5 rounded-xl bg-amber-600/20 border border-amber-500/40 text-amber-300 text-xs font-medium cursor-pointer"
                >
                  提交为提案
                </button>
                <button
                  onClick={() => saveCardDirect(true)}
                  className="flex-1 sm:flex-none min-h-[34px] px-3.5 py-1.5 rounded-xl bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-semibold cursor-pointer"
                >
                  原子保存并热重载
                </button>
              </div>
            )}
          </div>

          {subTab === 'card' && (
            <div className="space-y-3.5 text-xs">
              <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-4 gap-3">
                <div>
                  <label className="block text-slate-400 mb-1">卡密 (Code ID)</label>
                  <input
                    type="number"
                    value={editingCard.id}
                    onChange={(e) => setEditingCard({ ...editingCard, id: Number(e.target.value) })}
                    className="w-full px-3 py-2 input-modern input-modern-indigo font-mono"
                  />
                </div>
                <div className="sm:col-span-2">
                  <label className="block text-slate-400 mb-1">卡片名称</label>
                  <input
                    value={editingCard.name}
                    onChange={(e) => setEditingCard({ ...editingCard, name: e.target.value })}
                    className="w-full px-3 py-2 input-modern input-modern-indigo"
                  />
                </div>
                <div>
                  <label className="block text-slate-400 mb-1">OT 归属</label>
                  <select
                    value={editingCard.ot}
                    onChange={(e) => setEditingCard({ ...editingCard, ot: Number(e.target.value) })}
                    className="w-full px-3 py-2 input-modern input-modern-indigo"
                  >
                    <option value={4}>DIY 自定义 (0x4)</option>
                    <option value={7}>OCG+TCG+DIY (0x7)</option>
                    <option value={3}>OCG+TCG (0x3)</option>
                    <option value={1}>OCG (0x1)</option>
                    <option value={2}>TCG (0x2)</option>
                  </select>
                </div>
              </div>

              <div>
                <label className="block text-slate-400 mb-1">
                  卡片种类位掩码 (Type: 0x{Number(editingCard.type || 0).toString(16)})
                </label>
                <div className="flex flex-wrap gap-1.5 glass-subcard p-2.5 rounded-xl">
                  {CARD_TYPE_FLAGS.map((f) => {
                    const checked = (editingCard.type & f.bit) !== 0;
                    return (
                      <button
                        type="button"
                        key={f.bit}
                        onClick={() => toggleTypeBit(f.bit)}
                        className={`px-2.5 py-1 rounded-lg text-[11px] border cursor-pointer ${
                          checked
                            ? 'bg-indigo-600/30 border-indigo-400 text-indigo-200 font-medium'
                            : 'bg-slate-900/80 border-slate-800 text-slate-400'
                        }`}
                      >
                        {f.label}
                      </button>
                    );
                  })}
                </div>
              </div>

              <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-6 gap-2.5">
                <div>
                  <label className="block text-slate-400 mb-1">星级/阶级/Link</label>
                  <input
                    type="number"
                    value={editingCard.level}
                    onChange={(e) => setEditingCard({ ...editingCard, level: Number(e.target.value) })}
                    className="w-full px-2.5 py-1.5 input-modern input-modern-indigo"
                  />
                </div>
                <div>
                  <label className="block text-slate-400 mb-1">ATK 攻击力</label>
                  <input
                    type="number"
                    value={editingCard.attack}
                    onChange={(e) => setEditingCard({ ...editingCard, attack: Number(e.target.value) })}
                    className="w-full px-2.5 py-1.5 input-modern input-modern-indigo"
                  />
                </div>
                <div>
                  <label className="block text-slate-400 mb-1">DEF 守备力</label>
                  <input
                    type="number"
                    value={editingCard.defense}
                    onChange={(e) => setEditingCard({ ...editingCard, defense: Number(e.target.value) })}
                    className="w-full px-2.5 py-1.5 input-modern input-modern-indigo"
                  />
                </div>
                <div>
                  <label className="block text-slate-400 mb-1">左刻度 / 右刻度</label>
                  <div className="flex gap-1">
                    <input
                      type="number"
                      value={editingCard.lScale}
                      onChange={(e) => setEditingCard({ ...editingCard, lScale: Number(e.target.value) })}
                      className="w-1/2 px-2 py-1.5 input-modern input-modern-indigo"
                    />
                    <input
                      type="number"
                      value={editingCard.rScale}
                      onChange={(e) => setEditingCard({ ...editingCard, rScale: Number(e.target.value) })}
                      className="w-1/2 px-2 py-1.5 input-modern input-modern-indigo"
                    />
                  </div>
                </div>
                <div>
                  <label className="block text-slate-400 mb-1">属性 (Attr)</label>
                  <input
                    type="number"
                    value={editingCard.attribute}
                    onChange={(e) => setEditingCard({ ...editingCard, attribute: Number(e.target.value) })}
                    className="w-full px-2.5 py-1.5 input-modern input-modern-indigo"
                  />
                </div>
                <div>
                  <label className="block text-slate-400 mb-1">种族 (Race)</label>
                  <input
                    type="number"
                    value={editingCard.race}
                    onChange={(e) => setEditingCard({ ...editingCard, race: Number(e.target.value) })}
                    className="w-full px-2.5 py-1.5 input-modern input-modern-indigo"
                  />
                </div>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-3 gap-2.5">
                <div>
                  <label className="block text-slate-400 mb-1">字段码 Setcode (十六进制)</label>
                  <input
                    value={editingCard.setcodeHex || '0x0'}
                    onChange={(e) => setEditingCard({ ...editingCard, setcodeHex: e.target.value })}
                    className="w-full px-3 py-1.5 input-modern input-modern-indigo font-mono"
                  />
                </div>
                <div>
                  <label className="block text-slate-400 mb-1">同名异画卡密 (Alias)</label>
                  <input
                    type="number"
                    value={editingCard.alias}
                    onChange={(e) => setEditingCard({ ...editingCard, alias: Number(e.target.value) })}
                    className="w-full px-3 py-1.5 input-modern input-modern-indigo font-mono"
                  />
                </div>
                <div>
                  <label className="block text-slate-400 mb-1">Link 箭头掩码</label>
                  <input
                    type="number"
                    value={editingCard.linkMarker}
                    onChange={(e) => setEditingCard({ ...editingCard, linkMarker: Number(e.target.value) })}
                    className="w-full px-3 py-1.5 input-modern input-modern-indigo font-mono"
                  />
                </div>
              </div>

              <div>
                <label className="block text-slate-400 mb-1">效果文本 / 灵摆文本</label>
                <textarea
                  rows={5}
                  value={editingCard.desc}
                  onChange={(e) => setEditingCard({ ...editingCard, desc: e.target.value })}
                  className="w-full p-3 input-modern input-modern-indigo leading-relaxed"
                />
              </div>

              {selectedPkgId && hasPerm(user, 'diy:edit') && (
                <div className="flex justify-end">
                  <button onClick={deleteCurrentCard} className="text-rose-400 hover:underline cursor-pointer">
                    从当前包永久移除卡片 #{editingCard.id}
                  </button>
                </div>
              )}
            </div>
          )}

          {subTab === 'script' && (
            <div className="space-y-3 text-xs">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="flex flex-wrap items-center gap-1.5">
                  <span className="text-[11px] text-slate-400">快捷骨架：</span>
                  <button
                    type="button"
                    onClick={() =>
                      setLuaScript(
                        `-- c${editingCard.id}.lua (怪兽 1回合1次起动效果)\nlocal s,id=GetID()\nfunction s.initial_effect(c)\n\tlocal e1=Effect.CreateEffect(c)\n\te1:SetDescription(aux.Stringid(id,0))\n\te1:SetCategory(CATEGORY_DRAW)\n\te1:SetType(EFFECT_TYPE_IGNITION)\n\te1:SetRange(LOCATION_MZONE)\n\te1:SetCountLimit(1,id)\n\te1:SetTarget(s.drtg)\n\te1:SetOperation(s.drop)\n\tc:RegisterEffect(e1)\nend\nfunction s.drtg(e,tp,eg,ep,ev,re,r,rp,chk)\n\tif chk==0 then return Duel.IsPlayerCanDraw(tp,1) end\n\tDuel.SetOperationInfo(0,CATEGORY_DRAW,nil,0,tp,1)\nend\nfunction s.drop(e,tp,eg,ep,ev,re,r,rp)\n\tDuel.Draw(tp,1,REASON_EFFECT)\nend\n`
                      )
                    }
                    className="px-2 py-1 rounded-lg glass-subcard hover:border-indigo-500/40 text-[11px] text-slate-300 cursor-pointer"
                  >
                    起动抽卡模板
                  </button>
                  <button
                    type="button"
                    onClick={() =>
                      setLuaScript(
                        `-- c${editingCard.id}.lua (二速诱发即时效果)\nlocal s,id=GetID()\nfunction s.initial_effect(c)\n\tlocal e1=Effect.CreateEffect(c)\n\te1:SetDescription(aux.Stringid(id,0))\n\te1:SetCategory(CATEGORY_NEGATE+CATEGORY_DESTROY)\n\te1:SetType(EFFECT_TYPE_QUICK_O)\n\te1:SetCode(EVENT_CHAINING)\n\te1:SetProperty(EFFECT_FLAG_DAMAGE_STEP+EFFECT_FLAG_DAMAGE_CAL)\n\te1:SetRange(LOCATION_MZONE)\n\te1:SetCountLimit(1,id)\n\te1:SetCondition(s.negcon)\n\te1:SetTarget(s.negtg)\n\te1:SetOperation(s.negop)\n\tc:RegisterEffect(e1)\nend\nfunction s.negcon(e,tp,eg,ep,ev,re,r,rp)\n\treturn ep==1-tp and Duel.IsChainNegatable(ev)\nend\nfunction s.negtg(e,tp,eg,ep,ev,re,r,rp,chk)\n\tif chk==0 then return true end\n\tDuel.SetOperationInfo(0,CATEGORY_NEGATE,eg,1,0,0)\nend\nfunction s.negop(e,tp,eg,ep,ev,re,r,rp)\n\tif Duel.NegateActivation(ev) and re:GetHandler():IsRelateToEffect(re) then\n\t\tDuel.Destroy(eg,REASON_EFFECT)\n\tend\nend\n`
                      )
                    }
                    className="px-2 py-1 rounded-lg glass-subcard hover:border-indigo-500/40 text-[11px] text-slate-300 cursor-pointer"
                  >
                    二速康卡模板
                  </button>
                </div>
                {hasPerm(user, 'diy:sandbox_test') && (
                  <button
                    onClick={runSandbox}
                    disabled={sandboxRunning}
                    className="min-h-[36px] px-3.5 py-1.5 rounded-xl bg-purple-600 hover:bg-purple-500 text-white font-semibold cursor-pointer"
                  >
                    {sandboxRunning ? '正在运行 ocgcore 沙盒...' : '运行 ocgcore 单卡情景沙盒'}
                  </button>
                )}
              </div>

              <CodeMirrorEditor
                value={luaScript}
                onChange={setLuaScript}
                language="lua"
                accent="indigo"
                title={`script/c${editingCard.id}.lua`}
                minHeight="320px"
                maxHeight="520px"
              />

              {sandboxResult && (
                <div
                  className={`p-3.5 rounded-xl border space-y-1.5 ${
                    sandboxResult.success
                      ? 'bg-emerald-950/40 border-emerald-500/40 text-emerald-200'
                      : 'bg-rose-950/50 border-rose-500/40 text-rose-200'
                  }`}
                >
                  <div className="font-semibold flex flex-wrap items-center justify-between gap-2">
                    <span>
                      {sandboxResult.success ? '✓ 沙盒预检通过' : '✕ 沙盒拦截：发现错误'} · 耗时 {sandboxResult.elapsedMs} ms
                    </span>
                    <span className="font-mono text-[11px]">
                      可通召:{sandboxResult.summonableCount} · 可发动:{sandboxResult.activatableCount} · P0 LP:{' '}
                      {sandboxResult.postPlayer0Lp}
                    </span>
                  </div>
                  {sandboxResult.staticAuditViolations?.length > 0 && (
                    <div className="font-mono text-[11px] break-all">
                      静态安全违规：{sandboxResult.staticAuditViolations.join(' | ')}
                    </div>
                  )}
                  {sandboxResult.luaErrors?.length > 0 && (
                    <div className="font-mono text-[11px] break-all">
                      ocgcore Lua 异常：{sandboxResult.luaErrors.join(' | ')}
                    </div>
                  )}
                  {sandboxResult.observedMessages?.length > 0 && (
                    <div className="text-[11px] text-slate-300">
                      引擎消息轨迹：{sandboxResult.observedMessages.join(' → ')}
                    </div>
                  )}
                </div>
              )}
            </div>
          )}

          {subTab === 'image' && (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4 items-center text-xs">
              <div className="glass-subcard rounded-2xl p-4 flex flex-col items-center space-y-3">
                {selectedPkgId && hasCardImage ? (
                  <img
                    src={`/api/v1/diy/packages/${encodeURIComponent(selectedPkgId)}/cards/${Number(editingCard.id)}/image?v=${imgCacheBust}`}
                    alt={editingCard.name}
                    className="w-48 h-64 object-cover rounded-xl border border-slate-700 shadow-lg"
                  />
                ) : (
                  <div className="w-48 h-64 rounded-xl border border-dashed border-slate-700 flex items-center justify-center text-slate-500">
                    暂无卡图 ({editingCard.id})
                  </div>
                )}
                <div className="font-semibold text-sm">{editingCard.name}</div>
              </div>

              <div className="space-y-3">
                <p className="text-slate-400 leading-relaxed">
                  支持上传标准 PNG 或 JPG 卡图（自动校验二进制魔数头并限制 ≤ 8MB），保存至{' '}
                  <span className="font-mono text-slate-200">pics/{editingCard.id}.png/.jpg</span>，导出{' '}
                  <span className="font-mono text-sky-300">.ypk</span> 时自动随包打入供客户端直接显示。
                </p>
                {selectedPkgId && hasPerm(user, 'diy:edit') && (
                  <label className="inline-flex min-h-[40px] items-center px-4 py-2 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white font-semibold cursor-pointer">
                    上传 / 替换卡图 (.png / .jpg)
                    <input
                      type="file"
                      accept="image/png,image/jpeg"
                      className="hidden"
                      onChange={(e) => uploadCardArtwork(e.target.files)}
                    />
                  </label>
                )}
              </div>
            </div>
          )}

          {subTab === 'proposals' && (
            <div className="space-y-3 text-xs">
              <input
                value={reviewComment}
                onChange={(e) => setReviewComment(e.target.value)}
                placeholder="可选审批备注（批准后将自动写入目标 DIY 包并触发热重载）..."
                className="w-full px-3 py-2 input-modern input-modern-indigo"
              />
              <div className="space-y-2.5 max-h-96 overflow-y-auto">
                {proposals.map((p) => (
                  <div key={p.id} className="glass-subcard rounded-xl p-3.5 space-y-2.5">
                    <div className="flex items-center justify-between gap-2">
                      <div className="min-w-0">
                        <span className="font-semibold text-slate-200">{p.title}</span>{' '}
                        <span className="font-mono text-indigo-300">[{p.packageId}]</span>
                      </div>
                      <span
                        className={`px-2 py-0.5 rounded-full text-[11px] shrink-0 ${
                          p.status === 'approved'
                            ? 'bg-emerald-500/20 text-emerald-300'
                            : p.status === 'rejected'
                              ? 'bg-rose-500/20 text-rose-300'
                              : 'bg-amber-500/20 text-amber-300'
                        }`}
                      >
                        {p.status}
                      </span>
                    </div>
                    <pre className="p-2.5 rounded-lg bg-slate-950/90 text-[11px] font-mono text-slate-300 max-h-32 overflow-auto">
                      {p.diffJson}
                    </pre>
                    {p.status === 'pending' && hasPerm(user, 'diy:review_proposal') && (
                      <div className="flex justify-end gap-2">
                        <button
                          onClick={() => reviewProposal(p.id, false)}
                          className="min-h-[34px] px-3 py-1.5 rounded-xl bg-rose-600/80 hover:bg-rose-600 text-white cursor-pointer"
                        >
                          驳回提案
                        </button>
                        <button
                          onClick={() => reviewProposal(p.id, true)}
                          className="min-h-[34px] px-3.5 py-1.5 rounded-xl bg-emerald-600 hover:bg-emerald-500 text-white font-semibold cursor-pointer"
                        >
                          批准并热重载生效
                        </button>
                      </div>
                    )}
                  </div>
                ))}
                {proposals.length === 0 && (
                  <div className="py-8 text-center text-slate-500">当前暂无待审或历史 DIY 提案</div>
                )}
              </div>
            </div>
          )}
        </div>
      </div>

      {/* 卡组静态特征剖析器 & 0~100 六维能力雷达 */}
      <DeckSixDimRadarSection apiPrefix="/api/v1" chaosYdk={chaosPreview?.ydk} notify={notify} />

      {/* 底部：服务端随机盲盒池、混沌试抽预览与版本化卡库搜索 */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <div className="glass-card rounded-2xl p-4 sm:p-5 space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h2 className="text-sm font-bold flex items-center gap-2">
              <Sparkles className="w-4 h-4 text-purple-400" /> 随机盲盒池 ({poolDecks.length} 套) & 混沌组卡试抽
            </h2>
            <button
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

          <div className="max-h-48 overflow-y-auto space-y-1.5 text-xs">
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
            <div className="grid grid-cols-1 gap-2 max-h-52 overflow-y-auto">
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
  );
}
