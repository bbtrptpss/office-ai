"use client";

import { useRef, useState, useSyncExternalStore } from "react";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import {
  AppBar, Box, ButtonBase, Chip, Divider, IconButton, List, ListItemButton, ListItemIcon, ListItemText,
  Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField, Toolbar, Tooltip, Typography,
} from "@mui/material";
import { useColorScheme } from "@mui/material/styles";
import DashboardIcon from "@mui/icons-material/SpaceDashboardOutlined";
import ViewInArIcon from "@mui/icons-material/ViewInArOutlined";
import AccountTreeIcon from "@mui/icons-material/AccountTreeOutlined";
import PeopleIcon from "@mui/icons-material/PeopleAltOutlined";
import ApartmentIcon from "@mui/icons-material/ApartmentOutlined";
import ChecklistIcon from "@mui/icons-material/ChecklistOutlined";
import ViewKanbanIcon from "@mui/icons-material/ViewKanbanOutlined";
import TimelineIcon from "@mui/icons-material/TimelineOutlined";
import TaskAltIcon from "@mui/icons-material/TaskAltOutlined";
import ForumIcon from "@mui/icons-material/ForumOutlined";
import CampaignIcon from "@mui/icons-material/CampaignOutlined";
import EventIcon from "@mui/icons-material/EventOutlined";
import NewReleasesIcon from "@mui/icons-material/NewReleasesOutlined";
import MenuBookIcon from "@mui/icons-material/MenuBookOutlined";
import PaymentsIcon from "@mui/icons-material/PaymentsOutlined";
import VerifiedUserIcon from "@mui/icons-material/VerifiedUserOutlined";
import ExtensionIcon from "@mui/icons-material/ExtensionOutlined";
import AdminPanelSettingsIcon from "@mui/icons-material/AdminPanelSettingsOutlined";
import HistoryIcon from "@mui/icons-material/HistoryOutlined";
import SmartToyIcon from "@mui/icons-material/SmartToyOutlined";
import BusinessIcon from "@mui/icons-material/BusinessCenterOutlined";
import LogoutIcon from "@mui/icons-material/Logout";
import DarkModeIcon from "@mui/icons-material/DarkModeOutlined";
import LightModeIcon from "@mui/icons-material/LightModeOutlined";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { stopRealtime, useRealtime } from "@/lib/realtime";
import { Announcement, api, Approval, ChatChannel } from "@/lib/api";
import { useLoad } from "./common";
import { Avatar, Mascot } from "@/components/cute";
import { brand, glass } from "@/theme";
import { NotificationBell } from "@/components/notifications";
import { LATEST_VERSION, SEEN_KEY } from "@/lib/changelog";
import { art } from "@/lib/characters";

const MENU_WIDTH = 300;

/** Pages also offered as pill tabs in the top bar, like the reference design's Dashboard / Widgets / Recent. */
const TOP_TABS: NavKey[] = ["navDashboard", "navOffice", "navProjects", "navTaskBoard", "navChat", "navApprovals"];
/** Shorter top-bar labels where the menu name is too long for a pill. */
const TOP_LABELS: Partial<Record<NavKey, "navTaskBoardShort">> = { navTaskBoard: "navTaskBoardShort" };
/** Width from which the top-bar pills show their labels next to the icons. */
const WIDE = "@media (min-width: 1280px)";

/** A frosted-glass panel floating over the starry background. */
const glassPanel = (th: import("@mui/material/styles").Theme) => ({
  backgroundColor: glass.light.bg,
  backdropFilter: glass.blur,
  border: `1px solid ${glass.light.border}`,
  boxShadow: glass.light.shadow,
  ...th.applyStyles("dark", { backgroundColor: glass.dark.bg, border: `1px solid ${glass.dark.border}`, boxShadow: glass.dark.shadow }),
});

type NavKey =
  | "navDashboard" | "navOffice" | "navOrganization" | "navEmployees" | "navDepartments" | "navProjects" | "navTaskBoard" | "navActivity"
  | "navApprovals" | "navChat" | "navAnnouncements" | "navMeetings" | "navKnowledge" | "navFinance" | "navQuality" | "navIntegrations"
  | "navSecurity" | "navAudit" | "aiSettings" | "companySettings" | "navUpdates";

