"use client";

import { alpha, createTheme } from "@mui/material/styles";

/** Pastel "magic desk" palette (newOffice.jpg): lavender glass, candy pink, soft ink and star yellow. */
export const brand = {
  purple: "#8A6BC6",
  purpleDeep: "#5B4391",
  lavender: "#CFC0EE",
  lilac: "#EDE6FA",
  pink: "#F28DBE",
  pinkSoft: "#F9D9EA",
  star: "#F6D66E",
  ink: "#3A3150",
  cream: "#F3EEFB",
  night: "#1B1530",
  nightPaper: "#261E40",
};

/** Frosted glass surfaces: cards, the top bar and the sidebar float over the starry background. */
export const glass = {
  light: { bg: "rgba(255,255,255,.62)", border: "rgba(255,255,255,.8)", shadow: "0 8px 28px rgba(110,80,170,.12)" },
  dark: { bg: "rgba(40,31,66,.62)", border: "rgba(190,165,245,.16)", shadow: "0 8px 28px rgba(0,0,0,.35)" },
  blur: "blur(18px) saturate(140%)",
};

/** Four-point star doodles; drawn twice (white and yellow, offset) over the page gradient. */
function starField(fill: string, opacity: number) {
  const svg =
    `<svg xmlns='http://www.w3.org/2000/svg' width='360' height='300'><g fill='${fill}' opacity='${opacity}'>` +
    "<path d='M40 30l3 9 9 3-9 3-3 9-3-9-9-3 9-3z'/><path d='M250 60l2 6 6 2-6 2-2 6-2-6-6-2 6-2z'/>" +
    "<path d='M170 180l4 11 11 4-11 4-4 11-4-11-11-4 11-4z'/><path d='M320 230l2 5 5 2-5 2-2 5-2-5-5-2 5-2z'/>" +
    "<circle cx='110' cy='120' r='2.5'/><circle cx='300' cy='140' r='2'/><circle cx='60' cy='250' r='2'/></g></svg>";
  return `url("data:image/svg+xml,${encodeURIComponent(svg)}")`;
}

const pageBackground = {
  light: {
    backgroundImage: `${starField("#FFFFFF", 0.8)}, ${starField(brand.star, 0.38)}, linear-gradient(160deg, #E4D9F6 0%, #EEE3F8 45%, #F8E3EE 100%)`,
    backgroundPosition: "0 0, 180px 150px, 0 0",
  },
  dark: {
    backgroundImage: `${starField("#B9A3F0", 0.22)}, ${starField(brand.star, 0.18)}, linear-gradient(160deg, #1B1530 0%, #241A3D 55%, #2E1D3A 100%)`,
    backgroundPosition: "0 0, 180px 150px, 0 0",
  },
};

const headingFont = "var(--font-thai), system-ui, sans-serif";

