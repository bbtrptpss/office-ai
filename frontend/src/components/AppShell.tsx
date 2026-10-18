"use client";

import { useState } from "react";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import {
  AppBar, Box, Chip, Divider, Drawer, IconButton, List, ListItemButton, ListItemIcon, ListItemText,
  Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField, Toolbar, Tooltip, Typography, useMediaQuery, useTheme,
} from "@mui/material";
import { useColorScheme } from "@mui/material/styles";
import MenuIcon from "@mui/icons-material/Menu";
import DashboardIcon from "@mui/icons-material/SpaceDashboardOutlined";
import AccountTreeIcon from "@mui/icons-material/AccountTreeOutlined";
import PeopleIcon from "@mui/icons-material/PeopleAltOutlined";
import ApartmentIcon from "@mui/icons-material/ApartmentOutlined";
import ViewKanbanIcon from "@mui/icons-material/ViewKanbanOutlined";
import TimelineIcon from "@mui/icons-material/TimelineOutlined";
import TaskAltIcon from "@mui/icons-material/TaskAltOutlined";
import ForumIcon from "@mui/icons-material/ForumOutlined";
import CampaignIcon from "@mui/icons-material/CampaignOutlined";
import EventIcon from "@mui/icons-material/EventOutlined";
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
import { brand } from "@/theme";

const DRAWER_WIDTH = 264;

type NavKey =
  | "navDashboard" | "navOrganization" | "navEmployees" | "navDepartments" | "navProjects" | "navActivity"
  | "navApprovals" | "navChat" | "navAnnouncements" | "navMeetings" | "navKnowledge" | "navFinance" | "navQuality" | "navIntegrations"
  | "navSecurity" | "navAudit" | "aiSettings" | "companySettings";

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
  { key: "navOrganization", href: "/organization", icon: <AccountTreeIcon />, permission: "org.read" },
  { key: "navEmployees", href: "/employees", icon: <PeopleIcon />, permission: "org.read" },
  { key: "navDepartments", href: "/departments", icon: <ApartmentIcon />, permission: "org.read" },
  { key: "navProjects", href: "/projects", icon: <ViewKanbanIcon />, permission: "project.read" },
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
];

export default function AppShell({ children }: { children: React.ReactNode }) {
  const { me, can, logout } = useAuth();
  const { t, lang, setLang } = useI18n();
  const { mode, systemMode, setMode } = useColorScheme();
  const pathname = usePathname();
  const router = useRouter();
  const muiTheme = useTheme();
  const desktop = useMediaQuery(muiTheme.breakpoints.up("md"));
  const [open, setOpen] = useState(false);
  const [passwordOpen, setPasswordOpen] = useState(false);
  const dark = (mode === "system" ? systemMode : mode) === "dark";

  const items = NAV.filter((i) => !i.permission || can(i.permission));
  const pending = usePendingApprovals(can("task.read"));
  const unreadChat = useUnreadChat(can("org.read"));
  const unreadNews = useUnreadAnnouncements(can("org.read"));
  const badges: Partial<Record<NavKey, number>> = { navApprovals: pending, navChat: unreadChat, navAnnouncements: unreadNews };

  const drawer = (
    <Box sx={{ display: "flex", flexDirection: "column", height: "100%" }}>
      <Stack direction="row" spacing={1} sx={{ alignItems: "center", px: 2, pt: 2, pb: 1 }}>
        <Mascot size={52} />
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="h6" sx={{ lineHeight: 1.15, fontSize: 17 }}>
            {t.appName}
          </Typography>
          <Typography variant="caption" color="text.secondary">Cute AI Office ✦</Typography>
        </Box>
      </Stack>
      <List dense sx={{ flex: 1, overflowY: "auto" }}>
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
              {item.phase && <Chip size="small" variant="outlined" label={`${t.comingInPhase} ${item.phase}`} sx={{ height: 20, fontSize: 11 }} />}
            </ListItemButton>
          );
        })}
      </List>
      <Divider />
      <Stack direction="row" spacing={1.5} onClick={() => setPasswordOpen(true)} title={t.changePassword}
        sx={{ p: 2, alignItems: "center", cursor: "pointer", "&:hover": { bgcolor: "action.hover" } }}>
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

  return (
    <Box sx={{ display: "flex", minHeight: "100vh", bgcolor: "background.default" }}>
      <AppBar
        position="fixed"
        color="inherit"
        elevation={0}
        sx={{ borderBottom: 1, borderColor: "divider", bgcolor: (th) => `rgba(${th.vars?.palette.background.defaultChannel} / 0.72)`, backdropFilter: "blur(12px)", width: { md: `calc(100% - ${DRAWER_WIDTH}px)` }, ml: { md: `${DRAWER_WIDTH}px` } }}
      >
        <Toolbar>
          {!desktop && (
            <IconButton edge="start" onClick={() => setOpen(true)} aria-label="menu" sx={{ mr: 1 }}>
              <MenuIcon />
            </IconButton>
          )}
          <Box sx={{ flex: 1 }} />
          <Chip
            label={lang === "th" ? "EN" : "ไทย"}
            onClick={() => setLang(lang === "th" ? "en" : "th")}
            size="small"
            variant="outlined"
            sx={{ mr: 1 }}
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
        </Toolbar>
      </AppBar>

      <Box component="nav" sx={{ width: { md: DRAWER_WIDTH }, flexShrink: { md: 0 } }}>
        <Drawer
          variant={desktop ? "permanent" : "temporary"}
          open={desktop || open}
          onClose={() => setOpen(false)}
          ModalProps={{ keepMounted: true }}
          sx={(th) => ({ "& .MuiDrawer-paper": { width: DRAWER_WIDTH, boxSizing: "border-box", borderRight: `1.5px solid ${th.vars?.palette.divider}`, backgroundImage: `linear-gradient(180deg, ${brand.lilac} 0%, transparent 55%)`, ...th.applyStyles("dark", { backgroundImage: "linear-gradient(180deg, #2c2150 0%, transparent 55%)" }) } })}
        >
          {drawer}
        </Drawer>
      </Box>

      <ChangePasswordDialog open={passwordOpen} onClose={() => setPasswordOpen(false)} />

      <Box component="main" sx={{ flex: 1, minWidth: 0, p: { xs: 2, md: 3 }, mt: 8, backgroundImage: `radial-gradient(600px 300px at 90% 0%, ${brand.pinkSoft}55, transparent 70%), radial-gradient(500px 280px at 10% 100%, ${brand.lavender}44, transparent 70%)`, backgroundAttachment: "fixed" }}>
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
