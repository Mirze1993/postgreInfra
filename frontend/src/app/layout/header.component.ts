import { Component, inject, signal, computed, OnInit, HostListener } from '@angular/core';
import { Router, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs/operators';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-header',
  standalone: true,
  template: `
    <header class="header">
      <div class="left">
        <h1 class="page-title">{{ pageTitle() }}</h1>
        <div class="breadcrumb">
          PG Control Plane / <span>{{ pageTitle() }}</span>
        </div>
      </div>

      <div class="right">
        <div class="status-indicator">
          <span class="dot online"></span>
          <span class="status-text">Cluster Online</span>
        </div>

        <div class="user-menu">
          <button class="user-btn" (click)="showMenu.set(!showMenu())">
            <div class="avatar">{{ userInitial() }}</div>
            <div class="user-info">
              <div class="name">{{ auth.auth().username }}</div>
              <div class="role">{{ auth.auth().role }}</div>
            </div>
            <svg viewBox="0 0 24 24" width="14" height="14">
              <path fill="currentColor" d="M7 10l5 5 5-5z" />
            </svg>
          </button>

          @if (showMenu()) {
            <div class="dropdown">
              <button (click)="logout()">
                <svg viewBox="0 0 24 24" width="14" height="14">
                  <path
                    fill="currentColor"
                    d="M17 7l-1.41 1.41L18.17 11H8v2h10.17l-2.58 2.58L17 17l5-5zM4 5h8V3H4c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h8v-2H4V5z"
                  />
                </svg>
                Çıxış
              </button>
            </div>
          }
        </div>
      </div>
    </header>
  `,
  styles: [
    `
      .header {
        height: 64px;
        min-height: 64px;
        background: #fff;
        border-bottom: 1px solid #e2e8f0;
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: 0 1.5rem;
        flex-shrink: 0;
        box-sizing: border-box;
      }

      @media (max-width: 768px) {
        .header {
          padding: 0 0.75rem;
        }

        .status-indicator {
          display: none;
        }

        .user-info {
          display: none;
        }

        .right {
          gap: 0.5rem;
        }
      }
    `,
  ],
})
export class HeaderComponent implements OnInit {
  auth = inject(AuthService);
  private router = inject(Router);
  showMenu = signal(false);
  currentUrl = signal('/dashboard');

  pageTitle = computed(() => {
    const url = this.currentUrl();
    if (url.includes('dashboard')) return 'Dashboard';
    if (url.includes('backups')) return 'Backups';
    if (url.includes('migrations')) return 'Migrations';
    if (url.includes('settings')) return 'Settings';
    return '';
  });

  userInitial = computed(() => {
    const u = this.auth.auth().username;
    return u ? u.charAt(0).toUpperCase() : '?';
  });

  ngOnInit() {
    this.router.events
      .pipe(filter((e) => e instanceof NavigationEnd))
      .subscribe((e: any) => this.currentUrl.set(e.urlAfterRedirects));
  }

  @HostListener('document:click', ['$event'])
  onDocClick(e: MouseEvent) {
    const target = e.target as HTMLElement;
    if (!target.closest('.user-menu')) this.showMenu.set(false);
  }

  logout() {
    this.auth.logout();
    this.router.navigate(['/login']);
  }
}