interface NavItem {
  key: NavKey;
  href?: string;
  icon: React.ReactNode;
  permission?: string;
  /** Phase in which the page arrives; shown disabled until then. */
  phase?: number;
}

const NAV: NavItem[] = [
  { key: "navDashboard", href: "/", icon: <DashboardIcon />, permission: "project.read" },
  { key: "navOffice", href: "/office", icon: <ViewInArIcon />, permission: "org.read" },
  { key: "navOrganization", href: "/organization", icon: <AccountTreeIcon />, permission: "org.read" },
  { key: "navEmployees", href: "/employees", icon: <PeopleIcon />, permission: "org.read" },
  { key: "navDepartments", href: "/departments", icon: <ApartmentIcon />, permission: "org.read" },
  { key: "navProjects", href: "/projects", icon: <ViewKanbanIcon />, permission: "project.read" },
  { key: "navTaskBoard", href: "/tasks", icon: <ChecklistIcon />, permission: "task.read" },
  { key: "navActivity", href: "/agents", icon: <TimelineIcon />, permission: "agent.read" },
  { key: "navApprovals", href: "/approvals", icon: <TaskAltIcon />, permission: "task.read" },
  { key: "navChat", href: "/chat", icon: <ForumIcon />, permission: "org.read" },
  { key: "navAnnouncements", href: "/announcements", icon: <CampaignIcon />, permission: "org.read" },
  { key: "navMeetings", href: "/meetings", icon: <EventIcon />, permission: "org.read" },
  { key: "navKnowledge", href: "/knowledge", icon: <MenuBookIcon />, permission: "project.read" },
  { key: "navFinance", href: "/finance", icon: <PaymentsIcon />, permission: "finance.read" },
  { key: "navQuality", href: "/quality", icon: <VerifiedUserIcon />, permission: "audit.read" },
  { key: "navIntegrations", href: "/integrations", icon: <ExtensionIcon />, permission: "security.manage" },
  { key: "companySettings", href: "/company", icon: <BusinessIcon />, permission: "project.read" },
  { key: "aiSettings", href: "/ai-settings", icon: <SmartToyIcon />, permission: "agent.read" },
  { key: "navSecurity", href: "/security", icon: <AdminPanelSettingsIcon />, permission: "security.manage" },
  { key: "navAudit", href: "/audit", icon: <HistoryIcon />, permission: "audit.read" },
  { key: "navUpdates", href: "/updates", icon: <NewReleasesIcon /> },
];

