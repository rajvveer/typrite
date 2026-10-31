import { InMemoryWebStorage, UserManager, WebStorageStateStore } from "oidc-client-ts";

export const ROLES = [
  "Viewer",
  "CampaignOperator",
  "DeviceOperator",
  "ReleaseManager",
  "Administrator",
] as const;

export type Role = (typeof ROLES)[number];

export interface Session {
  token: string;
  role: Role;
  subject: string;
  accountId: string | null;
}

const STORAGE_KEY = "typrite.admin.session";
let oidc: UserManager | null = null;
export let allowDevAuth = false;
export let initializationError: string | null = null;

export function canSignIn(): boolean {
  return allowDevAuth || oidc !== null;
}

async function validateOperatorSession(token: string): Promise<void> {
  const response = await fetch("/api/v1/admin/session", {
    headers: { Authorization: `Bearer ${token}`, Accept: "application/json" },
    cache: "no-store",
  });
  if (!response.ok) throw new AuthError("Your session is invalid or you do not have operator access. Please sign in again.");
  const profile: unknown = await response.json();
  if (typeof profile !== "object" || profile === null) throw new AuthError("Invalid operator profile.");
  const session = parseSession(JSON.stringify({ ...profile, token }));
  if (session === null || session.accountId === null) throw new AuthError("Invalid operator profile.");
  setSession(session);
}

// Run once before React mounts, so StrictMode cannot consume an authorization code twice.
export async function initializeAuth(): Promise<void> {
  const query = new URLSearchParams(window.location.search);
  const callback = query.has("state") && (query.has("code") || query.has("error"));
  try {
    const response = await fetch("/api/v1/auth/config", { cache: "no-store" });
    if (!response.ok) throw new AuthError("Sign-in is unavailable. Please try reloading the page.");
    const config = await response.json() as Record<string, unknown>;
    allowDevAuth = config.allowDevAuth === true;
    if (!allowDevAuth) {
      if (typeof config.authority !== "string" || !config.authority.startsWith("https://")
          || typeof config.clientId !== "string" || config.clientId.length === 0
          || typeof config.scope !== "string") {
        throw new AuthError("Operator sign-in is not configured. Contact your administrator.");
      }
      oidc = new UserManager({
        authority: config.authority,
        client_id: config.clientId,
        redirect_uri: `${window.location.origin}/`,
        response_type: "code",
        response_mode: "query",
        scope: config.scope,
        automaticSilentRenew: false,
        loadUserInfo: false,
        // PKCE transaction state survives redirects; only our validated session persists afterwards.
        stateStore: new WebStorageStateStore({ store: window.sessionStorage }),
        userStore: new WebStorageStateStore({ store: new InMemoryWebStorage() }),
      });
      if (callback) {
        const user = await oidc.signinRedirectCallback();
        await validateOperatorSession(user.access_token);
        await oidc.removeUser();
      } else {
        const token = getToken();
        if (token !== null) await validateOperatorSession(token);
      }
    }
  } catch (error) {
    clearSession();
    initializationError = error instanceof Error ? error.message : "Sign-in failed. Please try again.";
  } finally {
    if (callback) window.history.replaceState(null, "", "/#/login");
  }
}

export async function signIn(): Promise<void> {
  if (oidc === null) throw new AuthError("Operator sign-in is not configured.");
  await oidc.clearStaleState();
  await oidc.signinRedirect({ prompt: "login" });
}

type Listener = (session: Session | null) => void;
const listeners = new Set<Listener>();

function isRole(value: unknown): value is Role {
  return typeof value === "string" && (ROLES as readonly string[]).includes(value);
}

function parseSession(raw: string | null): Session | null {
  if (raw === null) return null;
  try {
    const parsed: unknown = JSON.parse(raw);
    if (typeof parsed !== "object" || parsed === null) return null;
    const rec = parsed as Record<string, unknown>;
    if (typeof rec.token !== "string" || rec.token.length === 0) return null;
    if (!isRole(rec.role)) return null;
    return {
      token: rec.token,
      role: rec.role,
      subject: typeof rec.subject === "string" ? rec.subject : "",
      accountId: typeof rec.accountId === "string" ? rec.accountId : null,
    };
  } catch {
    return null;
  }
}

export function getSession(): Session | null {
  try {
    return parseSession(sessionStorage.getItem(STORAGE_KEY));
  } catch {
    return null;
  }
}

export function getToken(): string | null {
  return getSession()?.token ?? null;
}

function notify(): void {
  const session = getSession();
  for (const listener of listeners) listener(session);
}

export function setSession(session: Session): void {
  sessionStorage.setItem(STORAGE_KEY, JSON.stringify(session));
  notify();
}

export function clearSession(): void {
  sessionStorage.removeItem(STORAGE_KEY);
  notify();
}

export function subscribe(listener: Listener): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

export interface DevLoginRequest {
  subject: string;
  role: Role;
  accountId?: string;
}

interface DevLoginResponse {
  token: string;
  accountId: string | null;
}

export class AuthError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "AuthError";
  }
}

export async function devLogin(request: DevLoginRequest): Promise<Session> {
  if (!allowDevAuth) throw new AuthError("Development sign-in is disabled.");
  let response: Response;
  try {
    response = await fetch("/api/v1/admin/dev-login", {
      method: "POST",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(request),
    });
  } catch (err) {
    throw new AuthError(
      `Could not reach the API: ${err instanceof Error ? err.message : String(err)}`,
    );
  }
  if (!response.ok) {
    let detail = `${response.status} ${response.statusText}`;
    try {
      const body: unknown = await response.json();
      if (typeof body === "object" && body !== null && typeof (body as { error?: unknown }).error === "string") {
        detail = (body as { error: string }).error;
      }
    } catch {
      // keep status text
    }
    throw new AuthError(`Sign-in failed: ${detail}`);
  }
  const body = (await response.json()) as Partial<DevLoginResponse>;
  if (typeof body.token !== "string" || body.token.length === 0) {
    throw new AuthError("Sign-in failed: the API did not return a token");
  }
  const session: Session = {
    token: body.token,
    role: request.role,
    subject: request.subject,
    accountId: typeof body.accountId === "string" ? body.accountId : null,
  };
  setSession(session);
  return session;
}
