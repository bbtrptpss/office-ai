"use client";

import { useEffect, useState } from "react";
import { motion, useReducedMotion } from "motion/react";
import { Box, Card, CardContent, Chip, ChipProps, Stack, Tab, Tabs, Typography } from "@mui/material";
import { CHANGELOG, ChangeType, LATEST_VERSION, SEEN_KEY, TODO, TodoStatus } from "@/lib/changelog";
import { useI18n } from "@/lib/i18n";
import { emitLocal } from "@/lib/realtime";
import { PageHeader } from "@/components/common";

const TYPE_COLOR: Record<ChangeType, ChipProps["color"]> = { added: "success", changed: "info", fixed: "warning", security: "secondary" };
const TYPE_ORDER: ChangeType[] = ["added", "changed", "fixed", "security"];

export default function UpdatesPage() {
  const { t, lang } = useI18n();
  const reduce = useReducedMotion();
  const [tab, setTab] = useState<"log" | "todo">("log");
  const typeLabel: Record<ChangeType, string> = { added: t.changeAdded, changed: t.changeChanged, fixed: t.changeFixed, security: t.changeSecurity };
  const statusLabel: Record<TodoStatus, string> = { planned: t.todoPlanned, waitingCeo: t.todoWaitingCeo };
  const fmt = new Intl.DateTimeFormat(lang === "th" ? "th-TH" : "en-GB", { dateStyle: "long" });

  // Opening the page marks the latest release as seen (per browser).
  useEffect(() => {
    try { localStorage.setItem(SEEN_KEY, LATEST_VERSION); } catch { /* storage unavailable */ }
    emitLocal("updatesSeen");
  }, []);

  const waiting = TODO.filter((x) => x.status === "waitingCeo");
  const planned = TODO.filter((x) => x.status === "planned");

  return (
    <>
      <PageHeader title={t.navUpdates} subtitle={`${t.currentVersion} ${LATEST_VERSION}`} />
      <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ mb: 2 }}>
        <Tab value="log" label={t.updateLog} />
        <Tab value="todo" label={`${t.remainingWork} (${TODO.length})`} />
      </Tabs>

      {tab === "log" ? (
        <Stack spacing={2}>
          {CHANGELOG.map((r, i) => (
            <motion.div key={r.version} initial={reduce ? false : { opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: Math.min(i, 8) * 0.04 }}>
              <Card>
                <CardContent>
                  <Stack direction="row" spacing={1} sx={{ alignItems: "center", flexWrap: "wrap", rowGap: 1, mb: 1.5 }}>
                    <Chip size="small" color="primary" label={`v${r.version}`} sx={{ fontWeight: 700 }} />
                    <Typography fontWeight={700} sx={{ flex: 1, minWidth: 180 }}>{r.title[lang]}</Typography>
                    {i === 0 && <Chip size="small" color="secondary" label={t.latest} />}
                    <Typography variant="caption" color="text.secondary">{fmt.format(new Date(`${r.date}T00:00:00`))}</Typography>
                  </Stack>
                  <Stack spacing={0.75}>
                    {[...r.changes].sort((a, b) => TYPE_ORDER.indexOf(a.type) - TYPE_ORDER.indexOf(b.type)).map((c, j) => (
                      <Stack key={j} direction="row" spacing={1.5} sx={{ alignItems: "flex-start" }}>
                        <Chip size="small" variant="outlined" color={TYPE_COLOR[c.type]} label={typeLabel[c.type]} sx={{ minWidth: 76, flexShrink: 0 }} />
                        <Typography variant="body2" sx={{ pt: 0.25 }}>{c[lang]}</Typography>
                      </Stack>
                    ))}
                  </Stack>
                </CardContent>
              </Card>
            </motion.div>
          ))}
        </Stack>
      ) : (
        <Stack spacing={2}>
          {[{ title: t.todoWaitingCeo, items: waiting }, { title: t.todoPlanned, items: planned }].map((group) => (
            <Card key={group.title}>
              <CardContent>
                <Typography variant="h6" sx={{ mb: 1 }}>{group.title} ({group.items.length})</Typography>
                <Stack spacing={1}>
                  {group.items.map((x, i) => (
                    <Stack key={i} direction="row" spacing={1.5} sx={{ alignItems: "flex-start" }}>
                      <Box sx={{ width: 18, height: 18, mt: 0.25, flexShrink: 0, borderRadius: 1, border: 2, borderColor: x.status === "waitingCeo" ? "warning.main" : "divider" }} />
                      <Typography variant="body2" sx={{ flex: 1 }}>{x[lang]}</Typography>
                      <Chip size="small" variant="outlined" label={x.area[lang]} />
                    </Stack>
                  ))}
                </Stack>
              </CardContent>
            </Card>
          ))}
          <Typography variant="caption" color="text.secondary">{statusLabel.waitingCeo}: {t.todoWaitingHint}</Typography>
        </Stack>
      )}
    </>
  );
}
