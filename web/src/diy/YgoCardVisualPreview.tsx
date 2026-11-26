import React, { useState } from 'react';
import { ImagePlus, Maximize2, X } from 'lucide-react';
import {
  CARD_BITS,
  countLinkArrows,
  EditableDiyCard,
  formatCardBracketLine,
  formatStatDisplay,
  resolveCardFrameTheme,
  splitPendulumAndMonsterDesc,
  YGO_ATTRIBUTES,
  YGO_LINK_ARROWS
} from './ygoCardTaxonomy';

export interface YgoCardVisualPreviewProps {
  card: EditableDiyCard;
  imageUrl?: string | null;
  canUploadImage?: boolean;
  onSelectImageFiles?: (files: FileList | null) => void;
}

export function YgoCardVisualPreview({
  card,
  imageUrl,
  canUploadImage = false,
  onSelectImageFiles
}: YgoCardVisualPreviewProps) {
  const [dragOver, setDragOver] = useState(false);
  const [lightboxOpen, setLightboxOpen] = useState(false);
  const [imgBroken, setImgBroken] = useState(false);

  const theme = resolveCardFrameTheme(card.type || 0);
  const attr = YGO_ATTRIBUTES.find((a) => a.value === card.attribute);
  const bracketLine = formatCardBracketLine(card);
  const isNormalMonster = (card.type & CARD_BITS.NORMAL) !== 0 && !theme.isSpell && !theme.isTrap;
  const levelCount = Math.max(0, Math.min(13, Number(card.level) || 0));
  const linkRating = Math.max(1, countLinkArrows(card.linkMarker) || levelCount || 1);
  const { pendulumEffect, monsterEffect } = theme.isPendulum
    ? splitPendulumAndMonsterDesc(card.desc)
    : { pendulumEffect: '', monsterEffect: card.desc || '' };

  const validImage = imageUrl && !imgBroken ? imageUrl : null;
  const paddedCode = String(Math.max(0, Number(card.id) || 0)).padStart(8, '0');

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    setDragOver(false);
    if (canUploadImage && onSelectImageFiles && e.dataTransfer.files?.length) {
      onSelectImageFiles(e.dataTransfer.files);
      setImgBroken(false);
    }
  };

  return (
    <>
      <div className="relative mx-auto w-full max-w-[312px] select-none">
        {/* 卡片外发光底座 */}
        <div className="rounded-[18px] p-[9px] bg-gradient-to-b from-slate-600 via-slate-800 to-slate-950 shadow-[0_24px_50px_-12px_rgba(0,0,0,0.85)] border border-white/15">
          {/* OCG 卡面纹理层 */}
          <div
            className={`rounded-[11px] p-2.5 flex flex-col gap-1.5 border ${theme.cardOuterBg} ${theme.cardInnerBorder} shadow-inner`}
          >
            {/* 1. 卡名栏 + 属性珠 */}
            <div
              className={`h-9 px-2.5 rounded-md border flex items-center justify-between gap-2 shadow-[inset_0_1px_2px_rgba(255,255,255,0.28),0_2px_4px_rgba(0,0,0,0.35)] ${theme.titleBarCls}`}
            >
              <div className="font-bold text-[15px] tracking-wide truncate leading-none">
                {card.name?.trim() || '未命名新卡'}
              </div>
              <div
                className={`w-6 h-6 rounded-full border bg-gradient-to-br flex items-center justify-center text-[11px] font-black shadow shrink-0 ${
                  theme.isSpell
                    ? 'from-teal-300 via-emerald-600 to-teal-950 text-white border-teal-100'
                    : theme.isTrap
                      ? 'from-pink-300 via-rose-600 to-purple-950 text-white border-pink-100'
                      : attr?.orbCls || 'from-slate-500 to-slate-800 text-white border-slate-300'
                }`}
                title={theme.isSpell ? '魔法' : theme.isTrap ? '陷阱' : attr?.label || '未选属性'}
              >
                {theme.isSpell ? '魔' : theme.isTrap ? '陷' : attr?.kanji || '?'}
              </div>
            </div>

            {/* 2. 星级 / 阶级 / 魔陷标题栏 */}
            <div className="min-h-[18px] px-1 flex items-center">
              {theme.isSpell || theme.isTrap ? (
                <div className="ml-auto text-[11px] font-bold tracking-wider text-white/95 drop-shadow">
                  {bracketLine}
                </div>
              ) : theme.isLink ? (
                <div className="ml-auto text-[10px] font-mono font-bold text-sky-200/90 tracking-widest">
                  LINK MONSTER · L-{linkRating}
                </div>
              ) : theme.isXyz ? (
                <div className="flex items-center gap-0.5 mr-auto" title={`阶级 Rank ${levelCount}`}>
                  {Array.from({ length: levelCount }).map((_, i) => (
                    <span
                      key={i}
                      className="w-4 h-4 rounded-full bg-gradient-to-b from-zinc-900 to-black border border-amber-400 text-amber-300 text-[10px] flex items-center justify-center shadow"
                    >
                      ★
                    </span>
                  ))}
                </div>
              ) : (
                <div className="flex items-center gap-0.5 ml-auto" title={`等级 Level ${levelCount}`}>
                  {Array.from({ length: levelCount }).map((_, i) => (
                    <span
                      key={i}
                      className="w-4 h-4 rounded-full bg-gradient-to-b from-amber-300 via-orange-500 to-red-700 border border-yellow-200/80 text-yellow-100 text-[10px] flex items-center justify-center shadow"
                    >
                      ★
                    </span>
                  ))}
                </div>
              )}
            </div>

            {/* 3. 卡图框 + Link 八向箭头 */}
            <div className="relative mx-2 my-0.5">
              {/* Link 箭头环绕装饰 */}
              {theme.isLink &&
                YGO_LINK_ARROWS.map((arrow) => {
                  const active = (card.linkMarker & arrow.bit) !== 0;
                  const posCls =
                    arrow.row === 0 && arrow.col === 0
                      ? '-top-2 -left-2'
                      : arrow.row === 0 && arrow.col === 1
                        ? '-top-2.5 left-1/2 -translate-x-1/2'
                        : arrow.row === 0 && arrow.col === 2
                          ? '-top-2 -right-2'
                          : arrow.row === 1 && arrow.col === 0
                            ? 'top-1/2 -left-2.5 -translate-y-1/2'
                            : arrow.row === 1 && arrow.col === 2
                              ? 'top-1/2 -right-2.5 -translate-y-1/2'
                              : arrow.row === 2 && arrow.col === 0
                                ? '-bottom-2 -left-2'
                                : arrow.row === 2 && arrow.col === 1
                                  ? '-bottom-2.5 left-1/2 -translate-x-1/2'
                                  : '-bottom-2 -right-2';
                  return (
                    <span
                      key={arrow.bit}
                      className={`absolute z-20 w-5 h-5 rounded-sm flex items-center justify-center text-[11px] font-black border transition ${posCls} ${
                        active
                          ? 'bg-gradient-to-b from-red-500 to-rose-700 border-amber-200 text-white shadow-[0_0_10px_rgba(244,63,94,0.95)]'
                          : 'bg-slate-900/85 border-slate-600/70 text-slate-600'
                      }`}
                      title={`${arrow.name}箭头`}
                    >
                      {arrow.symbol}
                    </span>
                  );
                })}

              <label
                onDragOver={(e) => {
                  if (!canUploadImage) return;
                  e.preventDefault();
                  setDragOver(true);
                }}
                onDragLeave={() => setDragOver(false)}
                onDrop={handleDrop}
                onDoubleClick={(e) => {
                  if (validImage) {
                    e.preventDefault();
                    setLightboxOpen(true);
                  }
                }}
                className={`group relative block aspect-square w-full rounded-[4px] border-[3px] ${
                  dragOver
                    ? 'border-emerald-400 ring-2 ring-emerald-400/50'
                    : 'border-slate-300/70 shadow-[0_4px_12px_rgba(0,0,0,0.55)]'
                } bg-slate-950 overflow-hidden ${canUploadImage ? 'cursor-pointer' : ''}`}
              >
                {validImage ? (
                  <>
                    <img
                      src={validImage}
                      alt={card.name || 'Card Artwork'}
                      onError={() => setImgBroken(true)}
                      className="w-full h-full object-cover"
                    />
                    <div className="absolute inset-0 bg-black/55 opacity-0 group-hover:opacity-100 transition flex flex-col items-center justify-center gap-1.5 text-white text-xs">
                      {canUploadImage && (
                        <span className="px-2.5 py-1 rounded-lg bg-indigo-600/90 font-semibold flex items-center gap-1">
                          <ImagePlus className="w-3.5 h-3.5" /> 点击或拖拽更换卡图
                        </span>
                      )}
                      <button
                        type="button"
                        onClick={(e) => {
                          e.preventDefault();
                          e.stopPropagation();
                          setLightboxOpen(true);
                        }}
                        className="px-2 py-0.5 rounded bg-slate-900/85 text-[11px] text-slate-200 flex items-center gap-1 cursor-pointer"
                      >
                        <Maximize2 className="w-3 h-3" /> 放大预览
                      </button>
                    </div>
                  </>
                ) : (
                  <div className="w-full h-full bg-[radial-gradient(circle_at_50%_35%,rgba(99,102,241,0.28),rgba(2,6,23,0.96)_72%)] flex flex-col items-center justify-center p-4 text-center">
                    <ImagePlus className="w-8 h-8 text-slate-500 group-hover:text-indigo-300 transition mb-1.5" />
                    <div className="text-[11px] font-semibold text-slate-300">
                      {canUploadImage ? '点击或拖拽上传 PNG / JPG 卡图' : '暂无卡图预览'}
                    </div>
                    <div className="text-[10px] text-slate-500 font-mono mt-0.5">pics/{card.id || 0}.png</div>
                  </div>
                )}

                {canUploadImage && onSelectImageFiles && (
                  <input
                    type="file"
                    accept="image/png,image/jpeg"
                    className="hidden"
                    onChange={(e) => {
                      setImgBroken(false);
                      onSelectImageFiles(e.target.files);
                      e.target.value = '';
                    }}
                  />
                )}
              </label>
            </div>

            {/* 4. 灵摆刻度与灵摆效果栏（仅灵摆怪兽显示） */}
            {theme.isPendulum && (
              <div className="grid grid-cols-12 rounded-[4px] border border-teal-700/80 bg-[#e2f2ee]/95 text-slate-900 overflow-hidden shadow-sm">
                <div className="col-span-2 bg-sky-900/15 border-r border-teal-700/40 flex flex-col items-center justify-center py-1">
                  <span className="text-[10px] text-sky-700 leading-none">◀◆</span>
                  <span className="font-mono font-black text-xs text-slate-900">{card.lScale ?? 0}</span>
                </div>
                <div className="col-span-8 px-2 py-1 text-[9.5px] leading-snug max-h-14 overflow-y-auto whitespace-pre-wrap">
                  {pendulumEffect || <span className="text-slate-400 italic">（灵摆效果空栏）</span>}
                </div>
                <div className="col-span-2 bg-rose-900/15 border-l border-teal-700/40 flex flex-col items-center justify-center py-1">
                  <span className="text-[10px] text-rose-700 leading-none">◆▶</span>
                  <span className="font-mono font-black text-xs text-slate-900">{card.rScale ?? 0}</span>
                </div>
              </div>
            )}

            {/* 5. 怪兽种族类别 + 效果/描述文本框 + ATK/DEF 底栏 */}
            <div className={`rounded-[4px] border-[2px] px-2 py-1.5 flex flex-col justify-between min-h-[108px] shadow-inner ${theme.textBoxBg}`}>
              <div className="space-y-0.5">
                {!theme.isSpell && !theme.isTrap && (
                  <div className="font-extrabold text-[10.5px] tracking-tight text-slate-900 leading-tight">
                    {bracketLine}
                  </div>
                )}
                <div
                  className={`text-[10px] leading-[1.38] max-h-24 overflow-y-auto whitespace-pre-wrap break-words ${
                    isNormalMonster ? 'italic font-serif text-slate-800' : 'text-slate-900'
                  }`}
                >
                  {(theme.isPendulum ? monsterEffect : card.desc) || '在右侧填写卡片效果文本...'}
                </div>
              </div>

              {!theme.isSpell && !theme.isTrap && (
                <div className="mt-1.5 pt-1 border-t border-slate-900/25 flex items-center justify-end gap-3 font-mono text-[11px] font-black tracking-tight text-slate-950">
                  <span>ATK/{formatStatDisplay(card.attack)}</span>
                  {theme.isLink ? (
                    <span className="tracking-wider">LINK-{linkRating}</span>
                  ) : (
                    <span>DEF/{formatStatDisplay(card.defense)}</span>
                  )}
                </div>
              )}
            </div>

            {/* 6. 卡密角标与防伪印记 */}
            <div
              className={`px-1 flex items-center justify-between text-[9px] font-mono ${
                theme.isDarkTitle ? 'text-slate-800' : 'text-white/80'
              }`}
            >
              <span>{paddedCode}</span>
              <span className="flex items-center gap-1">
                <span>{(card.ot & 0x4) !== 0 ? 'DIY CUSTOM' : 'OCG/TCG'}</span>
                <span className="inline-block w-2.5 h-2.5 rounded-[2px] bg-gradient-to-br from-amber-200 via-yellow-400 to-amber-600 border border-amber-100 shadow-xs" />
              </span>
            </div>
          </div>
        </div>
      </div>

      {/* 双击全屏卡图查看器 */}
      {lightboxOpen && validImage && (
        <div
          className="fixed inset-0 z-50 bg-black/85 backdrop-blur-sm flex items-center justify-center p-4"
          onClick={() => setLightboxOpen(false)}
        >
          <div className="relative max-w-lg w-full" onClick={(e) => e.stopPropagation()}>
            <button
              type="button"
              onClick={() => setLightboxOpen(false)}
              className="absolute -top-10 right-0 p-2 rounded-full bg-slate-800 text-white hover:bg-slate-700 cursor-pointer"
            >
              <X className="w-4 h-4" />
            </button>
            <img src={validImage} alt={card.name} className="w-full rounded-2xl border border-slate-700 shadow-2xl" />
          </div>
        </div>
      )}
    </>
  );
}
