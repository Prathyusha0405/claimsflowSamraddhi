import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { User } from '../../core/models';
import { errorText, Toasts, usd } from '../../core/ui';
import { Icon } from '../../shared/icon';
import { Avatar } from '../../shared/widgets';

@Component({
  selector: 'cf-login',
  imports: [FormsModule, Icon, Avatar],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="login">
      <section class="hero">
        <div class="blob b1"></div><div class="blob b2"></div>
        <div class="brand"><span class="logo"><cf-icon name="logo" [size]="26" /></span><b>ClaimFlow</b><small>APAC</small></div>
        <h1>Claims that move,<br />not claims that wait.</h1>
        <p class="lead">One platform for policyholders, claims officers and managers across six markets — with every step visible, from first report to payment.</p>
        <ul class="feats">
          <li><span><cf-icon name="eye" [size]="18" /></span><div><b>Always know where your claim is</b><small>Live status tracker, plain-English updates, and answers on demand.</small></div></li>
          <li><span><cf-icon name="zap" [size]="18" /></span><div><b>One queue, clear priorities</b><small>Urgent and high-value claims first, with SLA clocks on every card.</small></div></li>
          <li><span><cf-icon name="shield" [size]="18" /></span><div><b>Exposure in real time</b><small>Outstanding liability by market, driven by an event stream.</small></div></li>
        </ul>
        <div class="tech"><span>Microservices</span><span>Outbox pattern</span><span>HTTP or Kafka events</span><span>.NET 9 · Angular 21</span></div>
      </section>

      <section class="panel">
        <div class="inner">
          <h2>Sign in</h2>
          <p class="sub">Pick a demo persona to explore — no password needed. Or sign in manually with <code>{{ password() }}</code>.</p>

          @if (offline()) {
            <div class="banner banner-bad mt-2">
              <cf-icon name="alert" [size]="20" />
              <div><b>Can't reach the API gateway.</b><div>Start the backend first (<code>scripts/start.ps1</code> or <code>./scripts/start.sh</code>), then refresh.</div></div>
            </div>
          }

          @for (g of groups(); track g.title) {
            <div class="gt">{{ g.title }}</div>
            <div class="people" [class.one]="g.users.length === 1">
              @for (u of g.users; track u.id) {
                <button type="button" class="person" [disabled]="busy()" (click)="signIn(u.email)">
                  <cf-avatar [name]="u.name" [size]="40" />
                  <span class="pi">
                    <b>{{ u.name }}</b>
                    <small>{{ u.blurb }}</small>
                  </span>
                  @if (u.market) { <span class="chip chip-info">{{ u.market }}</span> }
                  @if (u.role !== 'Claimant') { <span class="chip chip-brand">{{ u.role === 'Manager' ? 'Unlimited' : 'USD ' + limit(u) }}</span> }
                </button>
              }
            </div>
          }

          <div class="gt">Or sign in manually</div>
          <div class="form">
            <div class="field"><label class="label" for="em">Email</label><input id="em" class="input" type="email" [ngModel]="email()" (ngModelChange)="email.set($event)" placeholder="mei.tan@claimflow.demo" autocomplete="username" /></div>
            <div class="field"><label class="label" for="pw">Password</label><input id="pw" class="input" type="password" [ngModel]="password()" (ngModelChange)="password.set($event)" autocomplete="current-password" (keyup.enter)="signIn(email())" /></div>
            @if (error()) { <div class="err">{{ error() }}</div> }
            <button class="btn btn-primary btn-block" type="button" [disabled]="busy() || !email()" (click)="signIn(email())">
              {{ busy() ? 'Signing in…' : 'Sign in' }} <cf-icon name="arrow-right" [size]="16" />
            </button>
          </div>
        </div>
      </section>
    </div>`,
  styles: [`
    .login{display:grid;grid-template-columns:minmax(0,1.05fr) minmax(0,1fr);min-height:100vh}
    .hero{position:relative;overflow:hidden;background:linear-gradient(150deg,#0b1437 0%,#1b2a74 55%,#4a2a9c 100%);color:#fff;padding:56px 60px;display:flex;flex-direction:column;justify-content:center;gap:22px}
    .blob{position:absolute;border-radius:50%;filter:blur(70px);opacity:.55}.b1{width:380px;height:380px;background:#6366f1;top:-120px;right:-80px}.b2{width:320px;height:320px;background:#a855f7;bottom:-120px;left:-60px}
    .hero>*:not(.blob){position:relative}
    .brand{display:flex;align-items:center;gap:12px;font-size:22px}.brand small{background:rgba(255,255,255,.16);padding:2px 9px;border-radius:99px;font-weight:700;font-size:12px}
    .logo{width:46px;height:46px;border-radius:14px;background:linear-gradient(135deg,#6366f1,#a855f7);display:flex;align-items:center;justify-content:center;box-shadow:0 10px 24px rgba(99,102,241,.5)}
    h1{font-size:44px;line-height:1.08;letter-spacing:-.025em}
    .lead{font-size:16.5px;color:#cfd6f5;max-width:520px;line-height:1.55}
    .feats{list-style:none;padding:0;margin:8px 0 0;display:flex;flex-direction:column;gap:16px;max-width:520px}
    .feats li{display:flex;gap:14px;align-items:flex-start}.feats li>span{width:38px;height:38px;border-radius:11px;background:rgba(255,255,255,.12);display:flex;align-items:center;justify-content:center;flex:none}
    .feats b{display:block}.feats small{color:#b9c3ee;font-size:13px}
    .tech{display:flex;gap:8px;flex-wrap:wrap;margin-top:10px}.tech span{border:1px solid rgba(255,255,255,.22);border-radius:99px;padding:5px 12px;font-size:12px;color:#d7dcf7;font-weight:600}
    .panel{background:#fff;display:flex;align-items:center;justify-content:center;padding:40px 28px;overflow:auto}
    .inner{width:100%;max-width:520px;display:flex;flex-direction:column;gap:6px}
    .gt{font-size:11.5px;text-transform:uppercase;letter-spacing:.09em;color:#8590ad;font-weight:700;margin:18px 0 6px}
    .people{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px}.people.one{grid-template-columns:1fr}
    .person{display:flex;align-items:center;gap:11px;text-align:left;padding:11px 12px;border:1px solid var(--line);border-radius:13px;background:#fff;cursor:pointer;transition:.16s;position:relative;min-width:0}
    .person:hover:not(:disabled){border-color:var(--brand);box-shadow:0 8px 22px rgba(79,70,229,.14);transform:translateY(-1px)}
    .pi{display:flex;flex-direction:column;min-width:0;flex:1;line-height:1.25}.pi b{color:var(--ink)}.pi small{color:var(--ink-3);font-size:11.5px;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden}
    .person .chip{position:absolute;top:8px;right:8px;font-size:10.5px;padding:1px 7px}
    .person .chip + .chip{top:auto;bottom:8px}
    .form{display:flex;flex-direction:column;gap:12px}
    @media (max-width:960px){.login{grid-template-columns:1fr}.hero{padding:34px 26px}h1{font-size:32px}.feats,.tech{display:none}}
  `],
})
export class Login {
  private readonly api = inject(Api);
  private readonly auth = inject(Auth);
  private readonly router = inject(Router);
  private readonly toasts = inject(Toasts);

  protected readonly users = signal<User[]>([]);
  protected readonly password = signal('Demo@123');
  protected readonly email = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly offline = signal(false);

  protected readonly groups = computed(() => [
    { title: 'Policyholders', users: this.users().filter(u => u.role === 'Claimant') },
    { title: 'Claims officers', users: this.users().filter(u => u.role === 'ClaimsOfficer') },
    { title: 'Claims manager', users: this.users().filter(u => u.role === 'Manager') },
  ].filter(g => g.users.length > 0));

  constructor() {
    if (this.auth.user()) void this.router.navigateByUrl(this.auth.home());
    this.api.demoUsers().subscribe({
      next: d => { this.users.set(d.users); this.password.set(d.password); },
      error: () => this.offline.set(true),
    });
  }

  protected limit(u: User) { return usd(u.authorityLimitUsd).replace('USD ', ''); }

  protected async signIn(email: string) {
    this.busy.set(true);
    this.error.set('');
    try {
      const user = await this.auth.login(email, this.password());
      this.toasts.success(`Welcome back, ${user.name.split(' ')[0]}!`);
      await this.router.navigateByUrl(this.auth.home());
    } catch (e) {
      this.error.set(errorText(e));
    } finally {
      this.busy.set(false);
    }
  }
}
