// All authentication lives here. Today this is a development sign-in that
// exchanges {subject, role} for a JWT; in production this module is the only
// thing that should change when OIDC replaces it.

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
