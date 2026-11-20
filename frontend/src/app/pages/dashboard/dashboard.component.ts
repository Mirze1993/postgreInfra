import { Component, inject, signal, OnInit, OnDestroy } from '@angular/core';
import { AuthService } from '../../core/auth.service';
import { ClusterService, ClusterStatus } from '../../core/cluster.service';
import { Router } from '@angular/router';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h2>Cluster ümumi vəziyyəti</h2>
          <p class="subtitle">Real vaxt monitorinq · hər 5 saniyədə yenilənir</p>
        </div>
        <button class="btn-secondary" (click)="load()">↻ Yenilə</button>
      </div>

      @if (cluster.status(); as s) {
        <div
          class="banner"
          [class.ok]="s.leaderExists && s.hasQuorum"
          [class.bad]="!s.leaderExists || !s.hasQuorum"
        >
          @if (s.leaderExists) {
            ✅ Klaster işləyir — Lider: <strong>{{ s.leaderName }}</strong>
          } @else {
            ❌ Klasterdə lider yoxdur!
          }
        </div>

        <div class="stats-grid">
          @for (node of s.nodes; track node.url) {
            <div class="stat-card" [class.leader]="isLeader(node.role)">
              <div class="stat-header">
                <div class="stat-title">{{ node.url.replace('http://', '') }}</div>
                <span class="badge" [class.primary]="isLeader(node.role)">
                  {{ isLeader(node.role) ? 'PRIMARY' : 'REPLICA' }}
                </span>
              </div>
              <div class="stat-row">
                <span class="stat-label">Status</span>
                <span class="stat-value">{{ node.state }}</span>
              </div>
              <div class="stat-row">
                <span class="stat-label">Timeline</span>
                <span class="stat-value">{{ node.timeline }}</span>
              </div>
            </div>
          }
        </div>

        <div class="card">
          <h3>Replikasiya</h3>
          @if (s.replication.hasReplica) {
            <div class="metrics-grid">
              <div class="metric">
                <div class="metric-label">Replay Lag</div>
                <div class="metric-value">{{ formatLag(s.replication) }}</div>
              </div>
              <div class="metric">
                <div class="metric-label">State</div>
                <div class="metric-value">{{ s.replication.state }}</div>
              </div>
              <div class="metric">
                <div class="metric-label">Sync Mode</div>
                <div class="metric-value">{{ s.replication.syncState }}</div>
              </div>
            </div>
          } @else {
            <p class="bad">⚠ Replika qoşulmayıb</p>
          }
        </div>
      } @else {
        <div class="loading">Yüklənir...</div>
      }
    </div>
  `,
  styles: [
    `
      .layout {
        min-height: 100vh;
        background: #f8fafc;
      }
      header {
        background: #0f172a;
        color: #fff;
        padding: 1rem 2rem;
        display: flex;
        justify-content: space-between;
        align-items: center;
      }
      header h1 {
        margin: 0;
        font-size: 1.2rem;
      }
      .user {
        display: flex;
        gap: 1rem;
        align-items: center;
      }
      .user button {
        background: #334155;
        color: #fff;
        border: 0;
        padding: 0.4rem 0.8rem;
        border-radius: 4px;
        cursor: pointer;
      }
      main {
        padding: 2rem;
      }
      .banner {
        padding: 1rem 1.5rem;
        border-radius: 8px;
        margin-bottom: 1.5rem;
        font-weight: 500;
      }
      .banner.ok {
        background: #dcfce7;
        color: #166534;
      }
      .banner.bad {
        background: #fef2f2;
        color: #dc2626;
      }
      .cards {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
        gap: 1rem;
      }
      .card {
        background: #fff;
        padding: 1.5rem;
        border-radius: 8px;
        box-shadow: 0 2px 8px rgba(0, 0, 0, 0.05);
        border-left: 4px solid #94a3b8;
      }
      .card.leader {
        border-left-color: #16a34a;
      }
      .card h3 {
        margin: 0 0 0.5rem;
        color: #475569;
        font-size: 0.9rem;
      }
      .role {
        font-weight: 600;
        color: #2563eb;
        margin: 0 0 0.25rem;
      }
      .state {
        color: #64748b;
        font-size: 0.85rem;
        margin: 0 0 0.25rem;
      }
      .timeline {
        color: #94a3b8;
        font-size: 0.8rem;
        margin: 0;
      }
      .section {
        margin-top: 2rem;
        background: #fff;
        padding: 1.5rem;
        border-radius: 8px;
      }
      .bad {
        color: #dc2626;
        font-weight: 500;
      }
    `,
  ],
})
export class DashboardComponent implements OnInit, OnDestroy {
  auth = inject(AuthService);
  cluster = inject(ClusterService);
  private router = inject(Router);
  private timer: any;

  ngOnInit() {
    this.load();
    this.timer = setInterval(() => this.load(), 5000); // hər 5 saniyədə
  }

  ngOnDestroy() {
    clearInterval(this.timer);
  }

  async load() {
    try {
      await this.cluster.fetchStatus();
    } catch {
      /* ignore */
    }
  }
  isLeader(role: string): boolean {
    const r = role.toLowerCase();
    return r === 'primary' || r === 'master' || r === 'leader';
  }

  formatLag(s: ClusterStatus['replication']): string {
    if (!s.hasReplica) return '—';
    if (s.replayLagBytes > 0) {
      const kb = s.replayLagBytes / 1024;
      if (kb < 1024) return `${kb.toFixed(1)} KB`;
      return `${(kb / 1024).toFixed(2)} MB`;
    }
    return '0 (sinxron)';
  }
  logout() {
    this.auth.logout();
    this.router.navigate(['/login']);
  }
}
