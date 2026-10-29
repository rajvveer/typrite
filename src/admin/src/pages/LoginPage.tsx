import { useState, type FormEvent, type ReactNode } from "react";
import { ROLES, devLogin, type Role } from "../auth";
import { describeError } from "../api";
import { navigate } from "../router";
import { ErrorText } from "./shared";

export function LoginPage(): ReactNode {
  const [subject, setSubject] = useState<string>("");
  const [role, setRole] = useState<Role>("Viewer");
  const [accountId, setAccountId] = useState<string>("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<boolean>(false);

  async function onSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const trimmedAccount = accountId.trim();
      await devLogin(
        trimmedAccount.length > 0
          ? { subject: subject.trim(), role, accountId: trimmedAccount }
          : { subject: subject.trim(), role },
      );
      navigate("devices");
    } catch (err) {
      setError(describeError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="login">
      <h1>Development sign-in</h1>
      <p className="muted">
        Exchanges a subject and role for a development JWT. This form will be replaced by OIDC in production.
      </p>
      <form onSubmit={(e) => void onSubmit(e)} aria-describedby={error !== null ? "login-error" : undefined}>
        <div className="field">
          <label htmlFor="login-subject">Subject</label>
          <input
            id="login-subject"
            name="subject"
            type="text"
            required
            autoComplete="username"
            value={subject}
            onChange={(e) => setSubject(e.target.value)}
          />
        </div>
        <div className="field">
          <label htmlFor="login-role">Role</label>
          <select id="login-role" name="role" value={role} onChange={(e) => setRole(e.target.value as Role)}>
            {ROLES.map((r) => (
              <option key={r} value={r}>
                {r}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label htmlFor="login-account">Account ID (optional)</label>
          <input
            id="login-account"
            name="accountId"
            type="text"
            value={accountId}
            onChange={(e) => setAccountId(e.target.value)}
          />
        </div>
        <ErrorText error={error} id="login-error" />
        <button type="submit" disabled={busy}>
          {busy ? "Signing in…" : "Sign in"}
        </button>
      </form>
    </section>
  );
}
