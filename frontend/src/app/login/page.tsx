"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { motion } from "motion/react";
import { Alert, Box, Button, Card, CardContent, Stack, TextField, Typography } from "@mui/material";
import { ApiError } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { Mascot, Sparkles } from "@/components/cute";
import { brand } from "@/theme";

export default function LoginPage() {
  const { login } = useAuth();
  const { t } = useI18n();
  const router = useRouter();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await login(email, password);
      router.replace("/");
    } catch (err) {
      setError(err instanceof ApiError && err.status === 423 ? t.lockedOut : t.loginFailed);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Box
      sx={{
        position: "relative",
        minHeight: "100vh",
        display: "grid",
        placeItems: "center",
        p: 2,
        overflow: "hidden",
        background: `linear-gradient(135deg, ${brand.purpleDeep} 0%, ${brand.purple} 45%, ${brand.pink} 100%)`,
      }}
    >
      <Sparkles count={22} />
      <motion.div
        initial={{ opacity: 0, y: 24, scale: 0.96 }}
        animate={{ opacity: 1, y: 0, scale: 1 }}
        transition={{ duration: 0.5, ease: [0.2, 0.8, 0.2, 1] }}
        style={{ width: "100%", maxWidth: 420, position: "relative" }}
      >
        <Box sx={{ display: "flex", justifyContent: "center", mb: -4, position: "relative", zIndex: 1 }}>
          <Mascot size={120} />
        </Box>
        <Card sx={{ borderRadius: 8, boxShadow: "0 24px 60px rgba(40,10,80,.35)" }}>
          <CardContent sx={{ p: 4, pt: 6 }}>
            <Typography variant="h5" align="center" gutterBottom>
              {t.appName}
            </Typography>
            <Typography color="text.secondary" align="center" sx={{ mb: 3 }}>
              {t.welcome}
            </Typography>
            <form onSubmit={submit}>
              <Stack spacing={2}>
                {error && <Alert severity="error">{error}</Alert>}
                <TextField label={t.email} type="email" autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} required autoFocus />
                <TextField label={t.password} type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} required />
                <Button type="submit" variant="contained" size="large" disabled={busy}>
                  {t.login}
                </Button>
              </Stack>
            </form>
          </CardContent>
        </Card>
      </motion.div>
    </Box>
  );
}
