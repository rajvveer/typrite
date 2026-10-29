import { useState, type FormEvent, type ReactNode } from "react";
import {
  CREATIVE_FORMATS,
  CREATIVE_MIME_TYPES,
  allowedTransitions,
  createCampaign,
  describeError,
  listCampaigns,
  transitionCampaign,
  uploadCreative,
  type Campaign,
  type CampaignTransition,
  type CreativeFormat,
} from "../api";
import { EmptyRow, ErrorText, Notice, formatUtc, useAsync } from "./shared";

const TRANSITION_LABEL: Record<CampaignTransition, string> = {
  approve: "Approve",
  activate: "Activate",
  pause: "Pause",
  end: "End",
};
const ALL_TRANSITIONS: readonly CampaignTransition[] = ["approve", "activate", "pause", "end"];

function toUtcIso(local: string): string {
  const date = new Date(local);
  return Number.isNaN(date.getTime()) ? local : date.toISOString();
}

function NewCampaignForm({ onCreated }: { onCreated: () => void }): ReactNode {
  const [name, setName] = useState<string>("");
  const [startsAt, setStartsAt] = useState<string>("");
  const [endsAt, setEndsAt] = useState<string>("");
  const [weight, setWeight] = useState<string>("1");
  const [busy, setBusy] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  async function onSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault();
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const result = await createCampaign({
        name: name.trim(),
        startsAtUtc: toUtcIso(startsAt),
        endsAtUtc: toUtcIso(endsAt),
        weight: Number(weight),
      });
      setNotice(`Created campaign ${result.id}`);
      setName("");
      onCreated();
    } catch (err) {
      setError(describeError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="panel" onSubmit={(e) => void onSubmit(e)}>
      <h2>New campaign</h2>
      <div className="field-row">
        <div className="field">
          <label htmlFor="campaign-name">Name</label>
          <input id="campaign-name" type="text" required value={name} onChange={(e) => setName(e.target.value)} />
        </div>
        <div className="field">
          <label htmlFor="campaign-starts">Starts at (local)</label>
          <input
            id="campaign-starts"
            type="datetime-local"
            required
            value={startsAt}
            onChange={(e) => setStartsAt(e.target.value)}
          />
        </div>
        <div className="field">
          <label htmlFor="campaign-ends">Ends at (local)</label>
          <input
            id="campaign-ends"
            type="datetime-local"
            required
            value={endsAt}
            onChange={(e) => setEndsAt(e.target.value)}
          />
        </div>
        <div className="field">
          <label htmlFor="campaign-weight">Weight</label>
          <input
            id="campaign-weight"
            type="number"
            min={0}
            step={1}
            required
            value={weight}
            onChange={(e) => setWeight(e.target.value)}
          />
        </div>
      </div>
      <ErrorText error={error} />
      <Notice text={notice} />
      <button type="submit" disabled={busy}>
        {busy ? "Creating…" : "Create campaign"}
      </button>
    </form>
  );
}

