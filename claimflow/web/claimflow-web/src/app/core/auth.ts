import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { Api } from './api';
import { Role, User } from './models';

const KEY = 'claimflow.session';
interface Session { token: string; expiresAt: string; user: User }

@Injectable({ providedIn: 'root' })
export class Auth {
  private api = inject(Api);
  private router = inject(Router);
  private session = signal<Session | null>(Auth.restore());

  readonly user = computed(() => this.session()?.user ?? null);
  readonly token = computed(() => this.session()?.token ?? null);
  readonly isClaimant = computed(() => this.user()?.role === 'Claimant');
  readonly isStaff = computed(() => this.user()?.role === 'ClaimsOfficer' || this.user()?.role === 'Manager');
  readonly isManager = computed(() => this.user()?.role === 'Manager');

  private static restore(): Session | null {
    try {
      const raw = localStorage.getItem(KEY);
      if (!raw) return null;
      const s = JSON.parse(raw) as Session;
      return new Date(s.expiresAt).getTime() > Date.now() ? s : null;
    } catch { return null; }
  }

  async login(email: string, password: string): Promise<User> {
    const res = await new Promise<Session>((resolve, reject) =>
      this.api.login(email, password).subscribe({ next: r => resolve(r), error: e => reject(e) }));
    this.session.set(res);
    localStorage.setItem(KEY, JSON.stringify(res));
    return res.user;
  }

  logout(navigate = true) {
    this.session.set(null);
    localStorage.removeItem(KEY);
    if (navigate) void this.router.navigateByUrl('/login');
  }

  /** Where each role lands after sign-in. */
  home(): string {
    switch (this.user()?.role) {
      case 'Claimant': return '/my/claims';
      case 'Manager': return '/manager/dashboard';
      case 'ClaimsOfficer': return '/staff/queue';
      default: return '/login';
    }
  }
}

export const authGuard: CanActivateFn = () => {
  const auth = inject(Auth);
  return auth.user() ? true : inject(Router).createUrlTree(['/login']);
};

export const roleGuard = (...roles: Role[]): CanActivateFn => () => {
  const auth = inject(Auth);
  const role = auth.user()?.role;
  if (!role) return inject(Router).createUrlTree(['/login']);
  return roles.includes(role) ? true : inject(Router).createUrlTree([auth.home()]);
};

/** Adds the bearer token to API calls; a 401 anywhere else than the login call means the session is gone. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(Auth);
  const token = auth.token();
  const isLogin = req.url.endsWith('/identity/login');
  const authed = token && req.url.startsWith('/api') && !isLogin ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;
  return next(authed).pipe(catchError((e: unknown) => {
    if (e instanceof HttpErrorResponse && e.status === 401 && !isLogin && auth.user()) auth.logout();
    return throwError(() => e);
  }));
};
