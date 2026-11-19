import { Component, inject } from '@angular/core';
import { AuthService } from '../../core/auth.service';
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
        <p>Mərhələ 0 tamamlandı. Növbəti: klaster monitorinqi.</p>
        <div class="cards">
          <div class="card">
            <h3>Cluster</h3>
            <p>—</p>
          </div>
          <div class="card">
            <h3>Replication</h3>
            <p>—</p>
          </div>
          <div class="card">
            <h3>Backups</h3>
            <p>—</p>
          </div>
        </div>
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
      .cards {
        display: grid;
        grid-template-columns: repeat(3, 1fr);
        gap: 1rem;
        margin-top: 2rem;
      }
      .card {
        background: #fff;
        padding: 1.5rem;
        border-radius: 8px;
        box-shadow: 0 2px 8px rgba(0, 0, 0, 0.05);
      }
      .card h3 {
        margin: 0 0 0.5rem;
        color: #475569;
        font-size: 0.9rem;
        text-transform: uppercase;
      }
      .card p {
        margin: 0;
        font-size: 1.5rem;
        color: #94a3b8;
      }
    `,
  ],
})
export class DashboardComponent {
  auth = inject(AuthService);
  private router = inject(Router);

  logout() {
    this.auth.logout();
    this.router.navigate(['/login']);
  }
}
