import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Api } from '../../core/api';
import { ClaimDetail, Policy, SubmitClaimBody } from '../../core/models';
import { DayPipe, errorCode, errorText, MoneyPipe, money } from '../../core/ui';
import { Icon } from '../../shared/icon';
import { TypeIcon } from '../../shared/widgets';

const isoDay = (offset: number) => new Date(Date.now() + offset * 86_400_000).toISOString().slice(0, 10);

@Component({
  selector: 'cf-new-claim',
  imports: [FormsModule, RouterLink, Icon, TypeIcon, MoneyPipe, DayPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page narrow">
      @if (created(); as c) {
        <div class="card card-pad success fade-in">
          <span class="big"><cf-icon name="check" [size]="38" /></span>
          <h1>Claim reported</h1>
          <p class="sub">Thanks, we've got it. Your reference number is</p>
          <div class="ref">{{ c.claimNumber }}</div>
          <div class="row gap-2 wrap" style="justify-content:center">
            @if (c.priority !== 'Normal') { <span class="chip" [class.chip-bad]="c.priority === 'Urgent'" [class.chip-warn]="c.priority === 'High'">{{ c.priority }} priority</span> }
            <span class="chip chip-brand">An officer will pick it up shortly</span>
          </div>
          <p class="muted mt-2">You'll get a notification at every step, and you can track progress any time.</p>
          <div class="row gap-2 mt-3" style="justify-content:center">
            <a class="btn btn-primary" [routerLink]="['/my/claims', c.id]">Track my claim <cf-icon name="arrow-right" [size]="16" /></a>
            <a class="btn" routerLink="/my/claims">Back to my claims</a>
          </div>
        </div>
      } @else {
        <div class="page-head"><div><h1>Report an incident</h1><p>Three quick steps — it takes about two minutes.</p></div>
          <a class="btn btn-ghost" routerLink="/my/claims"><cf-icon name="x" [size]="16" /> Cancel</a></div>

        <ol class="wizard">
          @for (s of steps; track s; let i = $index) {
            <li [class.on]="step() === i + 1" [class.done]="step() > i + 1"><span>@if (step() > i + 1) { <cf-icon name="check" [size]="14" /> } @else { {{ i + 1 }} }</span>{{ s }}</li>
          }
        </ol>

        <div class="card card-pad fade-in">
          @switch (step()) {
            @case (1) {
              <h2>Which policy is this about?</h2>
              <p class="sub mb-2">Choose the policy that covers what happened.</p>
              @if (policies() === null) { <div class="skel" style="height:90px"></div> }
              <div class="col">
                @for (p of policies(); track p.policyNumber) {
                  <button type="button" class="pol" [class.sel]="policy()?.policyNumber === p.policyNumber" (click)="choose(p)">
                    <cf-type [type]="p.product" [size]="46" />
                    <span class="grow tl">
                      <b>{{ p.productName }} <span class="mono muted">· {{ p.policyNumber }}</span></b>
                      <small>{{ p.assetDescription }}</small>
                      <small>Valid {{ p.effectiveFrom | d }} – {{ p.effectiveTo | d }} · Cover up to {{ p.coverageLimit | money: p.currency : true }} · Excess {{ p.deductible | money: p.currency }}</small>
                    </span>
                    @if (!p.inForce) { <span class="chip chip-bad">{{ p.status }}</span> }
                    <span class="radio"></span>
                  </button>
                }
              </div>
            }

            @case (2) {
              <h2>What happened?</h2>
              <p class="sub mb-2">The more detail you give now, the fewer questions we'll need to ask later.</p>
              <div class="grid g2">
                <div class="field"><label class="label" for="d">Date of incident</label>
                  <input id="d" class="input" type="date" [max]="today" [ngModel]="date()" (ngModelChange)="date.set($event)" /></div>
                <div class="field"><label class="label" for="l">Where did it happen?</label>
                  <input id="l" class="input" [class.invalid]="touched() && !location().trim()" [ngModel]="location()" (ngModelChange)="location.set($event)" placeholder="e.g. PIE near Toa Payoh exit" /></div>
              </div>
              <div class="field mt-2"><label class="label" for="ds">Describe what happened</label>
                <textarea id="ds" class="textarea" [class.invalid]="touched() && description().trim().length < 20" [ngModel]="description()" (ngModelChange)="description.set($event)" placeholder="Tell us in your own words…"></textarea>
                <div class="row spread"><span class="hint" [class.err]="touched() && description().trim().length < 20">Minimum 20 characters</span><span class="hint">{{ description().length }}/2000</span></div></div>

              @if (policy()?.product === 'Motor') {
                <div class="divider"></div><h3 class="mb-2">Vehicle details</h3>
                <div class="grid g2">
                  <div class="field"><label class="label" for="pl">Vehicle registration</label><input id="pl" class="input" [ngModel]="plate()" (ngModelChange)="plate.set($event)" /></div>
                  <div class="field"><label class="label" for="pr">Police report no. <span class="muted">(optional)</span></label><input id="pr" class="input" [ngModel]="police()" (ngModelChange)="police.set($event)" /></div>
                </div>
                <div class="grid g2 mt-2">
                  <label class="check"><input type="checkbox" [ngModel]="thirdParty()" (ngModelChange)="thirdParty.set($event)" /> Another vehicle or party was involved</label>
                  <label class="check"><input type="checkbox" [ngModel]="drivable()" (ngModelChange)="drivable.set($event)" /> The car is still drivable</label>
                  <label class="check" style="grid-column:1/-1"><input type="checkbox" [ngModel]="injury()" (ngModelChange)="injury.set($event)" /> Someone was injured <span class="muted">— we'll fast-track this claim</span></label>
                </div>
              } @else if (policy()) {
                <div class="divider"></div><h3 class="mb-2">Property details</h3>
                <div class="grid g2">
                  <div class="field"><label class="label" for="ca">What caused the damage?</label>
                    <select id="ca" class="select" [ngModel]="cause()" (ngModelChange)="cause.set($event)">
                      @for (c of causes; track c) { <option [value]="c">{{ c }}</option> }
                    </select></div>
                  <div class="field"><label class="label" for="ad">Property address</label><input id="ad" class="input" [ngModel]="address()" (ngModelChange)="address.set($event)" /></div>
                </div>
                <label class="check mt-2"><input type="checkbox" [ngModel]="habitable()" (ngModelChange)="habitable.set($event)" /> The home is still safe to live in <span class="muted">— untick if you need emergency help</span></label>
              }
            }

            @case (3) {
              <h2>How much are you claiming?</h2>
              <p class="sub mb-2">An estimate is fine — an officer will assess the final amount.</p>
              <div class="field" style="max-width:340px"><label class="label" for="am">Estimated amount</label>
                <div class="input-group"><span class="prefix">{{ policy()?.currency }}</span>
                  <input id="am" class="input" type="number" min="0" step="any" [ngModel]="amount()" (ngModelChange)="amount.set($event)" placeholder="0" /></div>
                <span class="hint">Your policy covers up to {{ policy()?.coverageLimit | money: policy()?.currency }} with an excess of {{ policy()?.deductible | money: policy()?.currency }}.</span></div>

              <div class="divider"></div>
              <h3 class="mb-2">Review</h3>
              <dl class="kv">
                <dt>Policy</dt><dd>{{ policy()?.productName }} · <span class="mono">{{ policy()?.policyNumber }}</span></dd>
                <dt>Incident</dt><dd>{{ date() | d }} · {{ location() }}</dd>
                <dt>What happened</dt><dd>{{ description() }}</dd>
                <dt>Claimed</dt><dd><b>{{ amount() | money: policy()?.currency }}</b></dd>
              </dl>
              @if (error()) {
                <div class="banner banner-bad mt-2"><cf-icon name="alert" [size]="20" /><div class="grow"><b>We couldn't submit your claim</b><div>{{ error() }}</div></div>
                  @if (policyProblem()) { <button type="button" class="btn btn-sm" (click)="step.set(1)">Choose another policy</button> }</div>
              }
            }
          }

          <div class="row spread mt-3">
            <button class="btn" type="button" [disabled]="step() === 1 || busy()" (click)="back()"><cf-icon name="arrow-left" [size]="16" /> Back</button>
            @if (step() < 3) {
              <button class="btn btn-primary" type="button" [disabled]="!canNext()" (click)="next()">Continue <cf-icon name="arrow-right" [size]="16" /></button>
            } @else {
              <button class="btn btn-primary" type="button" [disabled]="busy() || !(amount() && amount()! > 0)" (click)="submit()">
                {{ busy() ? 'Submitting…' : 'Submit claim' }} <cf-icon name="send" [size]="16" /></button>
            }
          </div>
        </div>
      }
    </div>`,
  styles: [`
    .narrow{max-width:880px}
    .wizard{display:flex;list-style:none;padding:0;margin:0 0 18px;gap:10px}
    .wizard li{flex:1;display:flex;align-items:center;gap:10px;padding:11px 14px;background:#fff;border:1px solid var(--line);border-radius:12px;font-weight:600;color:var(--ink-3)}
    .wizard li span{width:26px;height:26px;border-radius:50%;background:#eef1f8;display:flex;align-items:center;justify-content:center;font-size:12.5px;flex:none}
    .wizard li.on{color:var(--brand);border-color:var(--brand);box-shadow:0 0 0 3px rgba(79,70,229,.1)}.wizard li.on span{background:var(--brand);color:#fff}
    .wizard li.done{color:var(--ink)}.wizard li.done span{background:#10b981;color:#fff}
    .pol{display:flex;align-items:center;gap:14px;padding:14px 16px;border:1.5px solid var(--line);border-radius:14px;background:#fff;cursor:pointer;text-align:left;transition:.15s;font:inherit}
    .pol:hover{border-color:#b8bff0}.pol.sel{border-color:var(--brand);background:var(--brand-soft)}
    .tl{display:flex;flex-direction:column;gap:2px}.tl small{color:var(--ink-3)}
    .radio{width:20px;height:20px;border-radius:50%;border:2px solid #cfd6ee;flex:none;position:relative}
    .sel .radio{border-color:var(--brand)}.sel .radio::after{content:'';position:absolute;inset:3px;border-radius:50%;background:var(--brand)}
    .success{text-align:center;padding:48px 30px!important;display:flex;flex-direction:column;align-items:center;gap:10px}
    .big{width:84px;height:84px;border-radius:50%;background:#10b981;color:#fff;display:flex;align-items:center;justify-content:center;animation:pop .5s both;box-shadow:0 14px 34px rgba(16,185,129,.4)}
    .ref{font-size:30px;font-weight:800;letter-spacing:.02em;color:var(--brand);background:var(--brand-soft);padding:8px 22px;border-radius:14px;margin:6px 0}
  `],
})
export class NewClaim {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  protected readonly steps = ['Policy', 'Incident', 'Amount & review'];
  protected readonly causes = ['Water', 'Fire', 'Storm', 'Theft', 'Other'];
  protected readonly today = isoDay(0);

  protected readonly step = signal(1);
  protected readonly policies = signal<Policy[] | null>(null);
  protected readonly policy = signal<Policy | null>(null);
  protected readonly date = signal(isoDay(-1));
  protected readonly location = signal('');
  protected readonly description = signal('');
  protected readonly touched = signal(false);
  protected readonly plate = signal('');
  protected readonly police = signal('');
  protected readonly thirdParty = signal(false);
  protected readonly drivable = signal(true);
  protected readonly injury = signal(false);
  protected readonly cause = signal('Water');
  protected readonly address = signal('');
  protected readonly habitable = signal(true);
  protected readonly amount = signal<number | null>(null);

  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly policyProblem = signal(false);
  protected readonly created = signal<ClaimDetail | null>(null);

  protected readonly step2Valid = computed(() =>
    !!this.date() && this.date() <= this.today && this.location().trim().length > 0 && this.description().trim().length >= 20);
  protected readonly canNext = computed(() => (this.step() === 1 ? this.policy() !== null : this.step2Valid()));

  constructor() {
    this.api.myPolicies().subscribe({ next: p => this.policies.set(p), error: e => { this.policies.set([]); this.error.set(errorText(e)); } });
  }

  protected choose(p: Policy) {
    this.policy.set(p);
    this.plate.set(p.vehicleRegistration ?? '');
    this.address.set(p.propertyAddress ?? '');
    this.error.set('');
  }

  protected next() {
    if (this.step() === 2) { this.touched.set(true); if (!this.step2Valid()) return; }
    this.step.update(s => Math.min(3, s + 1));
  }
  protected back() { this.step.update(s => Math.max(1, s - 1)); this.error.set(''); }

  protected submit() {
    const p = this.policy();
    if (!p || !this.amount()) return;
    const body: SubmitClaimBody = {
      policyNumber: p.policyNumber, type: p.product, incidentDate: this.date(), incidentLocation: this.location().trim(),
      description: this.description().trim(), claimedAmount: Number(this.amount()),
      motor: p.product === 'Motor' ? { vehicleRegistration: this.plate() || null, thirdPartyInvolved: this.thirdParty(), injuryInvolved: this.injury(), policeReportNumber: this.police() || null, drivable: this.drivable() } : null,
      property: p.product === 'Property' ? { propertyAddress: this.address() || null, damageCause: this.cause(), habitable: this.habitable() } : null,
    };
    this.busy.set(true);
    this.error.set('');
    this.api.submitClaim(body).subscribe({
      next: c => { this.created.set(c); this.busy.set(false); },
      error: e => {
        this.busy.set(false);
        this.error.set(errorText(e));
        this.policyProblem.set((errorCode(e) ?? '').startsWith('POLICY_'));
      },
    });
  }

  protected readonly fmt = money;
}
