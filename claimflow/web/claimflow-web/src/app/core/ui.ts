import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, Pipe, PipeTransform, signal } from '@angular/core';
import { ClaimStatus, Market, Sla } from './models';

// ── toasts ──
export interface Toast { id: number; kind: 'success' | 'error' | 'info'; text: string }

@Injectable({ providedIn: 'root' })
export class Toasts {
  readonly items = signal<Toast[]>([]);
  private n = 0;
  push(kind: Toast['kind'], text: string, ms = 4800) {
    const id = ++this.n;
    this.items.update(l => [...l, { id, kind, text }]);
    setTimeout(() => this.dismiss(id), ms);
  }
  success(t: string) { this.push('success', t); }
  error(t: string) { this.push('error', t, 7000); }
  info(t: string) { this.push('info', t); }
  dismiss(id: number) { this.items.update(l => l.filter(x => x.id !== id)); }
}

/** Pulls the human sentence out of the backend's RFC-7807 problem response. */
export function errorText(e: unknown): string {
  if (e instanceof HttpErrorResponse) {
    if (e.status === 0) return 'Cannot reach the server. Are all services running?';
    const body = e.error as { detail?: string } | null;
    return body?.detail ?? `Request failed (${e.status})`;
  }
  return e instanceof Error ? e.message : 'Something went wrong.';
}
export function errorCode(e: unknown): string | undefined {
  return e instanceof HttpErrorResponse ? (e.error as { code?: string } | null)?.code : undefined;
}

// ── labels ──
export const STATUS_LABEL: Record<ClaimStatus, string> = {
  Submitted: 'Submitted', UnderReview: 'Under review', AwaitingInformation: 'Awaiting info', Assessed: 'Assessed',
  Approved: 'Approved', Settled: 'Paid', Rejected: 'Declined', Withdrawn: 'Withdrawn',
};
export const STATUS_COLOR: Record<ClaimStatus, string> = {
  Submitted: '#3b82f6', UnderReview: '#7c5cf0', AwaitingInformation: '#f59e0b', Assessed: '#a855f7',
  Approved: '#10b981', Settled: '#14b8a6', Rejected: '#ef4444', Withdrawn: '#94a3b8',
};
export const MARKET_NAME: Record<Market, string> = { SG: 'Singapore', HK: 'Hong Kong', MY: 'Malaysia', TH: 'Thailand', ID: 'Indonesia', AU: 'Australia' };
export const MARKETS = Object.keys(MARKET_NAME) as Market[];
export const STATUSES = Object.keys(STATUS_LABEL) as ClaimStatus[];

// ── formatting ──
export function money(v: number | null | undefined, cur?: string | null, compact = false): string {
  if (v === null || v === undefined) return '—';
  const c = cur ? cur + ' ' : '';
  if (compact && Math.abs(v) >= 1_000_000) return c + new Intl.NumberFormat('en-US', { notation: 'compact', maximumFractionDigits: 1 }).format(v);
  return c + new Intl.NumberFormat('en-US', { minimumFractionDigits: Number.isInteger(v) ? 0 : 2, maximumFractionDigits: 2 }).format(v);
}
export function usd(v: number | null | undefined, compact = false): string { return money(v, 'USD', compact); }

export function dur(hours: number): string {
  const h = Math.abs(hours);
  if (h < 1) return `${Math.max(1, Math.round(h * 60))}m`;
  if (h < 48) return `${Math.round(h)}h`;
  return `${Math.floor(h / 24)}d ${Math.round(h % 24)}h`;
}
export function ago(iso?: string | null): string {
  if (!iso) return '—';
  const s = (Date.now() - new Date(iso).getTime()) / 1000;
  if (s < 45) return 'just now';
  if (s < 3600) return `${Math.round(s / 60)}m ago`;
  if (s < 86400) return `${Math.round(s / 3600)}h ago`;
  if (s < 86400 * 14) return `${Math.round(s / 86400)}d ago`;
  return new Date(iso).toLocaleDateString('en-GB', { day: '2-digit', month: 'short' });
}
export function dateTime(iso?: string | null): string {
  return iso ? new Date(iso).toLocaleString('en-GB', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '—';
}
export function dateOnly(iso?: string | null): string {
  return iso ? new Date(iso).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' }) : '—';
}
export function slaText(s: Sla): string {
  if (!s.dueAt || s.state === 'None') return '';
  const hours = (new Date(s.dueAt).getTime() - Date.now()) / 3_600_000;
  return hours < 0 ? `${s.stage} overdue by ${dur(hours)}` : `${s.stage} due in ${dur(hours)}`;
}
export function initials(name: string): string { return name.split(/\s+/).map(p => p[0]).slice(0, 2).join('').toUpperCase(); }
export function hue(name: string): number { let h = 0; for (const c of name) h = (h * 31 + c.charCodeAt(0)) % 360; return h; }

@Pipe({ name: 'money' }) export class MoneyPipe implements PipeTransform {
  transform(v: number | null | undefined, cur?: string | null, compact = false) { return money(v, cur, compact); }
}
@Pipe({ name: 'ago' }) export class AgoPipe implements PipeTransform {
  transform(v?: string | null) { return ago(v); }
}
@Pipe({ name: 'dt' }) export class DatePipe2 implements PipeTransform {
  transform(v?: string | null) { return dateTime(v); }
}
@Pipe({ name: 'd' }) export class DayPipe implements PipeTransform {
  transform(v?: string | null) { return dateOnly(v); }
}
