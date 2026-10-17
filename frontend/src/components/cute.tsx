"use client";

import { useId } from "react";
import { motion, useReducedMotion } from "motion/react";
import { Box } from "@mui/material";
import { brand } from "@/theme";
import type { EmployeeKind } from "@/lib/api";

/**
 * Optional licensed artwork: set NEXT_PUBLIC_MASCOT_IMAGE (e.g. "/characters/mascot.png", file placed in /public)
 * to replace the built-in mascot. Only use images you have the rights to.
 */
const MASCOT_IMAGE = process.env.NEXT_PUBLIC_MASCOT_IMAGE;

/** "Puni" — the office's original mascot: a little lavender imp with star horns. */
export function Mascot({ size = 120, float = true }: { size?: number; float?: boolean }) {
  const reduce = useReducedMotion();
  const id = useId();
  const animate = float && !reduce ? { y: [0, -8, 0], rotate: [0, -2, 0, 2, 0] } : undefined;

  return (
    <motion.div
      style={{ width: size, height: size, display: "inline-block" }}
      animate={animate}
      transition={{ duration: 4, repeat: Infinity, ease: "easeInOut" }}
    >
      {MASCOT_IMAGE ? (
        // eslint-disable-next-line @next/next/no-img-element
        <img src={MASCOT_IMAGE} alt="mascot" width={size} height={size} style={{ objectFit: "contain" }} />
      ) : (
        <svg viewBox="0 0 120 120" width={size} height={size} role="img" aria-label="Puni mascot">
          <defs>
            <linearGradient id={`${id}-body`} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0" stopColor="#B99CF2" />
              <stop offset="1" stopColor={brand.purple} />
            </linearGradient>
          </defs>
          {/* wings */}
          <motion.path
            d="M22 62 C6 52 6 78 18 80 C14 72 20 68 26 72 Z"
            fill={brand.purpleDeep}
            style={{ originX: "26px", originY: "70px" }}
            animate={reduce ? undefined : { rotate: [0, -12, 0] }}
            transition={{ duration: 1.6, repeat: Infinity, ease: "easeInOut" }}
          />
          <motion.path
            d="M98 62 C114 52 114 78 102 80 C106 72 100 68 94 72 Z"
            fill={brand.purpleDeep}
            style={{ originX: "94px", originY: "70px" }}
            animate={reduce ? undefined : { rotate: [0, 12, 0] }}
            transition={{ duration: 1.6, repeat: Infinity, ease: "easeInOut" }}
          />
          {/* star horns */}
          <path d="M40 30 l4 -12 l4 12 l-8 0 Z" fill={brand.pink} />
          <path d="M72 30 l4 -12 l4 12 l-8 0 Z" fill={brand.pink} />
          {/* body */}
          <path d="M60 24 C88 24 98 46 98 68 C98 92 84 104 60 104 C36 104 22 92 22 68 C22 46 32 24 60 24 Z" fill={`url(#${id}-body)`} />
          {/* belly */}
          <ellipse cx="60" cy="82" rx="22" ry="16" fill="#fff" opacity="0.9" />
          {/* eyes (blink) */}
          <motion.g
            style={{ originY: "60px" }}
            animate={reduce ? undefined : { scaleY: [1, 1, 0.1, 1, 1] }}
            transition={{ duration: 4, repeat: Infinity, times: [0, 0.9, 0.93, 0.96, 1] }}
          >
            <ellipse cx="47" cy="58" rx="6" ry="7.5" fill={brand.ink} />
            <ellipse cx="73" cy="58" rx="6" ry="7.5" fill={brand.ink} />
            <circle cx="49" cy="55" r="2.2" fill="#fff" />
            <circle cx="75" cy="55" r="2.2" fill="#fff" />
          </motion.g>
          {/* cheeks + smile */}
          <ellipse cx="38" cy="68" rx="5" ry="3" fill={brand.pink} opacity="0.7" />
          <ellipse cx="82" cy="68" rx="5" ry="3" fill={brand.pink} opacity="0.7" />
          <path d="M55 68 Q60 73 65 68" stroke={brand.ink} strokeWidth="2.4" fill="none" strokeLinecap="round" />
          {/* tiny heart badge */}
          <path d="M60 86 c-4 -5 -10 -1 -6 4 l6 5 l6 -5 c4 -5 -2 -9 -6 -4 Z" fill={brand.pink} />
        </svg>
      )}
    </motion.div>
  );
}

