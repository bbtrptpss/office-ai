"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { motion, useReducedMotion } from "motion/react";
import { Box, Button, Card, CardActionArea, CardContent, Chip, Stack, Tab, Tabs, Typography } from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import { api, MeetingSummary } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { Loading, PageHeader, StatusChip, useLoad } from "@/components/common";
import { Mascot } from "@/components/cute";
import { MeetingDialog } from "@/components/meetings";

export default function MeetingsPage() {
  const { t, lang } = useI18n();
  const { can } = useAuth();
  const router = useRouter();
  const reduce = useReducedMotion();
  const [tab, setTab] = useState<"upcoming" | "past">("upcoming");
  const [creating, setCreating] = useState(false);
  // "Upcoming" includes meetings that started within the last few hours, so one in progress is still on top.
  const [since] = useState(() => new Date(Date.now() - 6 * 3600_000).toISOString());
  const query = tab === "upcoming" ? `from=${encodeURIComponent(since)}` : `to=${encodeURIComponent(since)}`;
  const { data, error, reload } = useLoad(() => api.get<MeetingSummary[]>(`/api/meetings?${query}`), query);
  useRealtime("meetingChanged", () => void reload());

  const rows = tab === "past" ? [...(data ?? [])].reverse() : data ?? [];
  const day = new Intl.DateTimeFormat(lang === "th" ? "th-TH" : "en-GB", { weekday: "short", day: "numeric", month: "short" });
  const time = new Intl.DateTimeFormat(lang === "th" ? "th-TH" : "en-GB", { timeStyle: "short" });

  return (
    <>
      <PageHeader title={t.navMeetings}
        actions={can("comms.manage") && <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>{t.newMeeting}</Button>} />
      <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ mb: 2 }}>
        <Tab value="upcoming" label={t.upcoming} />
        <Tab value="past" label={t.past} />
      </Tabs>
      {!data ? <Loading error={error} /> : rows.length === 0 ? (
        <Stack sx={{ alignItems: "center", py: 6 }}><Mascot size={96} /><Typography color="text.secondary">{t.empty}</Typography></Stack>
      ) : (
        <Stack spacing={1.5}>
          {rows.map((m, i) => (
            <motion.div key={m.id} initial={reduce ? false : { opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: Math.min(i, 10) * 0.03 }}>
              <Card sx={{ opacity: m.status === "Cancelled" ? 0.6 : 1 }}>
                <CardActionArea component={Link} href={`/meetings/${m.id}`}>
                  <CardContent>
                    <Stack direction="row" spacing={2} sx={{ alignItems: "center" }}>
                      <Box sx={{ textAlign: "center", minWidth: 72, py: 0.5, borderRadius: 2, bgcolor: "action.hover" }}>
                        <Typography variant="caption" color="text.secondary" component="div">{day.format(new Date(m.scheduledAt))}</Typography>
                        <Typography fontWeight={700}>{time.format(new Date(m.scheduledAt))}</Typography>
                      </Box>
                      <Box sx={{ flex: 1, minWidth: 0 }}>
                        <Typography fontWeight={700} noWrap sx={{ textDecoration: m.status === "Cancelled" ? "line-through" : undefined }}>{m.title}</Typography>
                        <Typography variant="caption" color="text.secondary" component="div" noWrap>
                          {m.durationMinutes} min{m.location ? ` · ${m.location}` : ""} · {t.participants} {m.participantCount}
                          {m.openActionItems > 0 ? ` · ${t.actionItems} ${m.openActionItems}` : ""}
                        </Typography>
                      </Box>
                      <Stack direction={{ xs: "column", sm: "row" }} spacing={1} sx={{ alignItems: "flex-end" }}>
                        {m.projectName && <Chip size="small" variant="outlined" label={`📁 ${m.projectName}`} />}
                        {m.status !== "Scheduled" && <StatusChip value={m.status} />}
                        {m.minutesStatus !== "None" && <Chip size="small" variant="outlined" label={`${t.minutes}: ${m.minutesStatus === "Approved" ? "✓" : "…"}`}
                          color={m.minutesStatus === "Approved" ? "success" : "warning"} />}
                      </Stack>
                    </Stack>
                  </CardContent>
                </CardActionArea>
              </Card>
            </motion.div>
          ))}
        </Stack>
      )}
      {creating && (
        <MeetingDialog open onClose={() => setCreating(false)} onSaved={(m) => { setCreating(false); router.push(`/meetings/${m.id}`); }} />
      )}
    </>
  );
}