export const theme = createTheme({
  cssVariables: { colorSchemeSelector: "class" },
  colorSchemes: {
    light: {
      palette: {
        primary: { main: brand.purple, dark: brand.purpleDeep, light: brand.lavender, contrastText: "#fff" },
        secondary: { main: brand.pink, contrastText: "#fff" },
        background: { default: brand.cream, paper: "#ffffff" },
        text: { primary: brand.ink, secondary: "#7D7296" },
        divider: alpha(brand.purple, 0.14),
      },
    },
    dark: {
      palette: {
        primary: { main: "#B9A0F2", dark: brand.purple, light: brand.lilac, contrastText: brand.night },
        secondary: { main: "#F7A6CE", contrastText: brand.night },
        background: { default: brand.night, paper: brand.nightPaper },
        text: { primary: "#F3EDFF", secondary: "#B9AEDA" },
        divider: alpha("#B9A0F2", 0.18),
      },
    },
  },
  typography: {
    fontFamily: "var(--font-thai), system-ui, sans-serif",
    h3: { fontFamily: headingFont, fontWeight: 600 },
    h4: { fontFamily: headingFont, fontWeight: 600, letterSpacing: -0.2 },
    h5: { fontFamily: headingFont, fontWeight: 600, letterSpacing: -0.1 },
    h6: { fontFamily: headingFont, fontWeight: 600 },
    overline: { fontWeight: 600, letterSpacing: 1.2 },
    button: { fontWeight: 600 },
  },
  shape: { borderRadius: 16 },
  components: {
    MuiCssBaseline: {
      styleOverrides: (th) => ({
        body: {
          minHeight: "100vh",
          backgroundAttachment: "fixed",
          ...pageBackground.light,
          ...th.applyStyles("dark", pageBackground.dark),
        },
      }),
    },
    MuiCard: {
      defaultProps: { elevation: 0 },
      styleOverrides: {
        root: ({ theme }) => ({
          borderRadius: 24,
          backgroundColor: glass.light.bg,
          backdropFilter: glass.blur,
          border: `1px solid ${glass.light.border}`,
          boxShadow: glass.light.shadow,
          transition: "transform .25s cubic-bezier(.2,.8,.2,1), box-shadow .25s ease",
          ...theme.applyStyles("dark", { backgroundColor: glass.dark.bg, border: `1px solid ${glass.dark.border}`, boxShadow: glass.dark.shadow }),
        }),
      },
    },
    MuiPaper: { styleOverrides: { rounded: { borderRadius: 18 } } },
    MuiButton: {
      defaultProps: { disableElevation: true },
      styleOverrides: {
        root: { textTransform: "none", borderRadius: 999, paddingInline: 18, transition: "transform .15s ease, box-shadow .2s ease, background-color .2s ease" },
        contained: {
          boxShadow: `0 6px 16px ${alpha(brand.purple, 0.28)}`,
          "&:hover": { transform: "translateY(-1px)", boxShadow: `0 10px 22px ${alpha(brand.purple, 0.36)}` },
          "&:active": { transform: "translateY(0) scale(.98)" },
        },
      },
    },
    MuiChip: { styleOverrides: { root: { borderRadius: 999, fontWeight: 500 } } },
    MuiDialog: {
      styleOverrides: {
        paper: ({ theme }) => ({
          borderRadius: 28,
          backgroundColor: "rgba(255,255,255,.88)",
          backdropFilter: glass.blur,
          border: `1px solid ${glass.light.border}`,
          ...theme.applyStyles("dark", { backgroundColor: "rgba(38,30,64,.92)", border: `1px solid ${glass.dark.border}` }),
        }),
      },
    },
    MuiDialogTitle: { styleOverrides: { root: { fontFamily: headingFont, fontWeight: 600 } } },
    MuiOutlinedInput: {
      styleOverrides: {
        root: ({ theme }) => ({
          borderRadius: 14,
          backgroundColor: "rgba(255,255,255,.55)",
          ...theme.applyStyles("dark", { backgroundColor: "rgba(255,255,255,.04)" }),
        }),
      },
    },
    MuiTableCell: {
      styleOverrides: {
        root: ({ theme }) => ({ borderColor: theme.vars.palette.divider }),
        head: ({ theme }) => ({ fontWeight: 600, color: theme.vars.palette.text.secondary, fontSize: 13 }),
      },
    },
    MuiTableRow: { styleOverrides: { root: { transition: "background-color .2s ease" } } },
    MuiTabs: {
      styleOverrides: {
        indicator: { height: 3, borderRadius: 3 },
      },
    },
    MuiTab: { styleOverrides: { root: { textTransform: "none", fontWeight: 600, minHeight: 44 } } },
    MuiLinearProgress: {
      styleOverrides: {
        root: { borderRadius: 999, backgroundColor: alpha(brand.lavender, 0.5) },
        bar: { borderRadius: 999, backgroundImage: `linear-gradient(90deg, ${brand.purple}, ${brand.pink})` },
      },
    },
    MuiCheckbox: { styleOverrides: { root: { borderRadius: 8 } } },
    MuiListItemButton: {
      styleOverrides: {
        root: ({ theme }) => ({
          borderRadius: 14,
          margin: "2px 10px",
          transition: "background-color .2s ease, transform .2s ease",
          "&:hover": { transform: "translateX(2px)", backgroundColor: "rgba(255,255,255,.55)" },
          "&.Mui-selected": {
            backgroundColor: "#fff",
            color: theme.vars.palette.primary.main,
            boxShadow: `0 4px 14px ${alpha(brand.purple, 0.14)}`,
            "& .MuiListItemIcon-root": { color: theme.vars.palette.primary.main },
            "&:hover": { backgroundColor: "#fff" },
          },
          ...theme.applyStyles("dark", {
            "&:hover": { transform: "translateX(2px)", backgroundColor: "rgba(255,255,255,.06)" },
            "&.Mui-selected": { backgroundColor: "rgba(185,160,242,.16)", boxShadow: "none", "&:hover": { backgroundColor: "rgba(185,160,242,.2)" } },
          }),
        }),
      },
    },
  },
});
