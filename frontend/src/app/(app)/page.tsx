"use client";

import { useEffect, useState } from "react";
import { animate, useReducedMotion } from "motion/react";
import Link from "next/link";
import { Box, Button, Card, CardContent, Chip, Grid, LinearProgress, List, ListItem, ListItemIcon, ListItemText, Stack, Typography } from "@mui/material";
import { Announcement, api, Dashboard, UsageSummary, usd } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { Loading, useLoad } from "@/components/common";
import { Avatar, Mascot, OfficeScene, Rise, Sparkles } from "@/components/cute";
import { brand } from "@/theme";

function CountUp({ value }: { value: number }) {
  const reduce = useReducedMotion();
  const [shown, setShown] = useState(reduce ? value : 0);
  useEffect(() => {
    if (reduce) return;
    const controls = animate(0, value, { duration: 0.9, ease: "easeOut", onUpdate: (v) => setShown(Math.round(v)) });
    return () => controls.stop();
  }, [value, reduce]);
  return <>{reduce ? value : shown}</>;
}

function Stat({ label, value, suffix, emoji, tone, index }: {
  label: string; value: number; suffix?: string; emoji: string; tone?: "error" | "warning" | "success"; index: number;
}) {
  return (
    <Rise index={index}>
      <Card sx={{ height: "100%", "&:hover": { transform: "translateY(-3px)" } }}>
        <CardContent>
          <Stack direction="row" sx={{ alignItems: "center", justifyContent: "space-between" }}>
            <Typography variant="body2" color="text.secondary">{label}</Typography>
            <Box component="span" sx={{ fontSize: 22 }} aria-hidden>{emoji}</Box>
          </Stack>
          <Typography variant="h4" color={tone ? `${tone}.main` : undefined}>
            <CountUp value={value} />{suffix}
          </Typography>
        </CardContent>
      </Card>
    </Rise>
  );
}

export default function DashboardPage() {
  const { t } = useI18n();
  const { me, can } = useAuth();
  const { data, error, reload } = useLoad(() => api.get<Dashboard>("/api/dashboard"));
  useRealtime("taskChanged", () => void reload());
  useRealtime("organizationChanged", () => void reload());

  if (!data) return <Loading error={error} />;
  const pct = data.totalTasks ? Math.round((data.completedTasks / data.totalTasks) * 100) : 0;

  return (
    <>
      {/* Hero: the cartoon office */}
      <Rise>
        <Card
          sx={{
            position: "relative",
            overflow: "hidden",
            mb: 3,
            color: "#fff",
            border: 0,
            background: `linear-gradient(120deg, ${brand.purpleDeep} 0%, ${brand.purple} 55%, ${brand.pink} 120%)`,
          }}
        >
          <Sparkles count={12} />
          <CardContent sx={{ position: "relative", p: { xs: 2.5, md: 3.5 } }}>
            <Grid container spacing={2} sx={{ alignItems: "center" }}>
              <Grid size={{ xs: 12, md: 5 }}>
                <Stack direction="row" spacing={2} sx={{ alignItems: "center" }}>
                  <Mascot size={84} />
                  <Box>
                    <Typography variant="h5">
                      {t.hello} {me?.displayName} ✦
                    </Typography>
                    <Typography sx={{ opacity: 0.9 }}>{t.heroLine}</Typography>
                    <Typography variant="body2" sx={{ opacity: 0.85, mt: 0.5 }}>
                      🤖 {data.aiWorking} {t.aiWorking} · {data.aiAvailable} {t.aiAvailable}
                    </Typography>
                  </Box>
                </Stack>
              </Grid>
              <Grid size={{ xs: 12, md: 7 }}>
                <Box sx={{ bgcolor: "rgba(255,255,255,.14)", borderRadius: "22px", p: 1.5 }}>
                  <OfficeScene working={data.aiWorking} total={data.aiEmployees} />
                </Box>
              </Grid>
            </Grid>
          </CardContent>
        </Card>
      </Rise>

      <Grid container spacing={2}>
        <Grid size={{ xs: 6, md: 3 }}><Stat index={1} emoji="📁" label={t.activeProjects} value={data.activeProjects} /></Grid>
        <Grid size={{ xs: 6, md: 3 }}><Stat index={2} emoji="✅" label={t.tasksDone} value={data.completedTasks} suffix={` / ${data.totalTasks}`} tone="success" /></Grid>
        <Grid size={{ xs: 6, md: 3 }}><Stat index={3} emoji="🧱" label={t.tasksBlocked} value={data.blockedTasks} tone={data.blockedTasks ? "error" : undefined} /></Grid>
        <Grid size={{ xs: 6, md: 3 }}><Stat index={4} emoji="⏰" label={t.tasksOverdue} value={data.overdueTasks} tone={data.overdueTasks ? "warning" : undefined} /></Grid>
        <Grid size={{ xs: 6, md: 3 }}><Stat index={5} emoji="📝" label={t.waitingApproval} value={data.waitingApproval} tone={data.waitingApproval ? "warning" : undefined} /></Grid>
        <Grid size={{ xs: 6, md: 3 }}><Stat index={6} emoji="🤖" label={t.aiEmployees} value={data.aiEmployees} /></Grid>
        <Grid size={{ xs: 6, md: 3 }}><Stat index={7} emoji="💤" label={`AI ${t.aiAvailable}`} value={data.aiAvailable} /></Grid>
        <Grid size={{ xs: 6, md: 3 }}><Stat index={8} emoji="🧑‍💼" label={t.humanEmployees} value={data.humanEmployees} /></Grid>

        <Grid size={{ xs: 12, md: 6 }}>
          <Rise index={9}>
            <Card sx={{ height: "100%" }}>
              <CardContent>
                <Typography variant="h6" gutterBottom>{t.progress}</Typography>
                <Typography variant="h3" sx={{ fontFamily: "var(--font-cute)", fontWeight: 700 }}>
                  <CountUp value={pct} />%
                </Typography>
                <LinearProgress variant="determinate" value={pct} sx={{ height: 12, mt: 1 }} />
                <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
                  {data.completedTasks} / {data.totalTasks} {t.tasksTotal}
                </Typography>
              </CardContent>
            </Card>
          </Rise>
        </Grid>
        <Grid size={{ xs: 12, md: 6 }}>
          <Rise index={10}>
            <Card sx={{ height: "100%" }}>
              <CardContent>
                <Typography variant="h6">{t.workload}</Typography>
                {data.workload.length === 0 ? (
                  <Stack sx={{ alignItems: "center", py: 2 }}>
                    <Mascot size={64} />
                    <Typography color="text.secondary">{t.empty}</Typography>
                  </Stack>
                ) : (
                  <List dense>
                    {data.workload.map((w) => (
                      <ListItem key={w.employeeId} secondaryAction={<Typography fontWeight={600}>{w.openTasks}</Typography>}>
                        <ListItemIcon sx={{ minWidth: 44 }}><Avatar kind={w.kind} seed={w.displayName} size={32} /></ListItemIcon>
                        <ListItemText primary={w.displayName} />
                      </ListItem>
                    ))}
                  </List>
                )}
              </CardContent>
            </Card>
          </Rise>
        </Grid>
        {can("agent.read") && <AiCostCard />}
        {can("org.read") && <AnnouncementsCard />}
      </Grid>
    </>
  );
}