/** Stable hue per name so each AI employee keeps its own colour. */
function hueOf(seed: string) {
  let h = 0;
  for (const ch of seed) h = (h * 31 + ch.charCodeAt(0)) % 360;
  // keep within purple–pink–blue range to stay on theme
  return 230 + (h % 100);
}

/** A round cartoon avatar: a little robot for AI employees, a smiling face for humans. */
export function Avatar({ kind, seed, size = 36 }: { kind: EmployeeKind; seed: string; size?: number }) {
  const hue = hueOf(seed);
  const bg = `hsl(${hue} 70% 88%)`;
  const main = `hsl(${hue} 55% 55%)`;
  return (
    <Box component="span" sx={{ display: "inline-flex", width: size, height: size, flexShrink: 0 }}>
      <svg viewBox="0 0 40 40" width={size} height={size} role="img" aria-label={kind === "AI" ? "AI" : "Human"}>
        <circle cx="20" cy="20" r="20" fill={bg} />
        {kind === "AI" ? (
          <>
            <line x1="20" y1="6" x2="20" y2="11" stroke={main} strokeWidth="2" strokeLinecap="round" />
            <circle cx="20" cy="5.5" r="2.2" fill={brand.pink} />
            <rect x="9" y="11" width="22" height="18" rx="7" fill={main} />
            <rect x="12" y="14.5" width="16" height="10" rx="5" fill="#fff" />
            <circle cx="16.5" cy="19.5" r="2" fill={brand.ink} />
            <circle cx="23.5" cy="19.5" r="2" fill={brand.ink} />
            <circle cx="12.5" cy="24" r="1.5" fill={brand.pink} opacity=".7" />
            <circle cx="27.5" cy="24" r="1.5" fill={brand.pink} opacity=".7" />
            <rect x="15" y="30" width="10" height="5" rx="2.5" fill={main} />
          </>
        ) : (
          <>
            <circle cx="20" cy="17" r="8.5" fill="#FFE3D3" />
            <path d="M11.5 15 C12 7 28 7 28.5 15 C25 12 15 12 11.5 15 Z" fill={brand.ink} />
            <circle cx="17" cy="17.5" r="1.3" fill={brand.ink} />
            <circle cx="23" cy="17.5" r="1.3" fill={brand.ink} />
            <path d="M17.5 21 Q20 23 22.5 21" stroke={brand.ink} strokeWidth="1.3" fill="none" strokeLinecap="round" />
            <path d="M7 37 C8 27 32 27 33 37 Z" fill={main} />
          </>
        )}
      </svg>
    </Box>
  );
}

/** Floating stars, hearts and sparkles behind a page. Purely decorative. */
export function Sparkles({ count = 14 }: { count?: number }) {
  const reduce = useReducedMotion();
  const items = Array.from({ length: count }, (_, i) => ({
    left: (i * 37) % 100,
    top: (i * 53) % 100,
    size: 8 + ((i * 7) % 12),
    delay: (i % 7) * 0.6,
    shape: i % 3,
  }));
  return (
    <Box aria-hidden sx={{ position: "absolute", inset: 0, overflow: "hidden", pointerEvents: "none" }}>
      {items.map((it, i) => (
        <motion.svg
          key={i}
          viewBox="0 0 20 20"
          width={it.size}
          height={it.size}
          style={{ position: "absolute", left: `${it.left}%`, top: `${it.top}%` }}
          initial={{ opacity: 0 }}
          animate={reduce ? { opacity: 0.5 } : { opacity: [0, 0.9, 0], y: [0, -16, -28], rotate: [0, 20, 40] }}
          transition={{ duration: 5, delay: it.delay, repeat: Infinity, ease: "easeInOut" }}
        >
          {it.shape === 0 && <path d="M10 0 L12.5 7.5 L20 10 L12.5 12.5 L10 20 L7.5 12.5 L0 10 L7.5 7.5 Z" fill={brand.lavender} />}
          {it.shape === 1 && <path d="M10 18 C-4 8 4 -2 10 5 C16 -2 24 8 10 18 Z" fill={brand.pinkSoft} />}
          {it.shape === 2 && <circle cx="10" cy="10" r="5" fill={brand.lavender} opacity=".7" />}
        </motion.svg>
      ))}
    </Box>
  );
}