function CreativeUploadForm({ campaign }: { campaign: Campaign }): ReactNode {
  const [file, setFile] = useState<File | null>(null);
  const [format, setFormat] = useState<CreativeFormat>("400x225");
  const [durationMs, setDurationMs] = useState<string>("");
  const [clickThroughUrl, setClickThroughUrl] = useState<string>("");
  const [busy, setBusy] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const prefix = `creative-${campaign.id}`;

  async function onSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault();
    if (file === null) {
      setError("Choose a file to upload");
      return;
    }
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const req = {
        file,
        format,
        ...(durationMs.trim().length > 0 ? { durationMs: Number(durationMs) } : {}),
        ...(clickThroughUrl.trim().length > 0 ? { clickThroughUrl: clickThroughUrl.trim() } : {}),
      };
      const result = await uploadCreative(campaign.id, req);
      setNotice(`Uploaded creative ${result.id}`);
      setFile(null);
      (event.target as HTMLFormElement).reset();
    } catch (err) {
      setError(describeError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="creative-form" onSubmit={(e) => void onSubmit(e)}>
      <div className="field-row">
        <div className="field">
          <label htmlFor={`${prefix}-file`}>File</label>
          <input
            id={`${prefix}-file`}
            type="file"
            accept={CREATIVE_MIME_TYPES.join(",")}
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
          />
        </div>
        <div className="field">
          <label htmlFor={`${prefix}-format`}>Format</label>
          <select id={`${prefix}-format`} value={format} onChange={(e) => setFormat(e.target.value as CreativeFormat)}>
            {CREATIVE_FORMATS.map((f) => (
              <option key={f} value={f}>
                {f}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label htmlFor={`${prefix}-duration`}>Duration (ms)</label>
          <input
            id={`${prefix}-duration`}
            type="number"
            min={0}
            step={1}
            value={durationMs}
            onChange={(e) => setDurationMs(e.target.value)}
          />
        </div>
        <div className="field">
          <label htmlFor={`${prefix}-url`}>Click-through URL</label>
          <input
            id={`${prefix}-url`}
            type="url"
            value={clickThroughUrl}
            onChange={(e) => setClickThroughUrl(e.target.value)}
          />
        </div>
        <button type="submit" disabled={busy}>
          {busy ? "Uploading…" : "Upload creative"}
        </button>
      </div>
      <ErrorText error={error} />
      <Notice text={notice} />
    </form>
  );
}

function CampaignRows({ campaign, onChanged }: { campaign: Campaign; onChanged: () => void }): ReactNode {
  const [busy, setBusy] = useState<CampaignTransition | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const allowed = allowedTransitions(campaign.status);

  async function run(transition: CampaignTransition): Promise<void> {
    setBusy(transition);
    setError(null);
    setNotice(null);
    try {
      const result = await transitionCampaign(campaign.id, transition);
      setNotice(`Now ${result.status}`);
      onChanged();
    } catch (err) {
      setError(describeError(err));
    } finally {
      setBusy(null);
    }
  }

  return (
    <>
      <tr>
        <td className="mono">{campaign.id}</td>
        <td>{campaign.name}</td>
        <td>{campaign.status}</td>
        <td>{formatUtc(campaign.startsAtUtc)}</td>
        <td>{formatUtc(campaign.endsAtUtc)}</td>
        <td>{campaign.weight}</td>
        <td>
          <div className="inline-form">
            {ALL_TRANSITIONS.map((t) => (
              <button
                key={t}
                type="button"
                disabled={!allowed.includes(t) || busy !== null}
                onClick={() => void run(t)}
                aria-label={`${TRANSITION_LABEL[t]} ${campaign.name}`}
              >
                {busy === t ? `${TRANSITION_LABEL[t]}…` : TRANSITION_LABEL[t]}
              </button>
            ))}
          </div>
          <ErrorText error={error} />
          <Notice text={notice} />
        </td>
      </tr>
      <tr className="subrow">
        <td colSpan={7}>
          <CreativeUploadForm campaign={campaign} />
        </td>
      </tr>
    </>
  );
}

export function CampaignsPage(): ReactNode {
  const state = useAsync(() => listCampaigns(), []);
  const items = state.data?.items ?? [];

  return (
    <section>
      <div className="page-header">
        <h1>Campaigns</h1>
        <button type="button" onClick={state.reload} disabled={state.loading}>
          Refresh
        </button>
      </div>
      <NewCampaignForm onCreated={state.reload} />
      <ErrorText error={state.error} />
      <table>
        <thead>
          <tr>
            <th scope="col">ID</th>
            <th scope="col">Name</th>
            <th scope="col">Status</th>
            <th scope="col">Starts (UTC)</th>
            <th scope="col">Ends (UTC)</th>
            <th scope="col">Weight</th>
            <th scope="col">Lifecycle</th>
          </tr>
        </thead>
        <tbody>
          {items.length === 0 ? (
            <EmptyRow colSpan={7} text={state.loading ? "Loading…" : "No campaigns"} />
          ) : (
            items.map((c) => <CampaignRows key={c.id} campaign={c} onChanged={state.reload} />)
          )}
        </tbody>
      </table>
    </section>
  );
}