export default function AppShell({ children }: { children: React.ReactNode }) {
  const { me, can, logout } = useAuth();
  const { t, lang, setLang } = useI18n();
  const { mode, systemMode, setMode } = useColorScheme();
  const pathname = usePathname();
  const router = useRouter();
  // The menu lives behind the • • • at the top left: a mouse opens it by hovering, touch and keyboard by pressing.
  const [open, setOpen] = useState(false);
  const closeTimer = useRef<number | undefined>(undefined);
  const lastPointer = useRef<string>("");
  const showMenu = () => { window.clearTimeout(closeTimer.current); setOpen(true); };
  const hideMenuSoon = () => { window.clearTimeout(closeTimer.current); closeTimer.current = window.setTimeout(() => setOpen(false), 200); };
  const [passwordOpen, setPasswordOpen] = useState(false);
  const dark = (mode === "system" ? systemMode : mode) === "dark";

  const items = NAV.filter((i) => !i.permission || can(i.permission));
  const pending = usePendingApprovals(can("task.read"));
  const unreadChat = useUnreadChat(can("org.read"));
  const unreadNews = useUnreadAnnouncements(can("org.read"));
  const badges: Partial<Record<NavKey, number>> = { navApprovals: pending, navChat: unreadChat, navAnnouncements: unreadNews };
  const updatesUnseen = useUpdatesUnseen();

  const drawer = (
    <Box sx={{ display: "flex", flexDirection: "column", maxHeight: "calc(100vh - 96px)" }}>
      <Stack direction="row" spacing={1} sx={{ alignItems: "center", px: 2, pt: 2, pb: 1.5 }}>
        <Box sx={{ width: 52, height: 52, borderRadius: "18px", display: "grid", placeItems: "center", bgcolor: brand.pinkSoft, flexShrink: 0 }}>
          {art("logo")
            // eslint-disable-next-line @next/next/no-img-element
            ? <img src={art("logo")} alt="" width={52} height={52} style={{ borderRadius: 18 }} />
            : <Mascot size={46} />}
        </Box>
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="h6" sx={{ lineHeight: 1.15, fontSize: 16 }}>
            {t.appName}
          </Typography>
          <Typography variant="overline" color="primary" sx={{ fontSize: 10, lineHeight: 1.6 }}>Magic AI Office ✦</Typography>
        </Box>
      </Stack>
      <List dense sx={{ flex: 1, minHeight: 0, overflowY: "auto" }}>
        {items.map((item) => {
          const selected = item.href === "/" ? pathname === "/" : !!item.href && pathname.startsWith(item.href);
          return (
            <ListItemButton
              key={item.key}
              component={item.href ? Link : "div"}
              href={item.href ?? ""}
              selected={selected}
              disabled={!item.href}
              onClick={() => setOpen(false)}
            >
              <ListItemIcon sx={{ minWidth: 36 }}>{item.icon}</ListItemIcon>
              <ListItemText primary={t[item.key]} />
              {(badges[item.key] ?? 0) > 0 && <Chip size="small" color="secondary" label={badges[item.key]! > 99 ? "99+" : badges[item.key]} sx={{ height: 20, fontWeight: 700 }} />}
              {item.key === "navUpdates" && updatesUnseen && <Chip size="small" color="secondary" label={t.newBadge} sx={{ height: 20, fontWeight: 700 }} />}
              {item.phase && <Chip size="small" variant="outlined" label={`${t.comingInPhase} ${item.phase}`} sx={{ height: 20, fontSize: 11 }} />}
            </ListItemButton>
          );
        })}
      </List>
      <Divider sx={{ mx: 2 }} />
      <Stack direction="row" spacing={1.5} onClick={() => setPasswordOpen(true)} title={t.changePassword}
        sx={{ p: 2, m: 1, borderRadius: "18px", alignItems: "center", cursor: "pointer", "&:hover": { bgcolor: "action.hover" } }}>
        <Avatar kind="Human" seed={me?.displayName ?? "me"} size={38} />
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="body2" fontWeight={600} noWrap>
            {me?.displayName}
          </Typography>
          <Typography variant="caption" color="text.secondary" noWrap component="div">
            {me?.roles.join(", ")}
          </Typography>
        </Box>
      </Stack>
    </Box>
  );

  const tabs = TOP_TABS.map((k) => items.find((i) => i.key === k)).filter((i): i is NavItem => !!i?.href);

  return (
    <Box sx={{ display: "flex", minHeight: "100vh" }}>
      <AppBar
        position="fixed"
        color="transparent"
        elevation={0}
        sx={{ px: { xs: 1, md: 2 }, pt: 1.5, background: "none" }}
      >
        <Toolbar sx={(th) => ({ ...glassPanel(th), borderRadius: 999, minHeight: { xs: 56, sm: 56 }, px: { xs: 1, sm: 1.5 }, gap: 0.5 })}>
          <Box sx={{ position: "relative", mr: 1 }} onPointerEnter={(e) => e.pointerType === "mouse" && showMenu()}
            onPointerLeave={(e) => e.pointerType === "mouse" && hideMenuSoon()} onKeyDown={(e) => e.key === "Escape" && setOpen(false)}>
            <ButtonBase aria-label={t.menu} aria-haspopup="menu" aria-expanded={open}
              onPointerDown={(e) => { lastPointer.current = e.pointerType; }}
              onClick={() => (lastPointer.current === "mouse" ? showMenu() : setOpen((o) => !o))}
              sx={{ gap: 1, p: 0.5, pr: 1, borderRadius: 999, transition: "background-color .2s", bgcolor: open ? "action.hover" : "transparent", "&:hover": { bgcolor: "action.hover" } }}>
              <Box aria-hidden sx={{ width: 36, height: 36, borderRadius: "12px", overflow: "hidden", display: "grid", placeItems: "center", bgcolor: brand.pinkSoft,
                transition: "transform .2s", transform: open ? "scale(1.08) rotate(-4deg)" : "none" }}>
                {art("logo")
                  // eslint-disable-next-line @next/next/no-img-element
                  ? <img src={art("logo")} alt="" width={36} height={36} />
                  : <Mascot size={32} float={false} />}
              </Box>
              {(badges.navApprovals ?? 0) + (badges.navChat ?? 0) + (badges.navAnnouncements ?? 0) > 0 && (
                <Box aria-hidden sx={{ width: 7, height: 7, borderRadius: "50%", bgcolor: "secondary.main" }} />
              )}
            </ButtonBase>
            <Box role="menu" sx={(th) => ({
              position: "absolute", top: "calc(100% + 12px)", left: -8, width: MENU_WIDTH, maxWidth: "calc(100vw - 24px)", zIndex: 10,
              borderRadius: "26px", overflow: "hidden", backgroundColor: "rgba(255,255,255,.985)",
              border: "1px solid rgba(255,255,255,.9)", boxShadow: "0 18px 50px rgba(110,80,170,.25)", color: "text.primary",
              transformOrigin: "top left", transition: "opacity .18s ease, transform .18s ease, visibility .18s",
              opacity: open ? 1 : 0, transform: open ? "none" : "translateY(-6px) scale(.98)", visibility: open ? "visible" : "hidden",
              ...th.applyStyles("dark", { backgroundColor: "rgba(38,30,64,.985)", border: "1px solid rgba(190,165,245,.18)", boxShadow: "0 18px 50px rgba(0,0,0,.5)" }),
            })}>
              {drawer}
            </Box>
          </Box>
          <Box sx={{ flex: 1, display: "flex", justifyContent: "center", minWidth: 0 }}>
            <Stack direction="row" spacing={0.5} sx={{ display: { xs: "none", sm: "flex" }, p: 0.5, borderRadius: 999, bgcolor: "action.hover" }}>
              {tabs.map((item) => {
                const selected = item.href === "/" ? pathname === "/" : pathname.startsWith(item.href!);
                const name = t[TOP_LABELS[item.key] ?? item.key];
                return (
                  <Tooltip key={item.key} title={name} slotProps={{ tooltip: { sx: { [WIDE]: { display: "none" } } } }}>
                    <Button component={Link} href={item.href!} size="small" startIcon={item.icon} aria-label={name}
                      sx={(th) => ({
                        px: 1.25, py: 0.5, minWidth: 0, color: selected ? "primary.main" : "text.secondary", fontWeight: selected ? 700 : 500,
                        bgcolor: selected ? "#fff" : "transparent", boxShadow: selected ? `0 2px 10px ${brand.purple}26` : "none",
                        "& .MuiSvgIcon-root": { fontSize: 18 }, "&:hover": { bgcolor: selected ? "#fff" : "action.hover" },
                        "& .MuiButton-startIcon": { mx: 0 }, [WIDE]: { px: 1.75, "& .MuiButton-startIcon": { mr: 1, ml: -0.5 } },
                        ...th.applyStyles("dark", { bgcolor: selected ? "rgba(185,160,242,.2)" : "transparent", "&:hover": { bgcolor: "rgba(185,160,242,.14)" } }),
                      })}>
                      {/* Below WIDE the six pills don't fit with their labels, so they show icons only (name in the tooltip). */}
                      <Box component="span" sx={{ display: "none", [WIDE]: { display: "inline" } }}>{name}</Box>
                      {(badges[item.key] ?? 0) > 0 && <Box component="span" sx={{ ml: 0.75, minWidth: 18, height: 18, px: 0.5, borderRadius: 999, bgcolor: "secondary.main", color: "#fff", fontSize: 11, lineHeight: "18px", textAlign: "center" }}>{badges[item.key]! > 99 ? "99+" : badges[item.key]}</Box>}
                    </Button>
                  </Tooltip>
                );
              })}
            </Stack>
          </Box>
          <NotificationBell />
          <Chip
            label={lang === "th" ? "EN" : "ไทย"}
            onClick={() => setLang(lang === "th" ? "en" : "th")}
            size="small"
            variant="outlined"
          />
          <Tooltip title={dark ? "Light" : "Dark"}>
            <IconButton onClick={() => setMode(dark ? "light" : "dark")}>{dark ? <LightModeIcon /> : <DarkModeIcon />}</IconButton>
          </Tooltip>
          <Tooltip title={t.logout}>
            <IconButton
              onClick={async () => {
                await stopRealtime();
                await logout();
                router.replace("/login");
              }}
            >
              <LogoutIcon />
            </IconButton>
          </Tooltip>
          <Tooltip title={t.changePassword}>
            <IconButton onClick={() => setPasswordOpen(true)} sx={{ p: 0.5 }}>
              <Avatar kind="Human" seed={me?.displayName ?? "me"} size={34} />
            </IconButton>
          </Tooltip>
        </Toolbar>
      </AppBar>

      <ChangePasswordDialog open={passwordOpen} onClose={() => setPasswordOpen(false)} />

      <Box component="main" sx={{ flex: 1, minWidth: 0, px: { xs: 1.5, md: 2 }, pb: 3, pt: { xs: 10, md: 11 } }}>
        {children}
      </Box>
    </Box>
  );
}

