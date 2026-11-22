import { Component, inject, signal, computed, OnInit, HostListener } from '@angular/core';
import { Router, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs/operators';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-header',
  standalone: true,
  template: `
    <header class="header">
      <div class="header-left">
        <div class="title-area">
          <h1 class="page-title">{{ pageTitle() }}</h1>
          <div class="breadcrumb">
            <span>Control Plane</span>
            <span class="separator">/</span>
            <strong>{{ pageTitle() }}</strong>
          </div>
        </div>
      </div>

      <div class="header-right">
        <div class="cluster-status">
          <span class="status-dot"></span>
          <span>Cluster Healthy</span>
        </div>

        <div class="header-divider"></div>

        <div class="user-menu">
          <button class="user-btn" (click)="showMenu.set(!showMenu())">
            <div class="avatar">{{ userInitial() }}</div>

            <div class="user-info">
              <span class="user-name">{{ auth.auth().username }}</span>
              <span class="user-role">{{ auth.auth().role }}</span>
            </div>

            <span class="chevron">⌄</span>
          </button>

          @if (showMenu()) {
            <div class="dropdown">
              <button (click)="logout()">
                <span>↪</span>
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
        height: 72px;
        min-height: 72px;
        padding: 0 28px;
        background: #ffffff;
        border-bottom: 1px solid #e2e8f0;
        display: flex;
        align-items: center;
        justify-content: space-between;
        flex-shrink: 0;
        position: relative;
        z-index: 20;
      }

      .page-title {
        margin: 0 0 5px;
        color: #0f172a;
        font-size: 19px;
        font-weight: 700;
      }

      .breadcrumb {
        display: flex;
        align-items: center;
        gap: 8px;
        font-size: 12px;
        color: #94a3b8;
      }

      .breadcrumb strong {
        color: #475569;
        font-weight: 500;
      }

      .header-right {
        display: flex;
        align-items: center;
        gap: 20px;
      }

      .cluster-status {
        display: flex;
        align-items: center;
        gap: 9px;
        padding: 8px 13px;
        border-radius: 20px;
        background: #ecfdf5;
        color: #15803d;
        font-size: 12px;
        font-weight: 600;
      }

      .status-dot {
        width: 8px;
        height: 8px;
        border-radius: 50%;
        background: #16a34a;
        box-shadow: 0 0 0 3px #bbf7d0;
      }

      .header-divider {
        width: 1px;
        height: 32px;
        background: #e2e8f0;
      }

      .user-menu {
        position: relative;
      }

      .user-btn {
        display: flex;
        align-items: center;
        gap: 11px;
        padding: 6px 10px;
        background: transparent;
        border: 0;
        border-radius: 9px;
        cursor: pointer;
      }

      .user-btn:hover {
        background: #f8fafc;
      }

      .avatar {
        width: 38px;
        height: 38px;
        border-radius: 50%;
        background: linear-gradient(135deg, #2563eb, #6366f1);
        color: white;
        display: grid;
        place-items: center;
        font-weight: 700;
      }

      .user-info {
        display: flex;
        flex-direction: column;
        align-items: flex-start;
        gap: 3px;
      }

      .user-name {
        font-size: 13px;
        color: #0f172a;
        font-weight: 600;
      }

      .user-role {
        font-size: 11px;
        color: #94a3b8;
      }

      .chevron {
        color: #64748b;
        font-size: 18px;
      }

      .dropdown {
        position: absolute;
        top: calc(100% + 10px);
        right: 0;
        min-width: 170px;
        background: white;
        border: 1px solid #e2e8f0;
        border-radius: 9px;
        padding: 5px;
        box-shadow: 0 8px 25px rgba(15, 23, 42, 0.1);
      }

      .dropdown button {
        width: 100%;
        padding: 10px;
        border: 0;
        border-radius: 6px;
        background: transparent;
        color: #dc2626;
        cursor: pointer;
        text-align: left;
      }

      .dropdown button:hover {
        background: #fef2f2;
      }

      @media (max-width: 768px) {
        .header {
          padding: 0 12px;
          height: 64px;
          min-height: 64px;
        }

        .page-title {
          font-size: 16px;
        }

        .header-right {
          gap: 8px;
        }

        .cluster-status {
          padding: 7px;
          font-size: 0;
        }

        .user-info,
        .chevron,
        .header-divider {
          display: none;
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