/** A tiny cartoon office: desks with AI coworkers typing away. Used as the dashboard hero. */
export function OfficeScene({ working = 0, total = 0 }: { working?: number; total?: number }) {
  const reduce = useReducedMotion();
  const desks = [60, 170, 280, 390];
  return (
    <svg viewBox="0 0 460 150" width="100%" style={{ maxHeight: 170, display: "block" }} role="img" aria-label={`${working} of ${total} AI employees working`}>
      {/* window with moon */}
      <rect x="330" y="10" width="110" height="60" rx="14" fill={brand.lilac} />
      <circle cx="410" cy="34" r="12" fill="#FFF3B8" />
      <circle cx="404" cy="30" r="10" fill={brand.lilac} />
      <path d="M350 52 l3 -6 l3 6 l-6 0 Z M370 30 l2 -4 l2 4 Z" fill={brand.lavender} />
      {/* floor */}
      <rect x="0" y="128" width="460" height="22" rx="11" fill={brand.lilac} />
      {desks.map((x, i) => (
        <g key={x}>
          {/* coworker */}
          <motion.g
            animate={reduce ? undefined : { y: [0, -3, 0] }}
            transition={{ duration: 1.2 + i * 0.15, repeat: Infinity, ease: "easeInOut", delay: i * 0.2 }}
          >
            <line x1={x} y1="58" x2={x} y2="66" stroke={brand.purple} strokeWidth="2.5" strokeLinecap="round" />
            <circle cx={x} cy="56" r="3.5" fill={brand.pink} />
            <rect x={x - 18} y="66" width="36" height="28" rx="11" fill={i % 2 ? brand.purple : "#9C7BE0"} />
            <rect x={x - 13} y="71" width="26" height="15" rx="7" fill="#fff" />
            <circle cx={x - 5} cy="78.5" r="2.3" fill={brand.ink} />
            <circle cx={x + 5} cy="78.5" r="2.3" fill={brand.ink} />
          </motion.g>
          {/* desk + laptop */}
          <rect x={x - 34} y="98" width="68" height="8" rx="4" fill={brand.purpleDeep} />
          <rect x={x - 28} y="106" width="6" height="22" rx="3" fill={brand.purpleDeep} />
          <rect x={x + 22} y="106" width="6" height="22" rx="3" fill={brand.purpleDeep} />
          <motion.rect
            x={x + 6} y="84" width="22" height="14" rx="3"
            fill={brand.pinkSoft}
            animate={reduce ? undefined : { opacity: [0.6, 1, 0.6] }}
            transition={{ duration: 1.8, repeat: Infinity, delay: i * 0.3 }}
          />
          {/* typing sparkle */}
          {!reduce && (
            <motion.path
              d={`M${x + 34} 76 l2 -5 l2 5 l5 2 l-5 2 l-2 5 l-2 -5 l-5 -2 Z`}
              fill={brand.pink}
              animate={{ opacity: [0, 1, 0], scale: [0.6, 1, 0.6] }}
              transition={{ duration: 1.4, repeat: Infinity, delay: i * 0.35 }}
            />
          )}
        </g>
      ))}
      {/* plant */}
      <rect x="440" y="110" width="14" height="18" rx="4" fill={brand.pink} />
      <path d="M447 110 C440 98 444 92 447 100 C450 92 456 98 447 110 Z" fill="#8FD6B4" />
    </svg>
  );
}

/** Fade-and-rise for lists: wrap each item, pass its index. */
export function Rise({ children, index = 0, style }: { children: React.ReactNode; index?: number; style?: React.CSSProperties }) {
  const reduce = useReducedMotion();
  return (
    <motion.div
      style={{ height: "100%", ...style }}
      initial={reduce ? false : { opacity: 0, y: 14, scale: 0.98 }}
      animate={{ opacity: 1, y: 0, scale: 1 }}
      transition={{ duration: 0.4, delay: Math.min(index, 12) * 0.04, ease: [0.2, 0.8, 0.2, 1] }}
    >
      {children}
    </motion.div>
  );
}
