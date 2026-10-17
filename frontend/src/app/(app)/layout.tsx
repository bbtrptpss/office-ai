"use client";

import { useEffect } from "react";
import { usePathname, useRouter } from "next/navigation";
import { motion, useReducedMotion } from "motion/react";
import { Box, Typography } from "@mui/material";
import AppShell from "@/components/AppShell";
import { Mascot } from "@/components/cute";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";

export default function AuthenticatedLayout({ children }: { children: React.ReactNode }) {
  const { me, loading } = useAuth();
  const { t } = useI18n();
  const router = useRouter();
  const pathname = usePathname();
  const reduce = useReducedMotion();

  useEffect(() => {
    if (!loading && !me) router.replace("/login");
  }, [loading, me, router]);

  if (loading || !me) {
    return (
      <Box sx={{ display: "grid", placeItems: "center", minHeight: "100vh", bgcolor: "background.default" }}>
        <Box sx={{ textAlign: "center" }}>
          <Mascot size={96} />
          <Typography color="text.secondary" sx={{ mt: 1 }}>{t.loading}</Typography>
        </Box>
      </Box>
    );
  }

  return (
    <AppShell>
      <motion.div
        key={pathname}
        initial={reduce ? false : { opacity: 0, y: 12 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.28, ease: [0.2, 0.8, 0.2, 1] }}
      >
        {children}
      </motion.div>
    </AppShell>
  );
}