/** Number of approvals waiting for a decision, kept live. */
function usePendingApprovals(enabled: boolean) {
  const { data, reload } = useLoad(async () => (enabled ? (await api.get<Approval[]>("/api/approvals?status=Pending")).length : 0), String(enabled));
  useRealtime("approvalsChanged", () => void reload());
  return data ?? 0;
}

/** True while this browser has not opened /updates since the latest release. */
function useUpdatesUnseen() {
  return useSyncExternalStore(
    (notify) => {
      window.addEventListener("officeai:updatesSeen", notify);
      window.addEventListener("storage", notify);
      return () => {
        window.removeEventListener("officeai:updatesSeen", notify);
        window.removeEventListener("storage", notify);
      };
    },
    () => {
      try { return localStorage.getItem(SEEN_KEY) !== LATEST_VERSION; } catch { return false; }
    },
    () => false,
  );
}

/** Unread chat messages across channels, refreshed on every new message. */
function useUnreadChat(enabled: boolean) {
  const { data, reload } = useLoad(
    async () => (enabled ? (await api.get<ChatChannel[]>("/api/chat/channels")).reduce((n, c) => n + c.unread, 0) : 0),
    String(enabled),
  );
  useRealtime("chatMessage", () => void reload());
  useRealtime("chatRead", () => void reload());
  return data ?? 0;
}