function AiCostCard() {
  const { t } = useI18n();
  const { data, reload } = useLoad(() => api.get<UsageSummary>("/api/ai/usage"));
  useRealtime("agentRunChanged", () => void reload());
  if (!data) return null;
  return (
    <Grid size={12}>
      <Rise index={11}>
        <Card>
          <CardContent>
            <Stack direction={{ xs: "column", sm: "row" }} spacing={3} sx={{ alignItems: { sm: "center" } }}>
              <Box sx={{ flex: 1 }}>
                <Typography variant="h6">💸 {t.aiCostMonth}</Typography>
                <Typography variant="caption" color="text.secondary">{t.realOnly}</Typography>
                <Typography variant="h4">{usd(data.thisMonthUsd)}</Typography>
              </Box>
              <Box>
                <Typography variant="body2" color="text.secondary">{t.tokens}</Typography>
                <Typography variant="h6">{data.thisMonthTokens.toLocaleString()}</Typography>
              </Box>
              <Box>
                <Typography variant="body2" color="text.secondary">{t.runs}</Typography>
                <Typography variant="h6">{data.runsThisMonth}</Typography>
              </Box>
              <Box>
                <Typography variant="body2" color="text.secondary">{t.activeRuns}</Typography>
                <Typography variant="h6">{data.activeRuns}</Typography>
              </Box>
            </Stack>
          </CardContent>
        </Card>
      </Rise>
    </Grid>
  );
}

function AnnouncementsCard() {
  const { t, lang } = useI18n();
  const { data, reload } = useLoad(() => api.get<Announcement[]>("/api/announcements"));
  useRealtime("announcementsChanged", () => void reload());
  useRealtime("announcementRead", () => void reload());
  if (!data || data.length === 0) return null;
  const fmt = new Intl.DateTimeFormat(lang === "th" ? "th-TH" : "en-GB", { dateStyle: "medium" });
  return (
    <Grid size={12}>
      <Rise index={12}>
        <Card>
          <CardContent>
            <Stack direction="row" sx={{ alignItems: "center", mb: 1 }}>
              <Typography variant="h6" sx={{ flex: 1 }}>📣 {t.latestAnnouncements}</Typography>
              <Button component={Link} href="/announcements" size="small">{t.navAnnouncements}</Button>
            </Stack>
            <List dense disablePadding>
              {data.slice(0, 3).map((a) => (
                <ListItem key={a.id} disableGutters>
                  <ListItemText
                    primary={<Stack direction="row" spacing={1} component="span" sx={{ alignItems: "center" }}>
                      <span style={{ fontWeight: a.isRead ? 400 : 700 }}>{a.isPinned ? "📌 " : ""}{a.title}</span>
                      {a.priority === "Important" && <Chip size="small" color="error" variant="outlined" label={t.important} sx={{ height: 18 }} />}
                    </Stack>}
                    secondary={`${a.departmentName ?? t.wholeCompany} · ${fmt.format(new Date(a.publishedAt ?? a.createdAt))}`} />
                </ListItem>
              ))}
            </List>
          </CardContent>
        </Card>
      </Rise>
    </Grid>
  );
}
