import { inject } from '@angular/core';
import { CanActivateFn, Router, Routes } from '@angular/router';
import { Auth, authGuard, roleGuard } from './core/auth';
import { Shell } from './layout/shell';

/** "/" sends everybody to their own landing page. */
const toHome: CanActivateFn = () => inject(Router).createUrlTree([inject(Auth).home()]);

const staff = roleGuard('ClaimsOfficer', 'Manager');

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./features/login/login').then(m => m.Login), title: 'Sign in · ClaimFlow' },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', canActivate: [toHome], children: [] },

      // claimant
      { path: 'my/claims', canActivate: [roleGuard('Claimant')], title: 'My claims · ClaimFlow', loadComponent: () => import('./features/claimant/my-claims').then(m => m.MyClaims) },
      { path: 'my/claims/new', canActivate: [roleGuard('Claimant')], title: 'Report an incident · ClaimFlow', loadComponent: () => import('./features/claimant/new-claim').then(m => m.NewClaim) },
      { path: 'my/claims/:id', canActivate: [roleGuard('Claimant')], title: 'Claim · ClaimFlow', loadComponent: () => import('./features/claimant/claim-detail').then(m => m.ClaimantDetail) },

      // claims staff
      { path: 'staff/queue', canActivate: [staff], title: 'Claim queue · ClaimFlow', loadComponent: () => import('./features/staff/queue').then(m => m.Queue) },
      { path: 'staff/work', canActivate: [staff], title: 'My work · ClaimFlow', loadComponent: () => import('./features/staff/my-work').then(m => m.MyWork) },
      { path: 'staff/claims', canActivate: [staff], title: 'All claims · ClaimFlow', loadComponent: () => import('./features/staff/claims-search').then(m => m.ClaimsSearch) },
      { path: 'staff/claims/:id', canActivate: [staff], title: 'Review claim · ClaimFlow', loadComponent: () => import('./features/staff/claim-review').then(m => m.ClaimReview) },
      { path: 'staff/team', canActivate: [staff], title: 'Team & SLA · ClaimFlow', loadComponent: () => import('./features/staff/team').then(m => m.Team) },

      // manager
      { path: 'manager/dashboard', canActivate: [roleGuard('Manager')], title: 'Dashboard · ClaimFlow', loadComponent: () => import('./features/manager/dashboard').then(m => m.Dashboard) },

      // everyone
      { path: 'system', title: 'System · ClaimFlow', loadComponent: () => import('./features/system/system').then(m => m.System) },
    ],
  },
  { path: '**', redirectTo: '' },
];
