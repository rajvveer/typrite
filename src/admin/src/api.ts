import { clearSession, getToken } from "./auth";
import { navigate } from "./router";

// ---------- Error type ----------

export class ApiError extends Error {
  readonly status: number;
  readonly problems: readonly string[];

  constructor(status: number, message: string, problems: readonly string[] = []) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.problems = problems;
  }
}

export function describeError(err: unknown): string {
  if (err instanceof ApiError) {
    return err.problems.length > 0 ? `${err.message}: ${err.problems.join("; ")}` : err.message;
  }
  if (err instanceof Error) return err.message;
  return String(err);
}

// ---------- Core request ----------

interface ErrorBody {
  error?: unknown;
  problems?: unknown;
  title?: unknown;
  detail?: unknown;
}

async function readErrorMessage(response: Response): Promise<ApiError> {
  if (response.status === 403) {
    return new ApiError(403, "Not permitted for your role");
  }
  let message = `${response.status} ${response.statusText}`.trim();
  let problems: string[] = [];
  try {
    const text = await response.text();
    if (text.length > 0) {
      const body = JSON.parse(text) as ErrorBody;
      if (typeof body.error === "string") message = body.error;
      else if (typeof body.detail === "string") message = body.detail;
      else if (typeof body.title === "string") message = body.title;
      if (Array.isArray(body.problems)) {
        problems = body.problems.filter((p): p is string => typeof p === "string");
      }
    }
  } catch {
    // non-JSON body; keep the status text
  }
  return new ApiError(response.status, message, problems);
}

async function request<T>(method: string, path: string, body?: BodyInit, contentType?: string): Promise<T> {
  const headers: Record<string, string> = { Accept: "application/json" };
  const token = getToken();
  if (token !== null) headers.Authorization = `Bearer ${token}`;
  if (contentType !== undefined) headers["Content-Type"] = contentType;

  let response: Response;
  try {
    response = await fetch(path, { method, headers, body: body ?? null });
  } catch (err) {
    throw new ApiError(0, `Could not reach the API: ${err instanceof Error ? err.message : String(err)}`);
  }

  if (response.status === 401) {
    clearSession();
    navigate("login");
    throw new ApiError(401, "Your session has expired; please sign in again");
  }
  if (!response.ok) {
    throw await readErrorMessage(response);
  }
  if (response.status === 204) {
    return undefined as T;
  }
  const text = await response.text();
  if (text.length === 0) return undefined as T;
  return JSON.parse(text) as T;
}

function get<T>(path: string): Promise<T> {
  return request<T>("GET", path);
}

function postJson<T>(path: string, body: unknown): Promise<T> {
  return request<T>("POST", path, JSON.stringify(body), "application/json");
}

function postForm<T>(path: string, form: FormData): Promise<T> {
  return request<T>("POST", path, form);
}

// ---------- Devices ----------

export interface Device {
  id: string;
  status: string;
  appVersion: string;
  cpuArchitecture: string;
  lastSeenUtc: string | null;
  enrolledAtUtc: string;
  credentialKind: string;
  configurationVersion: number | string | null;
}

export interface DevicePage {
  items: Device[];
  nextCursor: string | null;
}

export const DEVICE_COMMAND_TYPES = [
  "refresh-configuration",
  "enter-safe-mode",
  "leave-safe-mode",
  "upload-diagnostics",
  "restart-bar",
] as const;
export type DeviceCommandType = (typeof DEVICE_COMMAND_TYPES)[number];

export interface DeviceCommandRequest {
  type: DeviceCommandType;
  payload?: unknown;
  ttlMinutes?: number;
}

export interface IdResponse {
  id: string;
}

export function listDevices(cursor: string | null, pageSize = 50): Promise<DevicePage> {
  const params = new URLSearchParams({ pageSize: String(pageSize) });
  if (cursor !== null) params.set("cursor", cursor);
  return get<DevicePage>(`/api/v1/admin/devices?${params.toString()}`);
}

export function sendDeviceCommand(deviceId: string, command: DeviceCommandRequest): Promise<IdResponse> {
  return postJson<IdResponse>(`/api/v1/admin/devices/${encodeURIComponent(deviceId)}/commands`, command);
}

// ---------- Campaigns ----------

export const CAMPAIGN_STATUSES = ["Draft", "Approved", "Active", "Paused", "Ended"] as const;
export type CampaignStatus = (typeof CAMPAIGN_STATUSES)[number];

export interface Campaign {
  id: string;
  name: string;
  status: CampaignStatus | string;
  startsAtUtc: string;
  endsAtUtc: string;
  weight: number;
}

export interface CampaignCreateRequest {
  name: string;
  startsAtUtc: string;
  endsAtUtc: string;
  weight: number;
}

