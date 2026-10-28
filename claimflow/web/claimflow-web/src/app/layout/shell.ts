import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { IsActiveMatchOptions, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Api } from '../core/api';
import { Auth } from '../core/auth';
import { NotificationItem } from '../core/models';
import { AgoPipe, Toasts } from '../core/ui';
import { Icon } from '../shared/icon';
import { Avatar } from '../shared/widgets';

interface NavItem { label: string; icon: string; link: string; query?: Record<string, string>; opts: IsActiveMatchOptions }

const SUBSET: IsActiveMatchOptions = { paths: 'subset', queryParams: 'exact', fragment: 'ignored', matrixParams: 'ignored' };
const EXACT: IsActiveMatchOptions = { paths: 'exact', queryParams: 'exact', fragment: 'ignored', matrixParams: 'ignored' };

@Component({
  selector: 'cf-toasts',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stack" aria-live="polite">
      @for (t of toasts.items(); track t.id) {
        <div [class]="'toast ' + t.kind" (click)="toasts.dismiss(t.id)">
          <cf-icon [name]="t.kind === 'success' ? 'check-circle' : t.kind === 'error' ? 'alert' : 'info'" [size]="20" />
          <span>{{ t.text }}</span>
        </div>
      }
    </div>`,
  styles: [`
    .stack{position:fixed;right:20px;bottom:20px;display:flex;flex-direction:column;gap:10px;z-index:300;max-width:min(420px,calc(100vw - 40px))}
    .toast{display:flex;gap:12px;align-items:flex-start;padding:14px 16px;border-radius:12px;background:#0f1b3d;color:#fff;box-shadow:0 14px 34px rgba(15,27,61,.3);cursor:pointer;animation:fadeUp .3s both;font-weight:500}
    .toast cf-icon{margin-top:1px}.success{background:#065f46}.error{background:#991b1b}.info{background:#1e3a8a}
  `],
})
export class ToastHost { protected readonly toasts = inject(Toasts); }

@Component({
  selector: 'cf-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Icon, Avatar, AgoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="shell">
      <aside class="side">
        <a class="brand" routerLink="/">
          <span class="logo"><cf-icon name="logo" [size]="22" /></span>
          <span class="bt"><b>ClaimFlow</b><small>APAC claims platform</small></span>
        </a>

        <nav>
          <div class="grp">{{ groupLabel() }}</div>
          @for (n of nav(); track n.label) {
            <a class="item" [routerLink]="n.link" [queryParams]="n.query" routerLinkActive="on" [routerLinkActiveOptions]="n.opts">
              <cf-icon [name]="n.icon" /><span class="lb">{{ n.label }}</span>
            </a>
          }
          <div class="grp">Platform</div>
          <a class="item" routerLink="/system" routerLinkActive="on"><cf-icon name="server" /><span class="lb">System &amp; events</span></a>
        </nav>

        <div class="me">
          <cf-avatar [name]="user()?.name ?? '?'" [size]="38" />
          <div class="mi"><b>{{ user()?.name }}</b><small>{{ roleLabel() }}</small></div>
          <button class="out" type="button" (click)="auth.logout()" title="Sign out" aria-label="Sign out"><cf-icon name="logout" [size]="18" /></button>
        </div>
      </aside>

      <div class="main">
        <header class="top">
          <span class="grow"></span>
          <span class="chip" [class.chip-ok]="mode() === 'Kafka'" [class.chip-brand]="mode() === 'Http'"
                [title]="mode() === 'Kafka' ? 'Events flow through Apache Kafka (topic claims.events)' : 'Events flow by direct HTTP fan-out from the outbox (no broker needed)'">
            <cf-icon name="radio" [size]="14" /> Events · {{ mode() === 'Kafka' ? 'Kafka' : 'HTTP' }}
          </span>
          <button class="bell" type="button" (click)="panel.set(!panel())" aria-label="Notifications">
            <cf-icon name="bell" [size]="20" />
            @if (unread() > 0) { <span class="cnt">{{ unread() > 9 ? '9+' : unread() }}</span> }
          </button>
        </header>
        <main class="content"><router-outlet /></main>
      </div>

      @if (panel()) {
        <div class="overlay" (click)="panel.set(false)"></div>
        <section class="notif">
          <header>
            <h3>Notifications</h3>
            <button class="btn btn-ghost btn-sm" type="button" (click)="readAll()" [disabled]="unread() === 0">Mark all read</button>
          </header>
          <div class="nl">
            @for (n of items(); track n.id) {
              <button type="button" class="ni" [class.unread]="!n.read" (click)="open(n)">
                <span [class]="'nk ' + n.kind"><cf-icon [name]="icon(n.kind)" [size]="16" /></span>
                <span class="nt"><b>{{ n.title }}</b><span>{{ n.body }}</span><small>{{ n.claimNumber }} · {{ n.createdAt | ago }}</small></span>
              </button>
            } @empty {
              <p class="muted center" style="padding:36px 16px">You're all caught up.</p>
            }
          </div>
        </section>
      }
    </div>`,
  styles: [`
    .shell{display:flex;min-height:100vh}
    .side{width:256px;background:linear-gradient(180deg,#0b1437,#101c4d);color:#c4cbe6;display:flex;flex-direction:column;padding:20px 14px;position:sticky;top:0;height:100vh;flex:none}
    .brand{display:flex;gap:12px;align-items:center;color:#fff;padding:4px 8px 22px;text-decoration:none!important}
    .logo{width:40px;height:40px;border-radius:12px;background:linear-gradient(135deg,#6366f1,#a855f7);display:flex;align-items:center;justify-content:center;box-shadow:0 8px 20px rgba(99,102,241,.45)}
    .bt{display:flex;flex-direction:column;line-height:1.15}.bt b{font-size:17px}.bt small{color:#8f9bd0;font-size:11.5px}
    nav{display:flex;flex-direction:column;gap:3px;flex:1;overflow:auto}
    .grp{font-size:11px;text-transform:uppercase;letter-spacing:.1em;color:#6b78b4;padding:16px 12px 6px;font-weight:700}
    .item{display:flex;align-items:center;gap:12px;padding:10px 12px;border-radius:10px;color:#c4cbe6;font-weight:600;text-decoration:none!important;transition:.15s}
    .item:hover{background:rgba(255,255,255,.07);color:#fff}
    .item.on{background:linear-gradient(135deg,rgba(99,102,241,.95),rgba(139,92,246,.95));color:#fff;box-shadow:0 8px 20px rgba(79,70,229,.35)}
    .me{display:flex;align-items:center;gap:11px;padding:14px 8px 4px;border-top:1px solid rgba(255,255,255,.08);margin-top:10px}
    .mi{display:flex;flex-direction:column;flex:1;min-width:0;line-height:1.2}.mi b{color:#fff;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.mi small{color:#8f9bd0}
    .out{border:0;background:rgba(255,255,255,.08);color:#c4cbe6;border-radius:9px;width:34px;height:34px;cursor:pointer;display:flex;align-items:center;justify-content:center}.out:hover{background:rgba(255,255,255,.16);color:#fff}
    .main{flex:1;min-width:0;display:flex;flex-direction:column}
    .top{display:flex;align-items:center;gap:14px;padding:14px 32px;position:sticky;top:0;background:rgba(243,245,251,.85);backdrop-filter:blur(10px);z-index:20;border-bottom:1px solid transparent}
    .bell{position:relative;border:1px solid var(--line);background:#fff;border-radius:12px;width:42px;height:42px;display:flex;align-items:center;justify-content:center;cursor:pointer;color:var(--ink-2);box-shadow:var(--shadow)}
    .cnt{position:absolute;top:-6px;right:-6px;background:#ef4444;color:#fff;border-radius:99px;font-size:11px;font-weight:800;min-width:19px;height:19px;display:flex;align-items:center;justify-content:center;padding:0 5px;border:2px solid var(--bg)}
    .content{flex:1}
    .overlay{position:fixed;inset:0;z-index:40}
    .notif{position:fixed;top:64px;right:28px;width:min(420px,calc(100vw - 28px));max-height:min(620px,calc(100vh - 90px));background:#fff;border-radius:16px;box-shadow:var(--shadow-lg);border:1px solid var(--line);z-index:50;display:flex;flex-direction:column;animation:fadeUp .2s both}
    .notif header{display:flex;justify-content:space-between;align-items:center;padding:14px 18px;border-bottom:1px solid var(--line)}
    .nl{overflow:auto}
    .ni{display:flex;gap:12px;width:100%;text-align:left;border:0;background:#fff;padding:13px 18px;cursor:pointer;border-bottom:1px solid #f0f2f9}.ni:hover{background:#fafbff}
    .ni.unread{background:#f6f7ff}.ni.unread .nt b::after{content:'';display:inline-block;width:7px;height:7px;border-radius:50%;background:#4f46e5;margin-left:8px}
    .nk{width:32px;height:32px;border-radius:10px;display:flex;align-items:center;justify-content:center;flex:none;background:#e8ecff;color:#4f46e5}
    .nk.success{background:#d9f7ea;color:#059669}.nk.danger{background:#fde4e4;color:#dc2626}.nk.warning,.nk.action{background:#fff1d6;color:#c76a00}
    .nt{display:flex;flex-direction:column;gap:2px;font-size:13px;color:#4a5578}.nt b{color:#0f1b3d;font-size:13.5px}.nt small{color:#8590ad}
    @media (max-width:900px){.side{width:72px;padding:16px 8px}.lb,.bt,.grp,.mi{display:none}.item{justify-content:center}.brand{justify-content:center;padding-bottom:14px}.me{justify-content:center;flex-direction:column}.top{padding:12px 16px}}
  `],
})
export class Shell {
  protected readonly auth = inject(Auth);
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly toasts = inject(Toasts);

  protected readonly user = this.auth.user;
  protected readonly items = signal<NotificationItem[]>([]);
  protected readonly panel = signal(false);
  protected readonly mode = signal<'Http' | 'Kafka' | ''>('');
  protected readonly unread = computed(() => this.items().filter(n => !n.read).length);
  private seen: Set<string> | null = null;

  protected readonly roleLabel = computed(() => ({ Claimant: 'Policyholder', ClaimsOfficer: 'Claims officer', Manager: 'Claims manager' }[this.user()?.role ?? 'Claimant']));
  protected readonly groupLabel = computed(() => (this.auth.isClaimant() ? 'My claims' : 'Claims workspace'));

  protected readonly nav = computed<NavItem[]>(() => {
    if (this.auth.isClaimant()) return [
      { label: 'My claims', icon: 'file', link: '/my/claims', opts: EXACT },
      { label: 'Report an incident', icon: 'plus', link: '/my/claims/new', opts: EXACT },
    ];
    const common: NavItem[] = [
      { label: 'Claim queue', icon: 'inbox', link: '/staff/queue', opts: SUBSET },
      { label: 'My work', icon: 'briefcase', link: '/staff/work', opts: SUBSET },
      { label: 'All claims', icon: 'search', link: '/staff/claims', opts: SUBSET },
      { label: 'Team & SLA', icon: 'users', link: '/staff/team', opts: SUBSET },
    ];
    return this.auth.isManager() ? [
      { label: 'Dashboard', icon: 'chart', link: '/manager/dashboard', opts: SUBSET },
      { label: 'Approvals', icon: 'check-circle', link: '/staff/claims', query: { status: 'Assessed' }, opts: SUBSET },
      ...common,
    ] : common;
  });

  constructor() {
    const poll = () => { this.loadNotifications(); };
    poll();
    this.loadMode();
    const a = setInterval(poll, 5000);
    const b = setInterval(() => this.loadMode(), 15000);
    inject(DestroyRef).onDestroy(() => { clearInterval(a); clearInterval(b); });
  }

  private loadNotifications() {
    this.api.notifications(15).subscribe({
      next: list => {
        // announce notifications that arrive while the app is open (not the backlog on first load)
        if (this.seen) for (const n of list) if (!this.seen.has(n.id) && !n.read) this.toasts.info(`${n.title} — ${n.claimNumber}`);
        this.seen = new Set(list.map(n => n.id));
        this.items.set(list);
      },
      error: () => { /* the bell is best-effort */ },
    });
  }

  private loadMode() {
    this.api.health().subscribe({ next: h => this.mode.set(h.messagingProvider === 'Kafka' ? 'Kafka' : 'Http'), error: () => {} });
  }

  protected icon(kind: string) {
    return ({ success: 'check-circle', danger: 'x-circle', warning: 'alert', action: 'message', info: 'info' } as Record<string, string>)[kind] ?? 'info';
  }

  protected open(n: NotificationItem) {
    this.panel.set(false);
    if (!n.read) this.api.markRead(n.id).subscribe({ next: () => this.loadNotifications(), error: () => {} });
    void this.router.navigateByUrl(this.auth.isClaimant() ? `/my/claims/${n.claimId}` : `/staff/claims/${n.claimId}`);
  }

  protected readAll() { this.api.markAllRead().subscribe({ next: () => this.loadNotifications(), error: () => {} }); }
}
