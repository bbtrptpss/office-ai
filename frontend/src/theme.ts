"use client";

import { alpha, createTheme } from "@mui/material/styles";

/** Kuromi-inspired palette: deep purple, lavender, bubblegum pink and soft ink. */
export const brand = {
  purple: "#7B4FC9",
  purpleDeep: "#4B2A86",
  lavender: "#CDB8F5",
  lilac: "#EFE6FF",
  pink: "#FF7EB9",
  pinkSoft: "#FFD3E8",
  ink: "#2A2140",
  cream: "#FBF8FF",
  night: "#17112A",
  nightPaper: "#221A3A",
};

const headingFont = "var(--font-cute), var(--font-thai), system-ui, sans-serif";

export const theme = createTheme({
  cssVariables: { colorSchemeSelector: "class" },
  colorSchemes: {
    light: {
      palette: {
        primary: { main: brand.purple, dark: brand.purpleDeep, light: brand.lavender, contrastText: "#fff" },
        secondary: { main: brand.pink, contrastText: "#fff" },
        background: { default: brand.cream, paper: "#ffffff" },
        text: { primary: brand.ink, secondary: "#6E6487" },
        divider: alpha(brand.purple, 0.14),
      },
    },
    dark: {
      palette: {
        primary: { main: "#B595F5", dark: brand.purple, light: brand.lilac, contrastText: brand.night },
        secondary: { main: "#FF9CCB", contrastText: brand.night },
        background: { default: brand.night, paper: brand.nightPaper },
        text: { primary: "#F3EDFF", secondary: "#B9AEDA" },
        divider: alpha("#B595F5", 0.18),
      },
    },
  },
  typography: {
    fontFamily: "var(--font-thai), system-ui, sans-serif",
    h4: { fontFamily: headingFont, fontWeight: 700 },
    h5: { fontFamily: headingFont, fontWeight: 700, letterSpacing: 0.2 },
    h6: { fontFamily: headingFont, fontWeight: 600 },
    button: { fontWeight: 600 },
  },
  shape: { borderRadius: 18 },
  components: {
    MuiCard: {
      defaultProps: { elevation: 0 },
      styleOverrides: {
        root: ({ theme }) => ({
          borderRadius: 22,
          border: `1.5px solid ${theme.vars.palette.divider}`,
          boxShadow: `0 6px 20px ${alpha(brand.purple, 0.08)}`,
          transition: "transform .25s cubic-bezier(.2,.8,.2,1), box-shadow .25s ease, border-color .25s ease",
          ...theme.applyStyles("dark", { boxShadow: "0 6px 24px rgba(0,0,0,.35)" }),
        }),
      },
    },
    MuiPaper: { styleOverrides: { rounded: { borderRadius: 18 } } },
    MuiButton: {
      defaultProps: { disableElevation: true },
      styleOverrides: {
        root: { textTransform: "none", borderRadius: 999, paddingInline: 18, transition: "transform .15s ease, box-shadow .2s ease" },
        contained: ({ theme }) => ({
          backgroundImage: `linear-gradient(135deg, ${brand.purple} 0%, ${brand.pink} 100%)`,
          boxShadow: `0 6px 16px ${alpha(brand.pink, 0.35)}`,
          "&:hover": { transform: "translateY(-1px)", boxShadow: `0 10px 22px ${alpha(brand.pink, 0.45)}` },
          "&:active": { transform: "translateY(0) scale(.98)" },
          "&.Mui-disabled": { backgroundImage: "none" },
          ...theme.applyStyles("dark", { color: "#fff" }),
        }),
      },
    },
    MuiChip: { styleOverrides: { root: { borderRadius: 999, fontWeight: 500 } } },
    MuiDialog: {
      styleOverrides: {
        paper: ({ theme }) => ({ borderRadius: 26, border: `1.5px solid ${theme.vars.palette.divider}` }),
      },
    },
    MuiDialogTitle: { styleOverrides: { root: { fontFamily: headingFont, fontWeight: 700 } } },
    MuiOutlinedInput: { styleOverrides: { root: { borderRadius: 14 } } },
    MuiTableCell: { styleOverrides: { head: { fontWeight: 600 } } },
    MuiTableRow: {
      styleOverrides: { root: { transition: "background-color .2s ease" } },
    },
    MuiLinearProgress: {
      styleOverrides: {
        root: { borderRadius: 999, backgroundColor: alpha(brand.lavender, 0.45) },
        bar: { borderRadius: 999, backgroundImage: `linear-gradient(90deg, ${brand.purple}, ${brand.pink})` },
      },
    },
    MuiListItemButton: {
      styleOverrides: {
        root: ({ theme }) => ({
          borderRadius: 14,
          margin: "2px 10px",
          transition: "background-color .2s ease, transform .2s ease",
          "&:hover": { transform: "translateX(2px)" },
          "&.Mui-selected": {
            backgroundColor: alpha(brand.purple, 0.12),
            color: theme.vars.palette.primary.main,
            "& .MuiListItemIcon-root": { color: theme.vars.palette.primary.main },
          },
        }),
      },
    },
  },
});
