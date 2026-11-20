import { Component, inject } from '@angular/core';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-settings',
  standalone: true,
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h2>Settings</h2>
          <p class="subtitle">Sistem konfiqurasiyası (Mərhələ 6-da doldurulacaq)</p>
        </div>
      </div>

      <div class="card">
        <h3>Cari istifadəçi</h3>
        <div class="info-row">
          <span class="info-label">İstifadəçi</span>
          <span class="info-value">{{ auth.auth().username }}</span>
        </div>
        <div class="info-row">
          <span class="info-label">Rol</span>
          <span class="info-value">{{ auth.auth().role }}</span>
        </div>
      </div>

      <div class="card" style="margin-top:1rem;">
        <h3>Tezliklə əlavə olunacaq</h3>
        <ul class="todo">
          <li>Alert qaydaları (Slack, email)</li>
          <li>RBAC idarəetməsi</li>
          <li>Maintenance mode</li>
          <li>Çoxklaster dəstəyi</li>
        </ul>
      </div>
    </div>
  `,
  styles: [
    `
      .page {
        max-width: 900px;
      }
      .page-header {
        margin-bottom: 1.5rem;
      }
      .page-header h2 {
        margin: 0 0 0.25rem;
        font-size: 1.5rem;
        color: #0f172a;
      }
      .subtitle {
        margin: 0;
        color: #64748b;
        font-size: 0.9rem;
      }
      .card {
        background: #fff;
        padding: 1.5rem;
        border-radius: 10px;
        border: 1px solid #e2e8f0;
      }
      .card h3 {
        margin: 0 0 1rem;
        font-size: 1rem;
        color: #0f172a;
      }
      .info-row {
        display: flex;
        justify-content: space-between;
        padding: 0.5rem 0;
        border-bottom: 1px solid #f1f5f9;
      }
      .info-row:last-child {
        border-bottom: 0;
      }
      .info-label {
        color: #64748b;
        font-size: 0.9rem;
      }
      .info-value {
        color: #0f172a;
        font-weight: 500;
        font-size: 0.9rem;
      }
      .todo {
        margin: 0;
        padding-left: 1.25rem;
        color: #64748b;
        font-size: 0.9rem;
      }
      .todo li {
        padding: 0.25rem 0;
      }
    `,
  ],
})
export class SettingsComponent {
  auth = inject(AuthService);
}
