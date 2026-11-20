import { Injectable, inject } from '@angular/core';
import axios from 'axios';
import { AuthService } from './auth.service';

export interface MigrationScript {
  id: number;
  version: string;
  description: string;
  sqlContent: string;
  checksum: string;
  status:
    'draft' | 'validating' | 'validation_failed' | 'pending' | 'approved' | 'applied' | 'rejected';
  createdBy: string;
  createdAt: string;
  submittedAt: string | null;
  reviewedBy: string | null;
  reviewedAt: string | null;
  appliedBy: string | null;
  appliedAt: string | null;
  validationLog: string | null;
  applyLog: string | null;
  rollbackSql: string | null;
  notes: string | null;
}

export interface AuditEntry {
  id: number;
  migrationId: number | null;
  version: string | null;
  action: string;
  performedBy: string;
  performedAt: string;
  details: string | null;
}

@Injectable({ providedIn: 'root' })
export class MigrationsService {
  private auth = inject(AuthService);
  private client = axios.create({ baseURL: '/api' });

  private get headers() {
    const t = this.auth.auth().token;
    return t ? { Authorization: `Bearer ${t}` } : {};
  }

  async list(status?: string): Promise<MigrationScript[]> {
    const p = status ? `?status=${status}` : '';
    return (await this.client.get(`/migrations${p}`, { headers: this.headers })).data;
  }

  async get(id: number): Promise<MigrationScript> {
    return (await this.client.get(`/migrations/${id}`, { headers: this.headers })).data;
  }

  async create(data: {
    version: string;
    description: string;
    sqlContent: string;
    rollbackSql?: string;
    notes?: string;
  }): Promise<MigrationScript> {
    return (await this.client.post('/migrations', data, { headers: this.headers })).data;
  }

  async submit(id: number): Promise<void> {
    await this.client.post(`/migrations/${id}/submit`, {}, { headers: this.headers });
  }

  async approve(id: number): Promise<void> {
    await this.client.post(`/migrations/${id}/approve`, {}, { headers: this.headers });
  }

  async apply(id: number): Promise<void> {
    await this.client.post(`/migrations/${id}/apply`, {}, { headers: this.headers });
  }

  async reject(id: number, reason: string): Promise<void> {
    await this.client.post(`/migrations/${id}/reject`, { reason }, { headers: this.headers });
  }

  async audit(migrationId?: number): Promise<AuditEntry[]> {
    const p = migrationId ? `?migrationId=${migrationId}` : '';
    return (await this.client.get(`/migrations/audit${p}`, { headers: this.headers })).data;
  }
}
