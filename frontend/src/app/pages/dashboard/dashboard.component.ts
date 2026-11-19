import { Component, inject, signal, OnInit, OnDestroy } from '@angular/core';
import { AuthService } from '../../core/auth.service';
import { ClusterService, ClusterStatus } from '../../core/cluster.service';
import { Router } from '@angular/router';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  template: `
    <div class="layout">
      <header>
        <h1>PG Control Plane</h1>
        <div class="user">
          <span>{{ auth.auth().username }} ({{ auth.auth().role }})</span>
          <button (click)="logout()">Çıxış</button>
        </div>
      </header>
      <main>
        <h2>Dashboard</h2>

        @if (cluster.status(); as s) {
          <!-- Ümumi banner -->
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

          <!-- Node kartları -->
          <!-- Node kartları -->
          <div class="cards">
            @for (node of s.nodes; track node.url) {
              <div class="card" [class.leader]="isLeader(node.role)">
                <h3>{{ node.url }}</h3>
                <p class="role">{{ isLeader(node.role) ? 'PRIMARY' : 'REPLICA' }}</p>
                <p class="state">{{ node.state }}</p>
                <p class="timeline">Timeline: {{ node.timeline }}</p>
              </div>
            }
          </div>

          <!-- Replikasiya -->
          <!-- Replikasiya -->
          <div class="section">
            <h3>Replikasiya</h3>
            @if (s.replication.hasReplica) {
              <p>
                Replay Lag: <strong>{{ formatLag(s.replication) }}</strong>
              </p>
              <p>State: {{ s.replication.state }} ({{ s.replication.syncState }})</p>
            } @else {
              <p class="bad">⚠ Replika qoşulmayıb!</p>
            }
          </div>
        } @else {
          <p>Yüklənir...</p>
        }
      </main>
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
