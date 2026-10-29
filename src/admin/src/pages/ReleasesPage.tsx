import { useState, type FormEvent, type ReactNode } from "react";
import {
  createRelease,
  describeError,
  listReleases,
  rollbackRelease,
  setRollout,
  type Release,
} from "../api";
import { EmptyRow, ErrorText, Notice, formatUtc, useAsync } from "./shared";

const ARCHITECTURES = ["x64", "arm64"] as const;

function NewReleaseForm({ onCreated }: { onCreated: () => void }): ReactNode {
  const [version, setVersion] = useState<string>("");
  const [cpuArchitecture, setCpuArchitecture] = useState<string>("x64");
  const [packageSha256, setPackageSha256] = useState<string>("");
  const [packageUrl, setPackageUrl] = useState<string>("");
  const [manifestSignature, setManifestSignature] = useState<string>("");
  const [busy, setBusy] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  async function onSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault();
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const result = await createRelease({
        version: version.trim(),
        cpuArchitecture,
        packageSha256: packageSha256.trim(),
        packageUrl: packageUrl.trim(),
        manifestSignature: manifestSignature.trim(),
      });
      setNotice(`Created release ${result.id}`);
      setVersion("");
      setPackageSha256("");
      setPackageUrl("");
      setManifestSignature("");
      onCreated();
    } catch (err) {
      setError(describeError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="panel" onSubmit={(e) => void onSubmit(e)}>
      <h2>New release</h2>
      <div className="field-row">
        <div className="field">
          <label htmlFor="release-version">Version</label>
          <input id="release-version" type="text" required value={version} onChange={(e) => setVersion(e.target.value)} />
        </div>
        <div className="field">
          <label htmlFor="release-arch">CPU architecture</label>
          <select id="release-arch" value={cpuArchitecture} onChange={(e) => setCpuArchitecture(e.target.value)}>
            {ARCHITECTURES.map((a) => (
              <option key={a} value={a}>
                {a}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label htmlFor="release-sha">Package SHA-256</label>
          <input
            id="release-sha"
            type="text"
            required
            pattern="[A-Fa-f0-9]{64}"
            value={packageSha256}
            onChange={(e) => setPackageSha256(e.target.value)}
          />
        </div>
        <div className="field">
          <label htmlFor="release-url">Package URL</label>
          <input id="release-url" type="url" required value={packageUrl} onChange={(e) => setPackageUrl(e.target.value)} />
        </div>
        <div className="field">
          <label htmlFor="release-signature">Manifest signature</label>
          <input
            id="release-signature"
            type="text"
            required
            value={manifestSignature}
            onChange={(e) => setManifestSignature(e.target.value)}
          />
        </div>
      </div>
      <ErrorText error={error} />
      <Notice text={notice} />
      <button type="submit" disabled={busy}>
        {busy ? "Creating…" : "Create release"}
      </button>
    </form>
  );
}

function ReleaseRow({ release, onChanged }: { release: Release; onChanged: () => void }): ReactNode {
  const [percent, setPercent] = useState<string>(String(release.rolloutPercent));
  const [busy, setBusy] = useState<"rollout" | "rollback" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  async function applyRollout(): Promise<void> {
    const value = Number(percent);
    if (!Number.isInteger(value) || value < 0 || value > 100) {
      setError("Rollout percent must be a whole number between 0 and 100");
      return;
    }
    setBusy("rollout");
    setError(null);
    setNotice(null);
    try {
      await setRollout(release.id, value);
      setNotice(`Rollout set to ${value}%`);
      onChanged();
    } catch (err) {
      setError(describeError(err));
    } finally {
      setBusy(null);
    }
  }

  async function rollback(): Promise<void> {
    setBusy("rollback");
    setError(null);
    setNotice(null);
    try {
      await rollbackRelease(release.id);
      setNotice("Rolled back");
      onChanged();
    } catch (err) {
      setError(describeError(err));
    } finally {
      setBusy(null);
    }
  }

  const inputId = `rollout-${release.id}`;
  return (
    <tr>
      <td className="mono">{release.id}</td>
      <td>{release.version}</td>
      <td>{release.cpuArchitecture}</td>
      <td>{release.status}</td>
      <td>{release.rolloutPercent}%</td>
      <td>{formatUtc(release.createdAtUtc)}</td>
      <td>
        <div className="inline-form">
          <label htmlFor={inputId}>Rollout %</label>
          <input
            id={inputId}
            type="number"
            min={0}
            max={100}
            step={1}
            value={percent}
            onChange={(e) => setPercent(e.target.value)}
            className="narrow"
          />
          <button type="button" onClick={() => void applyRollout()} disabled={busy !== null}>
            {busy === "rollout" ? "Applying…" : "Apply rollout"}
          </button>
          <button type="button" className="danger" onClick={() => void rollback()} disabled={busy !== null}>
            {busy === "rollback" ? "Rolling back…" : "Rollback"}
          </button>
        </div>
        <ErrorText error={error} />
        <Notice text={notice} />
      </td>
    </tr>
  );
}

export function ReleasesPage(): ReactNode {
  const state = useAsync(() => listReleases(), []);
  const items = state.data?.items ?? [];

  return (
    <section>
      <div className="page-header">
        <h1>Releases</h1>
        <button type="button" onClick={state.reload} disabled={state.loading}>
          Refresh
        </button>
      </div>
      <NewReleaseForm onCreated={state.reload} />
      <ErrorText error={state.error} />
      <table>
        <thead>
          <tr>
            <th scope="col">ID</th>
            <th scope="col">Version</th>
            <th scope="col">CPU</th>
            <th scope="col">Status</th>
            <th scope="col">Rollout</th>
            <th scope="col">Created (UTC)</th>
            <th scope="col">Actions</th>
          </tr>
        </thead>
        <tbody>
          {items.length === 0 ? (
            <EmptyRow colSpan={7} text={state.loading ? "Loading…" : "No releases"} />
          ) : (
            items.map((r) => <ReleaseRow key={r.id} release={r} onChanged={state.reload} />)
          )}
        </tbody>
      </table>
    </section>
  );
}
