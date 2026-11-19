import { Injectable, signal } from '@angular/core';
import axios from 'axios';

interface AuthState {
  token: string | null;
  username: string | null;
  role: string | null;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private state = signal<AuthState>({
    token: localStorage.getItem('token'),
    username: localStorage.getItem('username'),
    role: localStorage.getItem('role'),
  });

  readonly auth = this.state.asReadonly();

  async login(username: string, password: string) {
    const res = await axios.post('/api/auth/login', { username, password });
    const { token, username: u, role } = res.data;
    localStorage.setItem('token', token);
    localStorage.setItem('username', u);
    localStorage.setItem('role', role);
    this.state.set({ token, username: u, role });
  }

  logout() {
    localStorage.clear();
    this.state.set({ token: null, username: null, role: null });
  }

  isAuthenticated(): boolean {
    return !!this.state().token;
  }
}