/** Published announcements I have not acknowledged yet. */
function useUnreadAnnouncements(enabled: boolean) {
  const { data, reload } = useLoad(
    async () => (enabled ? (await api.get<Announcement[]>("/api/announcements")).filter((a) => !a.isRead).length : 0),
    String(enabled),
  );
  useRealtime("announcementsChanged", () => void reload());
  useRealtime("announcementRead", () => void reload());
  return data ?? 0;
}

function ChangePasswordDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useI18n();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [result, setResult] = useState<{ ok: boolean; text: string } | null>(null);
  const close = () => { setCurrent(""); setNext(""); setResult(null); onClose(); };
  return (
    <Dialog open={open} onClose={close} fullWidth maxWidth="xs">
      <DialogTitle>{t.changePassword}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {result && <Alert severity={result.ok ? "success" : "error"}>{result.text}</Alert>}
          <TextField type="password" label={t.currentPassword} autoComplete="current-password" value={current} onChange={(e) => setCurrent(e.target.value)} />
          <TextField type="password" label={t.newPassword} autoComplete="new-password" helperText="≥ 10" value={next} onChange={(e) => setNext(e.target.value)} />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={close}>{t.close}</Button>
        <Button variant="contained" disabled={!current || next.length < 10} onClick={async () => {
          try {
            await api.post("/api/auth/change-password", { currentPassword: current, newPassword: next });
            setResult({ ok: true, text: "✓" });
            setCurrent("");
            setNext("");
          } catch (e) {
            setResult({ ok: false, text: e instanceof Error ? e.message : String(e) });
          }
        }}>{t.save}</Button>
      </DialogActions>
    </Dialog>
  );
}
