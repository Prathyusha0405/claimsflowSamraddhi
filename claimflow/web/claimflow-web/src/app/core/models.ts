// Typed mirrors of the backend DTOs (camelCase JSON, enums as strings).
export type Role = 'Claimant' | 'ClaimsOfficer' | 'Manager';
export type ClaimStatus = 'Submitted' | 'UnderReview' | 'AwaitingInformation' | 'Assessed' | 'Approved' | 'Rejected' | 'Settled' | 'Withdrawn';
export type Priority = 'Normal' | 'High' | 'Urgent';
export type ClaimType = 'Motor' | 'Property';
export type Market = 'SG' | 'HK' | 'MY' | 'TH' | 'ID' | 'AU';
export type SlaState = 'None' | 'OnTrack' | 'DueSoon' | 'Breached';

export interface User {
  id: string; email: string; name: string; role: Role; market?: Market | null; currency?: string | null;
  authorityLimitUsd: number; blurb: string;
}
export interface LoginResponse { token: string; expiresAt: string; user: User }
export interface DemoUsers { password: string; users: User[] }

export interface Sla { state: SlaState; dueAt?: string | null; stage?: string | null }

export interface ClaimSummary {
  id: string; claimNumber: string; status: ClaimStatus; priority: Priority; type: ClaimType; market: Market; currency: string;
  claimantId: string; claimantName: string; claimedAmount: number; reserve: number; reserveUsd: number;
  assignedOfficerId?: string | null; assignedOfficerName?: string | null; submittedAt: string; updatedAt: string;
  incidentDate: string; ageHours: number; sla: Sla; awaitingClaimant: boolean; needsManagerApproval: boolean;
}
export interface Paged<T> { items: T[]; total: number; page: number; pageSize: number }

export interface PolicyTerms {
  productName: string; currency: string; coverageLimit: number; deductible: number;
  effectiveFrom: string; effectiveTo: string; assetDescription: string;
}
export interface Policy {
  policyNumber: string; claimantId: string; claimantName: string; market: Market; product: ClaimType; productName: string;
  currency: string; coverageLimit: number; deductible: number; effectiveFrom: string; effectiveTo: string;
  status: string; inForce: boolean; assetDescription: string; vehicleRegistration?: string | null; propertyAddress?: string | null;
}

export interface MotorDetails { vehicleRegistration?: string | null; thirdPartyInvolved: boolean; injuryInvolved: boolean; policeReportNumber?: string | null; drivable: boolean }
export interface PropertyDetails { propertyAddress?: string | null; damageCause: string; habitable: boolean }
export interface InformationRequest { id: string; question: string; requestedById: string; requestedByName: string; requestedAt: string; response?: string | null; respondedAt?: string | null }
export interface ClaimNote { id: string; at: string; authorId: string; authorName: string; text: string }
export interface TimelineEntry { at: string; actorId: string; actorName: string; actorRole: string; action: string; from?: ClaimStatus | null; to: ClaimStatus; message: string }
export interface Assessment {
  assessedLoss: number; coveredPercent: number; deductibleApplied: number; coverageLimit: number; payableAmount: number;
  requiresManagerApproval: boolean; notes?: string | null; assessedById: string; assessedByName: string; assessedAt: string;
}
export interface Decision { approved: boolean; notes?: string | null; reason?: string | null; amount: number; decidedById: string; decidedByName: string; decidedAt: string }
export interface Settlement { paymentReference: string; paidAmount: number; settledById: string; settledByName: string; settledAt: string }

export interface ClaimDetail {
  id: string; claimNumber: string; version: number; status: ClaimStatus; priority: Priority; type: ClaimType; market: Market;
  policyNumber: string; policy: PolicyTerms; claimantId: string; claimantName: string; incidentDate: string; incidentLocation: string;
  description: string; currency: string; claimedAmount: number; reserve: number; reserveUsd: number; usdRate: number;
  motor?: MotorDetails | null; property?: PropertyDetails | null; assignedOfficerId?: string | null; assignedOfficerName?: string | null;
  assessment?: Assessment | null; decision?: Decision | null; settlement?: Settlement | null;
  informationRequests: InformationRequest[]; timeline: TimelineEntry[]; notes?: ClaimNote[] | null;
  submittedAt: string; updatedAt: string; sla: Sla; allowedActions: string[]; actionHints: Record<string, string>;
}

export interface SubmitClaimBody {
  policyNumber: string; type: ClaimType; incidentDate: string; incidentLocation: string; description: string; claimedAmount: number;
  motor?: MotorDetails | null; property?: PropertyDetails | null;
}

export interface NotificationItem { id: string; title: string; body: string; kind: 'info' | 'action' | 'success' | 'warning' | 'danger'; claimId: string; claimNumber: string; createdAt: string; read: boolean }

// ── reporting ──
export interface Overview {
  asOf: string; totalClaims: number; openClaims: number; unassigned: number; awaitingInformation: number; awaitingApproval: number;
  approvedPendingPayment: number; exposureUsd: number; slaBreaches: number; avgHoursToFirstAssignment?: number | null;
  avgDaysToDecision?: number | null; approvalRate?: number | null; paidLast30dUsd: number; decidedLast30d: number;
}
export interface ExposureRow { key: string; label: string; currency?: string | null; count: number; reserve: number; reserveUsd: number }
export interface ExposureReport { totalUsd: number; byMarket: ExposureRow[]; byType: ExposureRow[]; byStatus: ExposureRow[] }
export interface StatusCount { status: ClaimStatus; count: number }
export interface OfficerWorkload {
  officerId: string; officerName: string; open: number; underReview: number; awaitingInformation: number; assessed: number;
  approvedPendingPayment: number; exposureUsd: number; slaBreaches: number; oldestOpenHours?: number | null;
  decidedLast30d: number; approvalRate?: number | null; avgDaysToDecision?: number | null;
}
export interface TrendPoint { date: string; submitted: number; decided: number; settled: number; paidUsd: number }
export interface SlaBreach {
  claimId: string; claimNumber: string; status: ClaimStatus; priority: Priority; market: Market; officerName?: string | null;
  stage: string; dueAt: string; overdueHours: number; reserveUsd: number;
}
export interface RecentEvent { id: string; eventType: string; claimNumber: string; summary: string; market: Market; status: ClaimStatus; version: number; occurredAt: string; receivedAt: string }

export interface ServiceHealth {
  name: string; url: string; status: 'Healthy' | 'Unhealthy' | 'Down'; latencyMs: number;
  details?: {
    service: string; uptimeSeconds: number; startedAt: string;
    checks?: { messaging?: { provider: string; transport: string; outboxPending: number; published: number; consumed: number; ignored: number; failed: number; lastError?: string | null } };
  } | null;
}
export interface SystemHealth { gateway: { status: string; uptimeSeconds: number }; messagingProvider: string; services: ServiceHealth[] }
