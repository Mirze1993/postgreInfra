import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="login-wrap">
      <form class="login-card" (ngSubmit)="onSubmit()">
        <h1>PG Control Plane</h1>
        <p class="subtitle">PostgreSQL HA idarəetmə paneli</p>

        <label>
          İstifadəçi
          <input [(ngModel)]="username" name="username" autocomplete="username" required />
        </label>

        <label>
          Parol
          <input
            [(ngModel)]="password"
            name="password"
            type="password"
            autocomplete="current-password"
            required
          />
        </label>

        @if (error()) {
          <div class="error">{{ error() }}</div>
        }

        <button type="submit" [disabled]="loading()">
          {{ loading() ? 'Yoxlanılır...' : 'Daxil ol' }}
        </button>

        <p class="hint">Demo: admin/admin və ya viewer/viewer</p>
      </form>
    </div>
  `,
  styles: [
    `
      .login-wrap {
        min-height: 100vh;
        display: grid;
        place-items: center;
        background: linear-gradient(135deg, #1e293b, #0f172a);
      }
      .login-card {
        background: #fff;
        padding: 2.5rem;
        border-radius: 12px;
        width: 360px;
        box-shadow: 0 20px 60px rgba(0, 0, 0, 0.3);
      }
      h1 {
        margin: 0 0 0.25rem;
        font-size: 1.5rem;
      }
      .subtitle {
        margin: 0 0 1.5rem;
        color: #64748b;
        font-size: 0.9rem;
      }
      label {
        display: block;
        margin-bottom: 1rem;
        font-size: 0.9rem;
        font-weight: 500;
      }
      input {
        display: block;
        width: 100%;
        padding: 0.6rem;
        margin-top: 0.3rem;
        border: 1px solid #cbd5e1;
        border-radius: 6px;
      }
      button {
        width: 100%;
        padding: 0.7rem;
        background: #2563eb;
        color: #fff;
        border: 0;
        border-radius: 6px;
        font-weight: 600;
        cursor: pointer;
      }
      button:disabled {
        opacity: 0.6;
        cursor: wait;
      }
      .error {
        background: #fef2f2;
        color: #dc2626;
        padding: 0.6rem;
        border-radius: 6px;
        margin-bottom: 1rem;
        font-size: 0.85rem;
      }
      .hint {
        color: #94a3b8;
        font-size: 0.75rem;
        text-align: center;
        margin: 1rem 0 0;
      }
    `,
  ],
})
export class LoginComponent {
  private auth = inject(AuthService);
  private router = inject(Router);

  username = '';
  password = '';
  loading = signal(false);
  error = signal<string | null>(null);

  async onSubmit() {
    this.loading.set(true);
    this.error.set(null);
    try {
      await this.auth.login(this.username, this.password);
      this.router.navigate(['/dashboard']);
    } catch (e: any) {
      this.error.set(e.response?.data?.error ?? 'Giriş alınmadı');
    } finally {
      this.loading.set(false);
    }
  }
}
