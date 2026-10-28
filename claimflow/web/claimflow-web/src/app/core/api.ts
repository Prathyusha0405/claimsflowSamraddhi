import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import {
  ClaimDetail, ClaimStatus, ClaimSummary, ClaimType, DemoUsers, ExposureReport, LoginResponse, Market, NotificationItem,
  OfficerWorkload, Overview, Paged, Policy, Priority, RecentEvent, SlaBreach, StatusCount, SubmitClaimBody, SystemHealth, TrendPoint, User,
} from './models';

type Query = Record<string, string | number | boolean | null | undefined>;

function params(q?: Query): HttpParams {
  let p = new HttpParams();
  for (const [k, v] of Object.entries(q ?? {})) if (v !== null && v !== undefined && v !== '') p = p.set(k, String(v));
  return p;
}

/** Thin typed wrapper over the gateway. Every URL is /api/<service>/... (proxied to :5100 in dev). */
@Injectable({ providedIn: 'root' })
export class Api {
  private http = inject(HttpClient);
  private base = '/api';

  // identity
  login(email: string, password: string) { return this.http.post<LoginResponse>(`${this.base}/identity/login`, { email, password }); }
  demoUsers() { return this.http.get<DemoUsers>(`${this.base}/identity/demo-users`); }
  staff() { return this.http.get<User[]>(`${this.base}/identity/staff`); }

  // policies
  myPolicies() { return this.http.get<Policy[]>(`${this.base}/policies/mine`); }

  // claims — claimant
  submitClaim(body: SubmitClaimBody) { return this.http.post<ClaimDetail>(`${this.base}/claims`, body); }
  myClaims() { return this.http.get<ClaimSummary[]>(`${this.base}/claims/mine`); }
  respond(claimId: string, requestId: string, response: string) {
    return this.http.post<ClaimDetail>(`${this.base}/claims/${claimId}/information-requests/${requestId}/respond`, { response });
  }
  withdraw(claimId: string, reason?: string) { return this.http.post<ClaimDetail>(`${this.base}/claims/${claimId}/withdraw`, { reason }); }

  // claims — staff
  queue(f: { market?: Market | ''; type?: ClaimType | ''; priority?: Priority | '' } = {}) {
    return this.http.get<ClaimSummary[]>(`${this.base}/claims/queue`, { params: params(f) });
  }
  myWork() { return this.http.get<ClaimSummary[]>(`${this.base}/claims/my-work`); }
  search(f: { status?: ClaimStatus | ''; officerId?: string; market?: Market | ''; type?: ClaimType | ''; q?: string; page?: number; pageSize?: number }) {
    return this.http.get<Paged<ClaimSummary>>(`${this.base}/claims`, { params: params(f) });
  }
  claim(id: string) { return this.http.get<ClaimDetail>(`${this.base}/claims/${id}`); }
  pickup(id: string) { return this.http.post<ClaimDetail>(`${this.base}/claims/${id}/pickup`, {}); }
  assign(id: string, officerId: string) { return this.http.post<ClaimDetail>(`${this.base}/claims/${id}/assign`, { officerId }); }
  requestInfo(id: string, question: string) { return this.http.post<ClaimDetail>(`${this.base}/claims/${id}/request-information`, { question }); }
  assess(id: string, assessedLoss: number, coveredPercent: number, notes: string) {
    return this.http.post<ClaimDetail>(`${this.base}/claims/${id}/assess`, { assessedLoss, coveredPercent, notes });
  }
  approve(id: string, notes: string) { return this.http.post<ClaimDetail>(`${this.base}/claims/${id}/approve`, { notes }); }
  reject(id: string, reason: string) { return this.http.post<ClaimDetail>(`${this.base}/claims/${id}/reject`, { reason }); }
  settle(id: string, paymentReference: string) { return this.http.post<ClaimDetail>(`${this.base}/claims/${id}/settle`, { paymentReference }); }
  addNote(id: string, text: string) { return this.http.post<ClaimDetail>(`${this.base}/claims/${id}/notes`, { text }); }

  // notifications
  notifications(limit = 15) { return this.http.get<NotificationItem[]>(`${this.base}/notifications`, { params: params({ limit }) }); }
  markRead(id: string) { return this.http.post<NotificationItem>(`${this.base}/notifications/${id}/read`, {}); }
  markAllRead() { return this.http.post<{ marked: number }>(`${this.base}/notifications/read-all`, {}); }

  // reporting
  overview() { return this.http.get<Overview>(`${this.base}/reports/overview`); }
  exposure() { return this.http.get<ExposureReport>(`${this.base}/reports/exposure`); }
  statusBreakdown() { return this.http.get<StatusCount[]>(`${this.base}/reports/status-breakdown`); }
  workload() { return this.http.get<OfficerWorkload[]>(`${this.base}/reports/workload`); }
  trend(days = 14) { return this.http.get<TrendPoint[]>(`${this.base}/reports/trend`, { params: params({ days }) }); }
  slaBreaches() { return this.http.get<SlaBreach[]>(`${this.base}/reports/sla-breaches`); }
  recentEvents(limit = 14) { return this.http.get<RecentEvent[]>(`${this.base}/reports/events/recent`, { params: params({ limit }) }); }

  // system
  health() { return this.http.get<SystemHealth>(`${this.base}/system/health`); }
}