export const CREATIVE_FORMATS = ["400x225", "384x216", "300x250"] as const;
export type CreativeFormat = (typeof CREATIVE_FORMATS)[number];

export const CREATIVE_MIME_TYPES = ["image/png", "image/jpeg", "image/webp", "video/mp4"] as const;

export interface CreativeUploadRequest {
  file: File;
  format: CreativeFormat;
  durationMs?: number;
  clickThroughUrl?: string;
}

export type CampaignTransition = "approve" | "activate" | "pause" | "end";

export interface CampaignStatusResponse {
  id: string;
  status: CampaignStatus | string;
}

export function allowedTransitions(status: string): CampaignTransition[] {
  switch (status) {
    case "Draft":
      return ["approve"];
    case "Approved":
      return ["activate", "end"];
    case "Active":
      return ["pause", "end"];
    case "Paused":
      return ["activate", "end"];
    default:
      return [];
  }
}

export function listCampaigns(): Promise<{ items: Campaign[] }> {
  return get<{ items: Campaign[] }>("/api/v1/admin/campaigns");
}

export function createCampaign(body: CampaignCreateRequest): Promise<IdResponse> {
  return postJson<IdResponse>("/api/v1/admin/campaigns", body);
}

export function uploadCreative(campaignId: string, req: CreativeUploadRequest): Promise<IdResponse> {
  const form = new FormData();
  form.append("file", req.file, req.file.name);
  form.append("format", req.format);
  if (req.durationMs !== undefined) form.append("durationMs", String(req.durationMs));
  if (req.clickThroughUrl !== undefined && req.clickThroughUrl.length > 0) {
    form.append("clickThroughUrl", req.clickThroughUrl);
  }
  return postForm<IdResponse>(`/api/v1/admin/campaigns/${encodeURIComponent(campaignId)}/creatives`, form);
}

export function transitionCampaign(campaignId: string, transition: CampaignTransition): Promise<CampaignStatusResponse> {
  return postJson<CampaignStatusResponse>(
    `/api/v1/admin/campaigns/${encodeURIComponent(campaignId)}/${transition}`,
    {},
  );
}

// ---------- Releases ----------

export interface Release {
  id: string;
  version: string;
  cpuArchitecture: string;
  status: string;
  rolloutPercent: number;
  createdAtUtc: string;
}

export interface ReleaseCreateRequest {
  version: string;
  cpuArchitecture: string;
  packageSha256: string;
  packageUrl: string;
  manifestSignature: string;
}

export function listReleases(): Promise<{ items: Release[] }> {
  return get<{ items: Release[] }>("/api/v1/admin/releases");
}

export function createRelease(body: ReleaseCreateRequest): Promise<IdResponse> {
  return postJson<IdResponse>("/api/v1/admin/releases", body);
}

export function setRollout(releaseId: string, percent: number): Promise<unknown> {
  return postJson<unknown>(`/api/v1/admin/releases/${encodeURIComponent(releaseId)}/rollout`, { percent });
}

export function rollbackRelease(releaseId: string): Promise<unknown> {
  return postJson<unknown>(`/api/v1/admin/releases/${encodeURIComponent(releaseId)}/rollback`, {});
}

// ---------- Reports ----------

export interface DeliveryRow {
  day: string;
  campaignId: string;
  creativeId: string;
  type: string;
  count: number;
  distinctDevices: number;
  visibleMilliseconds: number;
}

export interface DeliveryReport {
  from: string;
  to: string;
  items: DeliveryRow[];
}

export interface ReconciliationRun {
  id: string;
  partnerId: string;
  periodStart: string;
  periodEnd: string;
  status: string;
  ourCount: number;
  partnerCount: number;
  mismatchSummaryJson: string | null;
  startedAtUtc: string | null;
  finishedAtUtc: string | null;
}

export function getDeliveryReport(from: string, to: string): Promise<DeliveryReport> {
  const params = new URLSearchParams({ from, to });
  return get<DeliveryReport>(`/api/v1/admin/reports/delivery?${params.toString()}`);
}

export function getReconciliation(): Promise<{ items: ReconciliationRun[] }> {
  return get<{ items: ReconciliationRun[] }>("/api/v1/admin/reports/reconciliation");
}

// ---------- Audit ----------

export interface AuditEntry {
  actor: string;
  action: string;
  resource: string;
  timeUtc: string;
  metadataJson: string | null;
}

export function listAudit(resource: string, take = 100): Promise<{ items: AuditEntry[] }> {
  const params = new URLSearchParams({ take: String(take) });
  if (resource.trim().length > 0) params.set("resource", resource.trim());
  return get<{ items: AuditEntry[] }>(`/api/v1/admin/audit?${params.toString()}`);
}
