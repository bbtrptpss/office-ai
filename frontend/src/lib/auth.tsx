"use client";

import { createContext, useCallback, useContext, useEffect, useState } from "react";
import { api, ApiError, Me } from "./api";

interface Auth {
  me: Me | null;
  loading: boolean;
  can: (permission: string) => boolean;
  /** True when the password was right and an authenticator code is needed next (see loginCode). */
  login: (email: string, password: string) => Promise<boolean>;
  /** Second sign-in step: an authenticator code, or a recovery code. */
  loginCode: (code: string, recovery: boolean) => Promise<void>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<Auth | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [me, setMe] = useState<Me | null>(null);
  const [loading, setLoading] = useState(true);

  const fetchMe = useCallback(
    () =>
      api.get<Me>("/api/auth/me").catch((e) => {
        if (e instanceof ApiError && e.status === 401) return null;
        throw e;
      }),
    [],
  );

  const refresh = useCallback(async () => {
    setMe(await fetchMe());
  }, [fetchMe]);

  useEffect(() => {
    // Only a 401 means "signed out". Network errors (e.g. the API restarting) are retried instead of logging the user out.
    let cancelled = false;
    let attempt = 0;
    const load = () =>
      fetchMe()
        .then((m) => {
          if (cancelled) return;
          setMe(m);
          setLoading(false);
        })
        .catch(() => {
          if (cancelled) return;
          attempt++;
          if (attempt < 10) setTimeout(load, Math.min(5000, 500 * attempt));
          else {
            setMe(null);
            setLoading(false);
          }
        });
    load();
    return () => {
      cancelled = true;
    };
  }, [fetchMe]);

  const login = useCallback(
    async (email: string, password: string) => {
      const res = await api.post<{ requiresTwoFactor: boolean } | undefined>("/api/auth/login", { email, password });
      if (res?.requiresTwoFactor) return true;
      await refresh();
      return false;
    },
    [refresh],
  );

  const loginCode = useCallback(
    async (code: string, recovery: boolean) => {
      await api.post("/api/auth/login/2fa", recovery ? { recoveryCode: code } : { code });
      await refresh();
    },
    [refresh],
  );

  const logout = useCallback(async () => {
    await api.post("/api/auth/logout");
    setMe(null);
  }, []);

  // UI convenience only: the API enforces every permission server-side.
  const can = useCallback((p: string) => !!me?.permissions.includes(p), [me]);

  return <AuthContext.Provider value={{ me, loading, can, login, loginCode, logout }}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside AuthProvider");
  return ctx;
}
