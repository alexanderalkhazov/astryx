import { z } from 'zod';

import type { ApiClient } from './client';

export const NotificationCategory = z.enum(['orders', 'alerts', 'strategy', 'system']);
export type NotificationCategory = z.infer<typeof NotificationCategory>;

/** An Activity feed item (SYSTEM_DESIGN.md §11.10). `deepLink` is an Expo Router path. */
export const ActivityItem = z.object({
  id: z.string(),
  category: NotificationCategory,
  type: z.string(),
  title: z.string(),
  body: z.string(),
  severity: z.enum(['info', 'success', 'warning', 'error']),
  deepLink: z.string(),
  entityType: z.string().nullable(),
  entityId: z.string().nullable(),
  createdAt: z.string(),
  read: z.boolean(),
});
export type ActivityItem = z.infer<typeof ActivityItem>;

export const ActivityPage = z.object({ items: z.array(ActivityItem), nextCursor: z.string().nullable(), unreadCount: z.number().int() });
export type ActivityPage = z.infer<typeof ActivityPage>;

/** Notifications N1–N3. */
export function notificationsApi(client: ApiClient) {
  return {
    list: (category: NotificationCategory | 'all' = 'all', cursor?: string) =>
      client.request(`/api/v1/notifications?category=${category}${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`, { schema: ActivityPage }),
    markRead: (id: string) => client.request(`/api/v1/notifications/${encodeURIComponent(id)}/read`, { method: 'POST' }),
    markAllRead: (category?: NotificationCategory) => client.request('/api/v1/notifications/read-all', { method: 'POST', body: category ? { category } : {} }),
  };
}
